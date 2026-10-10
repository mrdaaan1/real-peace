using UnityEngine;
using UnityEngine.AI;

namespace RealPeace
{
    /// <summary>
    /// Один боец отряда: свой NavMeshAgent, своё оружие, здоровье и анимация.
    /// Куда идти — говорит отряд (место в строю), в кого стрелять — тоже отряд (общая цель),
    /// а целится, стреляет очередями, бросает гранату и падает убитым боец сам.
    /// </summary>
    public class Soldier : MonoBehaviour
    {
        public Squad Squad { get; private set; }
        public int Index { get; private set; }
        public Weapon Weapon { get; private set; }
        public SoldierRig Rig { get; private set; }
        public NavMeshAgent Agent { get; private set; }
        public bool Alive { get; private set; } = true;
        public float Hp, MaxHp;
        public Vector3 Position => transform.position;
        public bool Moving { get; private set; }
        public bool Aiming => aimTimer > 0f;
        /// <summary>Пулемёт развёрнут и может стрелять.</summary>
        public bool SetUp => Weapon.SetupTime <= 0f || setupTimer >= Weapon.SetupTime;
        public bool IsGrenadier => Squad.Def.HasAtGrenades && Index == 1;

        Transform ring;
        float cooldown, burstTimer, aimTimer, setupTimer, losTimer, deadTime;
        int burstLeft;
        Unit burstTarget;
        bool losOk;
        Unit losFor;

        public static Soldier Create(Squad squad, int index, Vector3 position)
        {
            var w = squad.Def.WeaponFor(index);
            var rig = SoldierRig.Build(squad.transform, squad.Side, w, squad.Id * 31 + index);
            var s = rig.gameObject.AddComponent<Soldier>();
            s.Squad = squad;
            s.Index = index;
            s.Weapon = w;
            s.Rig = rig;
            s.MaxHp = s.Hp = squad.Def.SoldierHp;
            NavBuilder.Snap(position, NavBuilder.InfantryAgent, out var p, 15f);
            rig.transform.position = p;

            var agent = rig.gameObject.AddComponent<NavMeshAgent>();
            agent.agentTypeID = NavBuilder.InfantryAgent;
            agent.radius = 0.35f;
            agent.height = 1.8f;
            agent.speed = squad.Def.Speed;
            agent.acceleration = 14f;
            agent.angularSpeed = 600f;
            agent.stoppingDistance = 0.25f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.avoidancePriority = 40 + index;
            agent.Warp(p);
            s.Agent = agent;

            // Кольцо выделения под ногами
            var ringGo = Shapes.MeshObject("Ring", rig.transform, Shapes.Ring(0.42f, 0.55f, 24), Shapes.Unlit(Color.white), new Vector3(0f, 0.06f, 0f));
            ringGo.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.ring = ringGo.transform;
            ringGo.SetActive(false);
            s.cooldown = Random.Range(0f, w.Cooldown);
            return s;
        }

        public void SetRing(bool on, Color color)
        {
            if (ring == null || !Alive) { if (ring != null) ring.gameObject.SetActive(false); return; }
            ring.gameObject.SetActive(on);
            if (on) ring.GetComponent<MeshRenderer>().sharedMaterial = Shapes.Unlit(color);
        }

        public void SetVisible(bool visible)
        {
            foreach (var r in Rig.Renderers) if (r != null) r.enabled = visible;
        }

        public void GoTo(Vector3 p)
        {
            if (!Alive || !Agent.isOnNavMesh) return;
            Agent.isStopped = false;
            Agent.SetDestination(p);
        }

        public void Stop()
        {
            if (!Alive || !Agent.isOnNavMesh) return;
            Agent.ResetPath();
        }

        public bool Arrived =>
            !Alive || !Agent.isOnNavMesh || (!Agent.pathPending && (!Agent.hasPath || Agent.remainingDistance <= Agent.stoppingDistance + 0.35f));

        public void TakeDamage(float damage, Unit by)
        {
            if (!Alive) return;
            Hp -= damage;
            Rig.Flinch();
            if (Hp > 0f) return;
            Alive = false;
            deadTime = 0f;
            Agent.enabled = false;
            Rig.SetDeadTilt();
            SetRing(false, Color.white);
            Squad.OnSoldierDied(this, by);
        }

