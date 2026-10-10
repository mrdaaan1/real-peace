using System.Collections.Generic;
using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// Отряд пехоты: стрелки, пулемётный расчёт, противотанковый расчёт. Командуют отрядом целиком:
    /// он расставляет бойцов в цепь у точки приказа (каждый ищет укрытие рядом), выбирает общую цель,
    /// копит подавление, отступает на базу и там пополняется. Захватывает точки.
    /// </summary>
    public class Squad : Unit
    {
        public readonly List<Soldier> Soldiers = new List<Soldier>();
        public float Suppression { get; private set; }
        public bool Suppressed => Suppression > 0.45f;
        public bool Pinned => Suppression > 0.85f && !Retreating;
        public float GrenadeCooldown;
        public Unit InfantryTarget { get; private set; }
        public Unit VehicleTarget { get; private set; }
        public bool InCombat => InfantryTarget != null || VehicleTarget != null;

        float targetTimer, reinforceTimer;
        Vector3 cachedPos;
        int cachedFrame = -1;
        float maxRange;
        bool hasAt;

        public static Squad Create(Faction side, UnitKind kind, Vector3 position, Transform parent)
        {
            var def = UnitDefs.Get(side, kind);
            var go = new GameObject(def.Short + "_" + side);
            go.transform.SetParent(parent, false);
            var sq = go.AddComponent<Squad>();
            sq.Side = side;
            sq.Def = def;
            foreach (var w in def.Weapons)
            {
                sq.maxRange = Mathf.Max(sq.maxRange, w.Range);
                if (w.VsVehicles) sq.hasAt = true;
            }
            if (def.HasAtGrenades) sq.hasAt = true;
            var facing = Vector3.forward * -Factions.NorthSign(side);
            for (int i = 0; i < def.Soldiers; i++)
                sq.Soldiers.Add(Soldier.Create(sq, i, position + Slot(i, def.Soldiers, facing)));
            return sq;
        }

        /// <summary>Место бойца в цепи: шеренга поперёк направления движения, края чуть сзади.</summary>
        static Vector3 Slot(int i, int count, Vector3 facing)
        {
            var right = Vector3.Cross(Vector3.up, facing).normalized;
            float x = (i - (count - 1) * 0.5f) * 2.3f;
            return right * x - facing * Mathf.Abs(x) * 0.25f;
        }

        public override Vector3 Position
        {
            get
            {
                if (cachedFrame == Time.frameCount) return cachedPos;
                var sum = Vector3.zero;
                int n = 0;
                foreach (var s in Soldiers)
                    if (s != null && s.Alive) { sum += s.Position; n++; }
                if (n > 0) cachedPos = sum / n;
                cachedFrame = Time.frameCount;
                return cachedPos;
            }
        }

        public override float Health01
        {
            get
            {
                float hp = 0f;
                foreach (var s in Soldiers) if (s.Alive) hp += s.Hp;
                return hp / (Def.Soldiers * Def.SoldierHp);
            }
        }

        public override bool Moving
        {
            get
            {
                foreach (var s in Soldiers) if (s.Alive && s.Moving) return true;
                return false;
            }
        }

        public override bool CanCapture => Alive && !Retreating && Soldiers.Count > 0;
        public override float AttackRange => maxRange;

        public float MoveSpeed => Def.Speed * (Retreating ? 1.35f : Suppressed ? 0.55f : 1f) * (Pinned ? 0f : 1f);

        public override string Status
        {
            get
            {
                if (Retreating) return "Отступает";
                if (Pinned) return "Прижат к земле";
                if (Suppressed) return "Подавлен";
                if (Def.Kind == UnitKind.MachineGun && InCombat && !Moving && !Soldiers[0].SetUp) return "Разворачивает пулемёт";
                if (InCombat) return "Ведёт бой";
                if (Moving) return AttackMove ? "Наступает" : "Движется";
                int inCover = 0;
                foreach (var s in Soldiers) if (CoverMap.At(s.Position) != Cover.None) inCover++;
                if (inCover * 2 >= Soldiers.Count) return "В укрытии";
                return "Ожидает приказа";
            }
        }

        public override IEnumerable<Vector3> PickPoints
        {
            get { foreach (var s in Soldiers) if (s.Alive) yield return s.Position + Vector3.up * 0.9f; }
        }

        public override float PickRadius => 0.9f;

        public override float Concealment
        {
            get
            {
                float c = base.Concealment;
                if (!Moving && CoverMap.At(Position) == Cover.Heavy) c *= 0.8f;
                if (Pinned) c *= 0.8f;
                return c;
            }
        }

        public Soldier RandomSoldier()
        {
            if (Soldiers.Count == 0) return null;
            return Soldiers[Random.Range(0, Soldiers.Count)];
        }

        /// <summary>Цель для конкретного оружия: противотанковое — по технике, остальное — по пехоте.</summary>
        public Unit TargetFor(Weapon w)
        {
            if (ForcedTarget != null && ForcedTarget.Alive)
            {
                if (ForcedTarget.IsVehicle && w.VsVehicles) return ForcedTarget;
                if (!ForcedTarget.IsVehicle && w.VsInfantry) return ForcedTarget;
            }
            if (Retreating) return null;
            if (w.VsVehicles && !w.VsInfantry) return VehicleTarget;
            return w.VsInfantry ? InfantryTarget : null;
        }

        public void Suppress(float amount)
        {
            if (Retreating) amount *= 0.3f;
            int inCover = 0;
            foreach (var s in Soldiers) if (CoverMap.At(s.Position) == Cover.Heavy) inCover++;
            if (Soldiers.Count > 0 && inCover * 2 >= Soldiers.Count) amount *= 0.6f;
            Suppression = Mathf.Clamp01(Suppression + amount);
        }

        // ---------- Движение ----------

        protected override void MoveTo(Vector3 destination, Vector3 facing)
        {
            // Бойцы встают цепью у точки приказа; каждый ищет поблизости укрытие, не занятое соседом
            var taken = new List<Vector3>();
            for (int i = 0; i < Soldiers.Count; i++)
            {
                var want = destination + Slot(i, Soldiers.Count, facing);
                want = MapLayout.Ground(want);
                var spot = Retreating ? want : CoverMap.FindCoverNear(want, 4.5f, p =>
                {
                    foreach (var t in taken) if ((t - p).sqrMagnitude < 1.6f * 1.6f) return false;
                    return true;
                });
                NavBuilder.Snap(spot, NavBuilder.InfantryAgent, out var snapped, 6f);
                taken.Add(snapped);
                Soldiers[i].GoTo(snapped);
            }
        }

        protected override void Halt()
        {
            foreach (var s in Soldiers) s.Stop();
        }

        protected override bool ReachedDestination()
        {
            foreach (var s in Soldiers)
                if (!s.Arrived) return false;
            return true;
        }

        protected override bool HasEnemyInRange() => InCombat;

        // ---------- Обновление ----------

        protected override void Update()
        {
            if (!Alive) return;
            float dt = Time.deltaTime;
            base.Update();
            if (dt <= 0f) return;

            Suppression = Mathf.Max(0f, Suppression - dt * (Pinned ? 0.07f : 0.11f));
            if (GrenadeCooldown > 0f) GrenadeCooldown -= dt;

            targetTimer -= dt;
            if (targetTimer <= 0f)
            {
                targetTimer = Random.Range(0.4f, 0.6f);
                var pos = Position;
                InfantryTarget = FindEnemy(pos, maxRange + 2f, u => !u.IsVehicle);
                VehicleTarget = hasAt ? FindEnemy(pos, maxRange + 2f, u => u.IsVehicle) : null;
            }

            // На базе: лечимся и пополняемся живой силой
            if (Vector3.Distance(Position, MapLayout.Base(Side)) < 30f && !Moving)
            {
                foreach (var s in Soldiers) s.Heal(dt * 8f);
                reinforceTimer -= dt;
                if (reinforceTimer <= 0f)
                {
                    reinforceTimer = 3f;
                    if (Soldiers.Count < Def.Soldiers && Battle.I.Spend(Side, ReinforceCost))
                    {
                        var s = Soldier.Create(this, FreeIndex(), Position + Random.insideUnitSphere * 2f);
                        Soldiers.Add(s);
                        s.SetVisible(VisibleToPlayer);
                        ApplyHighlight();
                    }
                }
            }
        }

        public int ReinforceCost => Mathf.RoundToInt(Def.Cost / (float)Def.Soldiers * 0.6f);

        int FreeIndex()
        {
            for (int i = 0; i < Def.Soldiers; i++)
            {
                bool used = false;
                foreach (var s in Soldiers) if (s.Index == i) used = true;
                if (!used) return i;
            }
            return Soldiers.Count;
        }

        public void OnSoldierDied(Soldier s, Unit by)
        {
            Soldiers.Remove(s);
            Suppress(0.12f);
            if (Battle.I != null) Battle.I.LoseTickets(Side, GameSettings.I.ticketsPerSoldier);
            if (Soldiers.Count == 0) Die();
        }

        protected override void ApplyHighlight()
        {
            var own = Battle.I == null || Side == Battle.I.PlayerSide;
            var color = Selected ? (own ? Shapes.Hex("#7dff6a") : Shapes.Hex("#ff5a4a")) : Hovered ? (own ? Shapes.Hex("#e8ffe0") : Shapes.Hex("#ffb0a0")) : Color.white;
            foreach (var s in Soldiers) s.SetRing(Selected || Hovered, color);
        }

        public void RefreshHighlight() => ApplyHighlight();

        protected override void ApplyVisible(bool visible)
        {
            foreach (var s in Soldiers) s.SetVisible(visible);
        }

        protected override void OnRetreatArrived()
        {
            base.OnRetreatArrived();
            Suppression = 0f;
        }
    }
}
