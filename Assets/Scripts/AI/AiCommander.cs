using System.Collections.Generic;
using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// ИИ противника. Раз в пару секунд: покупает подкрепления (пехота, пулемёты, ПТ, танки — по обстановке),
    /// отводит на базу потрёпанные и прижатые отряды, держит по отряду на своих точках, остальных
    /// посылает с атакой на нейтральные и вражеские точки (ближние — в первую очередь), противотанковые
    /// расчёты натравливает на замеченные танки.
    /// </summary>
    public class AiCommander : MonoBehaviour
    {
        Faction side;
        float thinkTimer;
        readonly Dictionary<Unit, int> assignment = new Dictionary<Unit, int>();
        readonly Dictionary<Unit, float> pinnedTime = new Dictionary<Unit, float>();

        public static AiCommander Create(Transform parent, Faction side)
        {
            var go = new GameObject("AI_" + side);
            go.transform.SetParent(parent, false);
            var ai = go.AddComponent<AiCommander>();
            ai.side = side;
            ai.thinkTimer = 2f;
            return ai;
        }

        void Update()
        {
            var battle = Battle.I;
            if (battle == null || battle.Over) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            foreach (var u in battle.Units)
                if (u.Side == side && u is Squad sq)
                    pinnedTime[u] = sq.Pinned ? (pinnedTime.TryGetValue(u, out var t) ? t + dt : dt) : 0f;

            thinkTimer -= dt;
            if (thinkTimer > 0f) return;
            thinkTimer = Random.Range(1.8f, 2.8f);
            Think(battle);
        }

        void Think(Battle battle)
        {
            var mine = new List<Unit>();
            var enemyTanks = new List<Unit>();
            int enemyTankCount = 0;
            foreach (var u in battle.Units)
            {
                if (!u.Alive) continue;
                if (u.Side == side) mine.Add(u);
                else if (u.IsVehicle)
                {
                    enemyTankCount++;
                    if (u.SpottedBy[(int)side]) enemyTanks.Add(u);
                }
            }
            var dead = new List<Unit>();
            foreach (var kv in assignment) if (kv.Key == null || !kv.Key.Alive) dead.Add(kv.Key);
            foreach (var d in dead) { assignment.Remove(d); pinnedTime.Remove(d); }

            Purchase(battle, mine, enemyTankCount);

            // Отвод потрёпанных
            foreach (var u in mine)
            {
                if (u.Retreating) continue;
                // Отводим, когда от отряда осталась треть (и бойцы ранены) или танк едва жив
                bool hurt = u is Squad sq
                    ? sq.Soldiers.Count <= Mathf.Max(1, u.Def.Soldiers / 3) && u.Health01 < 0.3f
                    : u.Health01 < 0.25f;
                bool pinnedLong = pinnedTime.TryGetValue(u, out var pt) && pt > 6f;
                bool farFromBase = Vector3.Distance(u.Position, MapLayout.Base(side)) > 40f;
                if ((hurt || pinnedLong) && farFromBase)
                {
                    u.OrderRetreat();
                    assignment.Remove(u);
                }
            }

            // Противотанковые — на ближайший замеченный танк
            foreach (var u in mine)
            {
                if (u.Retreating || u.Def.Kind != UnitKind.AntiTank || enemyTanks.Count == 0) continue;
                Unit best = null;
                float bestD = 90f;
                foreach (var t in enemyTanks)
                {
                    float d = Vector3.Distance(u.Position, t.Position);
                    if (d < bestD) { bestD = d; best = t; }
                }
                if (best != null && u.ForcedTarget != best) u.OrderAttack(best);
            }

            // Распределение по точкам
            var points = battle.Points;
            int[] garrison = new int[points.Length];
            int[] attackers = new int[points.Length];
            foreach (var kv in assignment)
            {
                if (kv.Key == null) continue;
                if (points[kv.Value].Owner == side) garrison[kv.Value]++; else attackers[kv.Value]++;
            }

            foreach (var u in mine)
            {
                if (u.Retreating || u.ForcedTarget != null) continue;
                bool idle = u.Waypoints.Count == 0 && !u.Moving;
                bool inCombat = u is Squad sq && sq.InCombat;
                if (assignment.TryGetValue(u, out int current))
                {
                    var cp = points[current];
                    bool held = cp.Owner == side && !cp.Contested;
                    if (!held)
                    {
                        // Точка ещё не взята: идём к ней или уже захватываем/обороняем — не трогаем
                        if (!idle) continue;
                        if (Vector3.Distance(u.Position, cp.Position) < GameSettings.I.captureRadius + 4f) continue;
                        attackers[current]--;
                    }
                    else
                    {
                        // Точка наша: один отряд пехоты остаётся гарнизоном, остальные идут дальше
                        if (!u.IsVehicle && garrison[current] <= 1) continue;
                        if (!idle) continue;
                        garrison[current]--;
                    }
                }
                if (inCombat && u.Waypoints.Count == 0) continue;

                int target = ChoosePoint(u, points, attackers, garrison);
                if (target < 0) continue;
                assignment[u] = target;
                if (points[target].Owner == side) garrison[target]++; else attackers[target]++;
                var offset = new Vector3(Random.Range(-7f, 7f), 0f, Random.Range(-7f, 7f));
                // Танки держатся чуть позади пехоты
                if (u.IsVehicle) offset += (MapLayout.Base(side) - points[target].Position).normalized * 14f;
                u.OrderMove(points[target].Position + offset, false, true);
            }
        }

        int ChoosePoint(Unit u, VictoryPoint[] points, int[] attackers, int[] garrison)
        {
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                float d = Vector3.Distance(u.Position, p.Position);
                float score = d;
                if (p.Owner == side)
                {
                    if (p.Contested) score -= 40f;          // своя точка под атакой — на помощь
                    else if (garrison[i] >= 1) score += 400f; // уже есть гарнизон
                    else score += 30f;
                }
                else
                {
                    if (p.Owner.HasValue) score -= 10f;     // вражеская — чуть важнее нейтральной
                    score += attackers[i] * 35f;          // распределяем силы между точками
                }
                score += Random.Range(0f, 20f);
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        void Purchase(Battle battle, List<Unit> mine, int enemyTanks)
        {
            int queued = 0;
            foreach (var q in battle.Queue) if (q.Side == side) queued++;
            if (queued > 0) return;

            int rifle = 0, mg = 0, at = 0, tank = 0;
            foreach (var u in mine)
                switch (u.Def.Kind)
                {
                    case UnitKind.Riflemen: rifle++; break;
                    case UnitKind.MachineGun: mg++; break;
                    case UnitKind.AntiTank: at++; break;
                    default: tank++; break;
                }

            UnitKind want;
            if (enemyTanks > at) want = UnitKind.AntiTank;
            else if (rifle < 3) want = UnitKind.Riflemen;
            else if (tank < 2 && battle.Elapsed > 90f) want = UnitKind.Tank;
            else if (mg < 2) want = UnitKind.MachineGun;
            else want = Random.value < 0.6f ? UnitKind.Riflemen : UnitKind.Tank;

            var def = UnitDefs.Get(side, want);
            // Копим на танк, не тратя на мелочь
            if (battle.Manpower[(int)side] < def.Cost) return;
            battle.Order(side, want, out _);
        }
    }
}