        public void Heal(float amount) => Hp = Mathf.Min(MaxHp, Hp + amount);

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (!Alive)
            {
                Rig.Animate(0f, dt, SoldierPose.Dead, false);
                deadTime += dt;
                // Тела остаются на поле минуту, потом уходят в землю
                if (deadTime > 60f) transform.position += Vector3.down * dt * 0.2f;
                if (deadTime > 70f) Destroy(gameObject);
                return;
            }

            float speed = Agent.velocity.magnitude;
            Moving = speed > 0.25f || (Agent.hasPath && Agent.remainingDistance > Agent.stoppingDistance + 0.35f);
            Agent.speed = Squad.MoveSpeed;

            if (Weapon.SetupTime > 0f)
                setupTimer = Moving ? 0f : setupTimer + dt;

            aimTimer -= dt;
            var target = Squad.TargetFor(Weapon);
            if (target != null) TryFire(target, dt);
            else burstLeft = 0;

            if (IsGrenadier) TryGrenade();

            // Поворот к цели, пока стоим
            if (aimTimer > 0f && burstTarget != null && burstTarget.Alive && speed < 1f)
            {
                Agent.updateRotation = false;
                var dir = burstTarget.Position - Position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 360f * dt);
            }
            else Agent.updateRotation = true;

            // Поза: прижат — лежит; пулемётчик развёрнут — лежит за пулемётом; в укрытии или подавлен — пригнулся
            var pose = SoldierPose.Stand;
            bool stationary = speed < 0.4f;
            if (Squad.Pinned || (Weapon.SetupTime > 0f && SetUp && stationary)) pose = SoldierPose.Prone;
            else if (stationary && (Squad.Suppressed || CoverMap.At(Position) != Cover.None || aimTimer > 0f && Squad.InCombat)) pose = SoldierPose.Crouch;
            else if (Squad.Suppressed && speed > 0.1f) pose = SoldierPose.Crouch;
            Rig.Animate(speed, dt, pose, aimTimer > 0f);
        }

        void TryFire(Unit target, float dt)
        {
            float dist = Vector3.Distance(Position, target.Position);
            if (dist > Weapon.Range) { burstLeft = 0; return; }

            // Линию видимости проверяем не каждый кадр
            losTimer -= dt;
            if (losTimer <= 0f || losFor != target)
            {
                losTimer = Random.Range(0.4f, 0.7f);
                losFor = target;
                losOk = Combat.LineOfSight(Position, target.Position);
            }
            if (!losOk) return;
            aimTimer = 1.2f;
            if (!SetUp) return;
            if (Moving && Weapon.SetupTime > 0f) return;

            if (burstLeft > 0)
            {
                burstTimer -= dt;
                if (burstTimer <= 0f)
                {
                    burstTimer = Weapon.BurstInterval;
                    burstLeft--;
                    FireShot(burstTarget != null && burstTarget.Alive ? burstTarget : target);
                }
                return;
            }

            cooldown -= dt;
            if (cooldown > 0f) return;
            cooldown = Weapon.Cooldown * Random.Range(0.85f, 1.2f) * (Moving ? 1.6f : 1f) * (Squad.Suppressed ? 1.4f : 1f);
            burstTarget = target;
            burstLeft = Weapon.Burst - 1;
            burstTimer = Weapon.BurstInterval;
            FireShot(target);
        }

        void FireShot(Unit target)
        {
            var muzzle = Rig.muzzle.position;
            Squad.RevealTimer = 4f;
            Combat.Shots[(int)Squad.Side]++;
            Effects.MuzzleFlash(muzzle, Weapon.Sound == ShotSound.MachineGun ? 0.35f : 0.25f);
            Sfx.Play(Weapon.Clip, muzzle, Weapon.Sound == ShotSound.Rifle ? 0.55f : 0.45f, Random.Range(0.92f, 1.08f));

            float mult = Squad.Side == Battle.I.PlayerSide ? 1f : GameSettings.I.AiAccuracyMultiplier;

            if (target is Squad enemySquad)
            {
                var victim = enemySquad.RandomSoldier();
                if (victim == null) return;
                float d = Vector3.Distance(Position, victim.Position);
                float p = Combat.HitChance(Weapon, d, Moving, CoverMap.At(victim.Position), false, Squad.Suppression, mult);
                bool hit = Random.value < p;
                var aimAt = victim.Position + Vector3.up * 1.2f;
                var end = hit ? aimAt : aimAt + new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(-1.1f, 0.4f), Random.Range(-1.5f, 1.5f));
                if (Weapon.Tracer || Random.value < 0.35f)
                    Effects.Tracer(muzzle, end, Weapon.Tracer ? Shapes.Hex("#ffcf6a") : Shapes.Hex("#f2e2b0"));
                if (hit) { victim.TakeDamage(Weapon.Damage * Random.Range(0.8f, 1.2f), Squad); Combat.Hits[(int)Squad.Side]++; }
                else if (end.y < MapLayout.Height(end.x, end.z) + 0.6f) Effects.Dust(new Vector3(end.x, MapLayout.Height(end.x, end.z), end.z), 0.45f);
                enemySquad.Suppress(Weapon.Suppression * (hit ? 1.5f : 1f));
            }
            else if (target is Tank tank)
            {
                if (Weapon.Penetration <= 0f) return;
                float d = Vector3.Distance(Position, tank.Position);
                float p = Combat.HitChance(Weapon, d, Moving, Cover.None, true, Squad.Suppression, mult);
                bool hit = Random.value < p;
                var aimAt = tank.Position + Vector3.up * 1.2f;
                var end = hit ? aimAt : aimAt + Random.insideUnitSphere * 3f;
                var from = Position;
                if (Weapon.Sound == ShotSound.Rocket)
                {
                    Projectile.Launch(muzzle, end, d / 70f, 0.2f, true, () => OnAtHit(tank, from, hit, end));
                }
                else
                {
                    Effects.Tracer(muzzle, end, Shapes.Hex("#ffe08a"));
                    OnAtHit(tank, from, hit, end);
                }
            }
        }

        void OnAtHit(Tank tank, Vector3 from, bool hit, Vector3 at)
        {
            if (Weapon.Splash > 0f) Effects.Blast(at, 0.45f, false);
            if (hit && tank != null && tank.Alive) tank.TakeArmorHit(from, Weapon, Squad);
            else if (Weapon.Splash <= 0f) Effects.Dust(new Vector3(at.x, MapLayout.Height(at.x, at.z), at.z), 0.6f);
        }

        /// <summary>Гранатомётчик отряда бросает противотанковую гранату в танк, подошедший вплотную.</summary>
        void TryGrenade()
        {
            var tank = Squad.VehicleTarget as Tank;
            if (Squad.ForcedTarget is Tank forced) tank = forced;
            if (tank == null || !tank.Alive || Squad.GrenadeCooldown > 0f || Squad.Pinned) return;
            var w = UnitDefs.AtGrenade;
            float d = Vector3.Distance(Position, tank.Position);
            if (d > w.Range) return;
            Squad.GrenadeCooldown = w.Cooldown;
            var from = Position;
            float mult = Squad.Side == Battle.I.PlayerSide ? 1f : GameSettings.I.AiAccuracyMultiplier;
            bool hit = Random.value < Combat.HitChance(w, d, Moving, Cover.None, true, Squad.Suppression, mult);
            var end = hit ? tank.Position + Vector3.up * 0.8f : MapLayout.Ground(tank.Position + new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f)));
            aimTimer = 1f;
            Projectile.Launch(Position + Vector3.up * 1.7f, end, 0.9f, 2.5f, false, () =>
            {
                Effects.Blast(end, 0.5f, !hit);
                if (hit && tank != null && tank.Alive) tank.TakeArmorHit(from, w, Squad);
            });
            if (Squad.Side == Battle.I.PlayerSide) Battle.I.Message("Граната в танк!", Squad.Side);
        }
    }
}
