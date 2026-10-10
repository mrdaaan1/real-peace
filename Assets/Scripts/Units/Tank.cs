using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace RealPeace
{
    /// <summary>
    /// Танк: едет по навмешу для техники (мимо пешеходных мостиков и чащи), башня сама наводится на цель,
    /// пушка стреляет бронебойными по технике и осколочными по пехоте, спаренный пулемёт — по пехоте рядом.
    /// Броня зависит от стороны попадания. Подбитый танк горит и остаётся на поле — укрытием и препятствием.
    /// </summary>
    public class Tank : Unit
    {
        TankParts parts;
        NavMeshAgent agent;
        AudioSource engine;
        Transform ring;
        Renderer[] renderers;
        Burner smoke;

        public float Hp { get; private set; }
        Unit gunTarget, coaxTarget;
        float targetTimer, gunCooldown, coaxCooldown, coaxBurstTimer, recoil, wheelSpin;
        int coaxBurst;
        bool losOk;
        float losTimer;
        Unit losFor;

        public override bool IsVehicle => true;
        public override Vector3 Position => transform.position;
        public override float Health01 => Mathf.Clamp01(Hp / Def.Hp);
        public override bool Moving => agent != null && agent.enabled && (agent.velocity.magnitude > 0.3f || (agent.hasPath && agent.remainingDistance > agent.stoppingDistance + 0.5f));
        public override float AttackRange => Def.GunAP.Range;
        public bool EngineDamaged => Hp < Def.Hp * 0.35f;

        public override string Status
        {
            get
            {
                if (!Alive) return "Подбит";
                if (Retreating) return "Отходит на ремонт";
                if (EngineDamaged && Moving) return "Двигатель повреждён";
                if (gunTarget != null) return gunTarget.IsVehicle ? "Дуэль с танком" : "Ведёт бой";
                if (Moving) return AttackMove ? "Наступает" : "Движется";
                return EngineDamaged ? "Двигатель повреждён" : "Ожидает приказа";
            }
        }

        public override IEnumerable<Vector3> PickPoints { get { yield return Position + Vector3.up * 1.2f; } }
        public override float PickRadius => 3.2f;

        // Танк шумный: его замечают издалека, разве что в лесу или за домами
        public override float Concealment => base.Concealment * 1.1f;

        public static Tank Create(Faction side, Vector3 position, Transform parent)
        {
            var def = UnitDefs.Get(side, UnitKind.Tank);
            var go = new GameObject(def.Short + "_" + side);
            go.transform.SetParent(parent, false);
            NavBuilder.Snap(position, NavBuilder.TankAgent, out var p, 20f);
            go.transform.position = p;
            go.transform.rotation = Quaternion.LookRotation(Vector3.forward * -Factions.NorthSign(side));
            var t = go.AddComponent<Tank>();
            t.Side = side;
            t.Def = def;
            t.Hp = def.Hp;
            t.parts = TankModels.Build(go.transform, side);
            t.renderers = go.GetComponentsInChildren<Renderer>();

            var agent = go.AddComponent<NavMeshAgent>();
            agent.agentTypeID = NavBuilder.TankAgent;
            agent.radius = 2.0f;
            agent.height = 2.6f;
            agent.speed = def.Speed;
            agent.acceleration = 2.6f;
            agent.angularSpeed = 48f;
            agent.stoppingDistance = 1.5f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
            agent.avoidancePriority = 20;
            agent.Warp(p);
            t.agent = agent;

            t.engine = go.AddComponent<AudioSource>();
            t.engine.clip = SoundFactory.Engine;
            t.engine.loop = true;
            t.engine.spatialBlend = 1f;
            t.engine.rolloffMode = AudioRolloffMode.Linear;
            t.engine.minDistance = 10f;
            t.engine.maxDistance = 120f;
            t.engine.volume = 0.35f;
            t.engine.dopplerLevel = 0f;
            t.engine.Play();

            var ringGo = Shapes.MeshObject("Ring", go.transform, Shapes.Ring(3.1f, 3.4f, 40), Shapes.Unlit(Color.white), new Vector3(0f, 0.08f, 0f));
            ringGo.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringGo.SetActive(false);
            t.ring = ringGo.transform;
            t.gunCooldown = Random.Range(1f, 3f);
            return t;
        }

        // ---------- Движение ----------

        protected override void MoveTo(Vector3 destination, Vector3 facing)
        {
            if (!agent.isOnNavMesh) return;
            NavBuilder.Snap(destination, NavBuilder.TankAgent, out var p, 15f);
            agent.isStopped = false;
            agent.SetDestination(p);
        }

        protected override void Halt()
        {
            if (agent.isOnNavMesh) agent.ResetPath();
        }

        protected override bool ReachedDestination() =>
            !agent.isOnNavMesh || (!agent.pathPending && (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.6f));

        protected override bool HasEnemyInRange() => gunTarget != null;

        // ---------- Бой ----------

        protected override void Update()
        {
            if (!Alive) return;
            base.Update();
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            agent.speed = Def.Speed * (EngineDamaged ? 0.55f : 1f) * (Retreating ? 1.1f : 1f);
            float speed = agent.velocity.magnitude;
            engine.pitch = Mathf.Lerp(0.75f, 1.25f, speed / Def.Speed) * Mathf.Clamp(Time.timeScale, 0.5f, 1.5f);
            wheelSpin += speed * dt * 140f;
            if (parts.wheels != null)
                for (int i = 0; i < parts.wheels.Length; i++)
                    parts.wheels[i].localRotation = parts.wheelBase[i] * Quaternion.AngleAxis(wheelSpin, parts.wheelAxis);

            targetTimer -= dt;
            if (targetTimer <= 0f)
            {
                targetTimer = Random.Range(0.4f, 0.6f);
                if (ForcedTarget != null && ForcedTarget.Alive) gunTarget = ForcedTarget;
                else if (Retreating) gunTarget = null;
                else
                {
                    gunTarget = FindEnemy(Position, Def.GunAP.Range, u => u.IsVehicle);
                    if (gunTarget == null) gunTarget = FindEnemy(Position, Def.GunHE.Range, u => !u.IsVehicle);
                }
                coaxTarget = FindEnemy(Position, Def.Coax.Range, u => !u.IsVehicle);
            }

            // Башня поворачивается к цели (или обратно по ходу)
            float wantYaw = 0f;
            if (gunTarget != null && gunTarget.Alive)
            {
                var local = transform.InverseTransformPoint(gunTarget.Position);
                wantYaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            }
            float curYaw = parts.turret.localEulerAngles.y;
            float newYaw = Mathf.MoveTowardsAngle(curYaw, wantYaw, Def.TurretSpeed * dt);
            parts.turret.localRotation = Quaternion.Euler(0f, newYaw, 0f);
            bool aligned = Mathf.Abs(Mathf.DeltaAngle(newYaw, wantYaw)) < 2.5f;

            // Откат ствола
            recoil = Mathf.MoveTowards(recoil, 0f, dt * 1.2f);
            var gp = parts.gun.localPosition;
            parts.gun.localPosition = new Vector3(gp.x, gp.y, parts.gunRestZ - recoil * 0.45f);

            gunCooldown -= dt;
            if (gunTarget != null && gunTarget.Alive && aligned && gunCooldown <= 0f)
            {
                losTimer -= dt;
                if (losTimer <= 0f || losFor != gunTarget)
                {
                    losTimer = 0.5f;
                    losFor = gunTarget;
                    losOk = Combat.LineOfSight(Position + Vector3.up * 0.7f, gunTarget.Position);
                }
                if (losOk) FireGun(gunTarget);
            }

            // Спаренный пулемёт: только по пехоте в секторе башни
            coaxCooldown -= dt;
            if (coaxTarget != null && coaxTarget.Alive)
            {
                var dir = coaxTarget.Position - Position;
                dir.y = 0f;
                float off = Vector3.Angle(parts.turret.forward, dir);
                if (off < 20f && Vector3.Distance(Position, coaxTarget.Position) < Def.Coax.Range)
                {
                    if (coaxBurst > 0)
                    {
                        coaxBurstTimer -= dt;
                        if (coaxBurstTimer <= 0f)
                        {
                            coaxBurstTimer = Def.Coax.BurstInterval;
                            coaxBurst--;
                            FireCoax(coaxTarget as Squad);
                        }
                    }
                    else if (coaxCooldown <= 0f && Combat.LineOfSight(Position, coaxTarget.Position))
                    {
                        coaxCooldown = Def.Coax.Cooldown * Random.Range(0.9f, 1.3f);
                        coaxBurst = Def.Coax.Burst;
                    }
                }
            }

            // На базе ремонтируемся
            if (!Moving && Vector3.Distance(Position, MapLayout.Base(Side)) < 32f)
                Hp = Mathf.Min(Def.Hp, Hp + dt * 6f);

            if (smoke != null) smoke.strength = EngineDamaged ? 0.5f : 0f;
        }

        void FireGun(Unit target)
        {
            bool vehicle = target.IsVehicle;
            var w = vehicle ? Def.GunAP : Def.GunHE;
            gunCooldown = w.Cooldown * Random.Range(0.92f, 1.12f) * (Moving ? 1.3f : 1f);
            recoil = 1f;
            RevealTimer = 5f;
            var muzzle = parts.muzzle.position;
            Effects.MuzzleFlash(muzzle, 1.1f);
            Effects.Dust(muzzle + parts.turret.forward * 0.6f, 1.4f);
            Sfx.Play(SoundFactory.Cannon, muzzle, 0.9f, Random.Range(0.92f, 1.05f));

            float mult = Side == Battle.I.PlayerSide ? 1f : GameSettings.I.AiAccuracyMultiplier;
            float d = Vector3.Distance(Position, target.Position);
            var from = Position;

            if (vehicle && target is Tank tank)
            {
                bool hit = Random.value < Combat.HitChance(w, d, Moving, Cover.None, true, 0f, mult);
                var end = hit ? tank.Position + Vector3.up * 1.2f : MapLayout.Ground(tank.Position + new Vector3(Random.Range(-5f, 5f), 0f, Random.Range(-5f, 5f)));
                Effects.Shell(muzzle, end);
                if (hit) tank.TakeArmorHit(from, w, this);
                else Effects.Dust(end, 1.2f);
            }
            else if (target is Squad squad)
            {
                var victim = squad.RandomSoldier();
                if (victim == null) return;
                // Осколочный: попадание — точно в бойца, промах — разрыв в нескольких метрах
                bool hit = Random.value < Combat.HitChance(w, d, Moving, Cover.None, false, 0f, mult);
                var at = victim.Position;
                if (!hit) at += new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f));
                at = MapLayout.Ground(at);
                Effects.Shell(muzzle, at + Vector3.up * 0.3f);
                Combat.Explode(at, w, Side, this);
            }
        }

        void FireCoax(Squad target)
        {
            if (target == null || !target.Alive) return;
            var victim = target.RandomSoldier();
            if (victim == null) return;
            var w = Def.Coax;
            var muzzle = parts.coaxMuzzle.position;
            Effects.MuzzleFlash(muzzle, 0.25f);
            Sfx.Play(w.Clip, muzzle, 0.4f, Random.Range(0.95f, 1.1f));
            float mult = Side == Battle.I.PlayerSide ? 1f : GameSettings.I.AiAccuracyMultiplier;
            float d = Vector3.Distance(Position, victim.Position);
            bool hit = Random.value < Combat.HitChance(w, d, Moving, CoverMap.At(victim.Position), false, 0f, mult);
            var end = victim.Position + Vector3.up * 1.1f;
            if (!hit) end += new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(-1f, 0.3f), Random.Range(-1.5f, 1.5f));
            Effects.Tracer(muzzle, end, Shapes.Hex("#ffcf6a"));
            if (hit) victim.TakeDamage(w.Damage * Random.Range(0.8f, 1.2f), this);
            target.Suppress(w.Suppression);
            RevealTimer = 3f;
        }

        // ---------- Повреждения ----------

        /// <summary>Попадание бронебойным (снаряд, ПТР, граната): с какой стороны, пробил ли.</summary>
        public void TakeArmorHit(Vector3 from, Weapon w, Unit by)
        {
            if (!Alive) return;
            float dmg = Combat.ArmorDamage(this, from, w, out bool pen);
            var at = Position + Vector3.up * 1.3f + (from - Position).normalized * 1.5f;
            if (pen)
            {
                Effects.MuzzleFlash(at, 0.8f);
                Sfx.Play(SoundFactory.Grenade, at, 0.7f, 1.3f);
            }
            else
            {
                Effects.MuzzleFlash(at, 0.4f);
                Sfx.Play(SoundFactory.Ricochet, at, 0.8f, Random.Range(0.9f, 1.15f));
                if (Side == Battle.I.PlayerSide && Random.value < 0.4f) Battle.I.Message(Def.Short + ": рикошет!", Side);
            }
            TakeDamage(dmg, by);
        }

        public void TakeDamage(float damage, Unit by)
        {
            if (!Alive) return;
            bool wasDamaged = EngineDamaged;
            Hp -= damage;
            if (!wasDamaged && EngineDamaged && Hp > 0f)
            {
                if (smoke == null) smoke = Effects.Burning(transform, new Vector3(0f, 1.8f, -2.4f), 0.5f, false);
                if (Side == Battle.I.PlayerSide) Battle.I.Message(Def.Short + ": двигатель повреждён!", Side);
            }
            if (Hp <= 0f) Destroyed();
        }

        void Destroyed()
        {
            Hp = 0f;
            Effects.Blast(Position + Vector3.up * 1.2f, 1.3f, false);
            if (Battle.I != null) Battle.I.LoseTickets(Side, GameSettings.I.ticketsPerTank);
            engine.Stop();
            agent.enabled = false;
            ring.gameObject.SetActive(false);
            // Остов: обгоревшая краска, башню сорвало и повернуло
            var burnt = Shapes.Hex("#2b2723");
            foreach (var r in renderers)
                if (r != null && r.transform != ring)
                {
                    var m = Shapes.Mat(Shapes.Shade(burnt, Random.Range(0.85f, 1.15f)), Surf.Armor);
                    var arr = new Material[r.sharedMaterials.Length];
                    for (int k = 0; k < arr.Length; k++) arr[k] = m;
                    r.sharedMaterials = arr;
                }
            parts.turret.localPosition += new Vector3(Random.Range(-0.3f, 0.3f), 0.25f, Random.Range(-0.3f, 0.3f));
            parts.turret.localRotation *= Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(-60f, 60f), Random.Range(-10f, 10f));
            // Горит долго
            if (smoke != null) Destroy(smoke.gameObject);
            smoke = Effects.Burning(transform, new Vector3(0f, 1.9f, -0.5f), 1.3f, true);
            // Остов — препятствие и укрытие
            var obstacle = gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = new Vector3(3.2f, 2.5f, 6.2f);
            obstacle.center = Vector3.up * 1.2f;
            obstacle.carving = true;
            CoverMap.MarkCircle(new Vector2(Position.x, Position.z), 4.2f, Cover.Heavy);
            foreach (var r in renderers) if (r != null) r.enabled = true;
            Die();
        }

        protected override void ApplyHighlight()
        {
            if (ring == null) return;
            bool own = Battle.I == null || Side == Battle.I.PlayerSide;
            ring.gameObject.SetActive(Alive && (Selected || Hovered));
            ring.GetComponent<MeshRenderer>().sharedMaterial = Shapes.Unlit(Selected
                ? (own ? Shapes.Hex("#7dff6a") : Shapes.Hex("#ff5a4a"))
                : (own ? Shapes.Hex("#e8ffe0") : Shapes.Hex("#ffb0a0")));
        }

        protected override void ApplyVisible(bool visible)
        {
            if (!Alive) return;
            foreach (var r in renderers) if (r != null && r.transform != ring) r.enabled = visible;
            // Мотор врага слышно и в тумане войны — подсказка, что где-то рядом танк
            if (smoke != null) smoke.gameObject.SetActive(visible);
        }
    }
}
