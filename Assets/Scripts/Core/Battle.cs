using System.Collections.Generic;
using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// Состояние боя: юниты обеих сторон, точки контроля, очки победы, живая сила, производство подкреплений,
    /// обнаружение (туман войны), пауза и скорость, лента сообщений, итог.
    /// </summary>
    public class Battle : MonoBehaviour
    {
        public static Battle I { get; private set; }

        public Faction PlayerSide { get; private set; }
        /// <summary>Очки победы в начале и лимит юнитов — с поправкой на карту (на большой карте бой дольше).</summary>
        public float MaxTickets => GameSettings.I.startTickets * MapLayout.Map.TicketScale;
        public int UnitCap => GameSettings.I.unitCap + MapLayout.Map.ExtraUnits;
        public Faction EnemySide => PlayerSide.Other();
        public readonly List<Unit> Units = new List<Unit>();
        public VictoryPoint[] Points { get; private set; }
        public readonly float[] Tickets = new float[2];
        public readonly float[] Manpower = new float[2];
        public readonly int[] Kills = new int[2];   // уничтожено юнитов противника
        public readonly int[] Losses = new int[2];
        public bool Over { get; private set; }
        public Faction Winner { get; private set; }
        public float Elapsed { get; private set; }

        public class Production
        {
            public Faction Side;
            public UnitKind Kind;
            public float Left, Total;
        }

        public readonly List<Production> Queue = new List<Production>();

        public struct Note
        {
            public string Text;
            public float Time;
            public Color Color;
        }

        public readonly List<Note> Notes = new List<Note>();

        // Пауза и скорость
        public bool Paused { get; private set; }
        public float Speed { get; private set; } = 1f;
        static readonly float[] Speeds = { 0.5f, 1f, 1.5f, 2f };

        float spotTimer;

        public static Battle Create(Transform world, Faction player)
        {
            var go = new GameObject("Battle");
            go.transform.SetParent(world, false);
            var b = go.AddComponent<Battle>();
            I = b;
            b.PlayerSide = player;
            var s = GameSettings.I;
            for (int i = 0; i < 2; i++)
            {
                b.Tickets[i] = b.MaxTickets;
                b.Manpower[i] = s.startManpower;
            }
            b.Points = new VictoryPoint[MapLayout.PointXZ.Length];
            for (int i = 0; i < b.Points.Length; i++) b.Points[i] = VictoryPoint.Create(i, go.transform);

            foreach (Faction f in new[] { Faction.USSR, Faction.Germany })
            {
                var basePos = MapLayout.Base(f);
                float front = -Factions.NorthSign(f); // направление к фронту
                // Стартовые силы: три стрелковых отделения, пулемёт, противотанковый расчёт
                b.Spawn(f, UnitKind.Riflemen, basePos + new Vector3(-16f, 0f, front * 22f));
                b.Spawn(f, UnitKind.Riflemen, basePos + new Vector3(0f, 0f, front * 26f));
                b.Spawn(f, UnitKind.Riflemen, basePos + new Vector3(16f, 0f, front * 22f));
                b.Spawn(f, UnitKind.MachineGun, basePos + new Vector3(-8f, 0f, front * 12f));
                b.Spawn(f, UnitKind.AntiTank, basePos + new Vector3(8f, 0f, front * 12f));
            }
            b.Message("Битва за Токмачку началась. Захватите точки контроля!", null);
            return b;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            Time.timeScale = 1f;
        }

        // ---------- Юниты ----------

        public Unit Spawn(Faction side, UnitKind kind, Vector3 position)
        {
            Unit u = kind == UnitKind.Tank
                ? Tank.Create(side, position, transform)
                : (Unit)Squad.Create(side, kind, position, transform);
            Units.Add(u);
            bool visible = side == PlayerSide || !GameSettings.I.fogOfWar;
            u.SetVisibleToPlayer(true);
            if (!visible) u.SetVisibleToPlayer(false);
            return u;
        }

        public void OnUnitDestroyed(Unit u)
        {
            Units.Remove(u);
            Losses[(int)u.Side]++;
            Kills[(int)u.Side.Other()]++;
            if (u.Side == PlayerSide) Message($"Потерян юнит: {u.Def.Name}", u.Side);
            else if (u.VisibleToPlayer || u.IsVehicle) Message($"Уничтожен враг: {u.Def.Name}", PlayerSide);
        }

        public int CountUnits(Faction side)
        {
            int n = 0;
            foreach (var u in Units) if (u.Alive && u.Side == side) n++;
            foreach (var q in Queue) if (q.Side == side) n++;
            return n;
        }

        /// <summary>Потери стоят очков победы.</summary>
        public void LoseTickets(Faction side, float amount) => Tickets[(int)side] = Mathf.Max(0f, Tickets[(int)side] - amount);

        // ---------- Живая сила и подкрепления ----------

        public bool Spend(Faction side, int amount)
        {
            if (Manpower[(int)side] < amount) return false;
            Manpower[(int)side] -= amount;
            return true;
        }

        /// <summary>Заказать подкрепление. Вернёт false и причину, если нельзя.</summary>
        public bool Order(Faction side, UnitKind kind, out string reason)
        {
            var def = UnitDefs.Get(side, kind);
            reason = null;
            if (Over) { reason = "Бой окончен"; return false; }
            if (CountUnits(side) >= UnitCap) { reason = "Достигнут лимит юнитов"; return false; }
            int queued = 0;
            foreach (var q in Queue) if (q.Side == side) queued++;
            if (queued >= 5) { reason = "Очередь подкреплений заполнена"; return false; }
            if (!Spend(side, def.Cost)) { reason = $"Не хватает живой силы ({def.Cost})"; return false; }
            Queue.Add(new Production { Side = side, Kind = kind, Left = def.BuildTime, Total = def.BuildTime });
            return true;
        }

        void TickProduction(float dt)
        {
            foreach (Faction f in new[] { Faction.USSR, Faction.Germany })
            {
                // У каждой стороны производится по одному юниту за раз
                Production first = null;
                foreach (var q in Queue) if (q.Side == f) { first = q; break; }
                if (first == null) continue;
                first.Left -= dt;
                if (first.Left > 0f) continue;
                Queue.Remove(first);
                var spawn = MapLayout.SpawnPoint(f) + new Vector3(Random.Range(-6f, 6f), 0f, 0f);
                var u = Spawn(f, first.Kind, spawn);
                float front = -Factions.NorthSign(f);
                u.OrderMove(MapLayout.Base(f) + new Vector3(Random.Range(-18f, 18f), 0f, front * Random.Range(18f, 30f)), false, false);
                if (f == PlayerSide) Message($"Прибыло подкрепление: {u.Def.Name}", f);
            }
        }

        // ---------- Пауза и скорость ----------

        public void SetPaused(bool value)
        {
            Paused = value;
            ApplyTime();
        }

        public void ChangeSpeed(int dir)
        {
            int i = System.Array.IndexOf(Speeds, Speed);
            if (i < 0) i = 1;
            i = Mathf.Clamp(i + dir, 0, Speeds.Length - 1);
            Speed = Speeds[i];
            ApplyTime();
        }

        public void SetSpeed(float value)
        {
            Speed = value;
            ApplyTime();
        }

        void ApplyTime() => Time.timeScale = Paused || Over ? 0f : Speed;

        // ---------- Сообщения ----------

        public void Message(string text, Faction? about)
        {
            var color = about.HasValue ? (about.Value == PlayerSide ? Shapes.Hex("#c8f0b0") : Shapes.Hex("#ffb0a0")) : Shapes.Hex("#f0e6c8");
            Notes.Add(new Note { Text = text, Time = Time.unscaledTime, Color = color });
            if (Notes.Count > 7) Notes.RemoveAt(0);
        }

        // ---------- Обновление ----------

        void Update()
        {
            float dt = Time.deltaTime;
            if (Over || dt <= 0f) return;
            Elapsed += dt;
            var s = GameSettings.I;

            // Точки контроля: считаем отряды пехоты в радиусе
            int[] owned = new int[2];
            foreach (var p in Points)
            {
                int ussr = 0, germany = 0;
                foreach (var u in Units)
                {
                    if (!u.CanCapture) continue;
                    if ((u.Position - p.Position).sqrMagnitude > s.captureRadius * s.captureRadius) continue;
                    if (u.Side == Faction.USSR) ussr++; else germany++;
                }
                p.Tick(dt, ussr, germany);
                if (p.Owner.HasValue) owned[(int)p.Owner.Value]++;
            }

            // Доход и потеря очков
            for (int i = 0; i < 2; i++)
            {
                var side = (Faction)i;
                float income = (s.baseIncome + s.incomePerPoint * owned[i]) * dt;
                if (side != PlayerSide) income *= s.AiIncomeMultiplier;
                Manpower[i] = Mathf.Min(2000f, Manpower[i] + income);
                int diff = owned[1 - i] - owned[i];
                if (diff > 0) Tickets[i] = Mathf.Max(0f, Tickets[i] - s.bleedPerPointPerSecond * diff * dt);
            }

            TickProduction(dt);

            spotTimer -= dt;
            if (spotTimer <= 0f)
            {
                spotTimer = 0.25f;
                UpdateSpotting();
            }

            CheckVictory();
        }

        /// <summary>
        /// Обнаружение: юнит виден стороне, если её боец близко (дальность обзора × маскировка цели) и есть линия
        /// видимости. Стреляющий выдаёт себя вспышками дальше. Вплотную видно всегда.
        /// </summary>
        void UpdateSpotting()
        {
            foreach (var u in Units)
            {
                if (!u.Alive) continue;
                for (int side = 0; side < 2; side++)
                {
                    if ((int)u.Side == side) { u.SpottedBy[side] = true; continue; }
                    bool seen = false;
                    var up = u.Position;
                    foreach (var v in Units)
                    {
                        if (!v.Alive || (int)v.Side != side) continue;
                        float d = Vector3.Distance(v.Position, up);
                        if (d < 12f) { seen = true; break; }
                        float range = v.Def.Sight * u.Concealment;
                        if (u.RevealTimer > 0f) range = Mathf.Max(range, 75f);
                        if (d > range) continue;
                        if (Combat.LineOfSight(v.Position, up)) { seen = true; break; }
                    }
                    u.SpottedBy[side] = seen;
                }
                bool visible = u.Side == PlayerSide || !GameSettings.I.fogOfWar || u.SpottedBy[(int)PlayerSide];
                u.SetVisibleToPlayer(visible);
            }
        }

        void CheckVictory()
        {
            for (int i = 0; i < 2; i++)
            {
                var side = (Faction)i;
                bool noTickets = Tickets[i] <= 0f;
                bool noArmy = CountUnits(side) == 0 && Manpower[i] < UnitDefs.Get(side, UnitKind.Riflemen).Cost;
                if (noTickets || noArmy)
                {
                    Over = true;
                    Winner = side.Other();
                    Message(Winner == PlayerSide ? "ПОБЕДА!" : "Поражение…", Winner);
                    ApplyTime();
                    return;
                }
            }
        }
    }
}
