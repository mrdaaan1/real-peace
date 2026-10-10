using UnityEngine;

namespace RealPeace
{
    public enum Cover : byte { None = 0, Light = 1, Heavy = 2 }

    /// <summary>
    /// Сетка укрытий с шагом 1 м на всю карту. Лёгкое укрытие — лес, изгородь, забор, воронка;
    /// тяжёлое — каменная стена, мешки с песком, стена дома, остов танка.
    /// Укрытие снижает шанс попадания и подавление; солдаты по приказу сами ищут клетку с укрытием.
    /// </summary>
    public static class CoverMap
    {
        const int N = (int)MapLayout.Size;
        static byte[] cells;

        public static void Build()
        {
            cells = new byte[N * N];
            // Лес: лёгкое укрытие внутри
            foreach (var f in MapLayout.Forests)
                MarkCircle(f.center, f.radius * 0.95f, Cover.Light);
            // Линейные препятствия: укрытие вдоль них с обеих сторон
            foreach (var b in MapLayout.Barriers)
            {
                var k = b.kind == BarrierKind.StoneWall || b.kind == BarrierKind.Sandbags ? Cover.Heavy : Cover.Light;
                MarkSegment(b.a, b.b, 1.8f, k);
            }
            // Стены домов: тяжёлое укрытие по периметру
            foreach (var b in MapLayout.Buildings)
            {
                var rot = Quaternion.Euler(0f, b.yaw, 0f);
                float hx = b.size.x * 0.5f, hz = b.size.z * 0.5f;
                Vector2 C(float x, float z)
                {
                    var p = b.center + rot * new Vector3(x, 0f, z);
                    return new Vector2(p.x, p.z);
                }
                var c00 = C(-hx, -hz); var c10 = C(hx, -hz); var c11 = C(hx, hz); var c01 = C(-hx, hz);
                var k = b.kind == BuildingKind.Ruin ? Cover.Light : Cover.Heavy;
                MarkSegment(c00, c10, hx > 3f ? 1.8f : 1.5f, k);
                MarkSegment(c10, c11, 1.8f, k);
                MarkSegment(c11, c01, 1.8f, k);
                MarkSegment(c01, c00, 1.8f, k);
            }
        }

        public static Cover At(Vector3 p)
        {
            if (cells == null) return Cover.None;
            int x = Mathf.FloorToInt(p.x + MapLayout.Half), z = Mathf.FloorToInt(p.z + MapLayout.Half);
            if (x < 0 || z < 0 || x >= N || z >= N) return Cover.None;
            return (Cover)cells[z * N + x];
        }

        /// <summary>Множитель шанса попадания по солдату в этой клетке.</summary>
        public static float HitMultiplier(Cover c) => c == Cover.Heavy ? 0.4f : c == Cover.Light ? 0.7f : 1f;

        public static void MarkCircle(Vector2 center, float radius, Cover kind)
        {
            if (cells == null) return;
            int x0 = Mathf.FloorToInt(center.x - radius + MapLayout.Half), x1 = Mathf.CeilToInt(center.x + radius + MapLayout.Half);
            int z0 = Mathf.FloorToInt(center.y - radius + MapLayout.Half), z1 = Mathf.CeilToInt(center.y + radius + MapLayout.Half);
            for (int z = Mathf.Max(0, z0); z <= Mathf.Min(N - 1, z1); z++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(N - 1, x1); x++)
                {
                    float dx = x + 0.5f - MapLayout.Half - center.x, dz = z + 0.5f - MapLayout.Half - center.y;
                    if (dx * dx + dz * dz <= radius * radius) Raise(x, z, kind);
                }
        }

        static void MarkSegment(Vector2 a, Vector2 b, float width, Cover kind)
        {
            float len = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / 0.5f));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, (float)i / steps);
                int px = Mathf.FloorToInt(p.x + MapLayout.Half), pz = Mathf.FloorToInt(p.y + MapLayout.Half);
                int r = Mathf.CeilToInt(width);
                for (int z = pz - r; z <= pz + r; z++)
                    for (int x = px - r; x <= px + r; x++)
                    {
                        if (x < 0 || z < 0 || x >= N || z >= N) continue;
                        float dx = x + 0.5f - MapLayout.Half - p.x, dz = z + 0.5f - MapLayout.Half - p.y;
                        if (dx * dx + dz * dz <= width * width) Raise(x, z, kind);
                    }
            }
        }

        static void Raise(int x, int z, Cover kind)
        {
            int i = z * N + x;
            if (cells[i] < (byte)kind) cells[i] = (byte)kind;
        }

        /// <summary>
        /// Лучшая клетка с укрытием рядом с желаемой точкой (в радиусе searchRadius), не занятая другими.
        /// Ищем по кольцам: ближе — лучше, тяжёлое укрытие важнее лёгкого.
        /// </summary>
        public static Vector3 FindCoverNear(Vector3 want, float searchRadius, System.Func<Vector3, bool> free)
        {
            var best = want;
            float bestScore = (float)At(want) * 10f - 0.01f;
            if (At(want) != Cover.None && free(want)) return want;
            int r = Mathf.CeilToInt(searchRadius);
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    float d2 = dx * dx + dz * dz;
                    if (d2 > searchRadius * searchRadius) continue;
                    var p = new Vector3(want.x + dx, want.y, want.z + dz);
                    var c = At(p);
                    if (c == Cover.None) continue;
                    float score = (float)c * 10f - Mathf.Sqrt(d2) * 2f;
                    if (score > bestScore && free(p) && !MapLayout.InWater(p.x, p.z))
                    {
                        bestScore = score;
                        best = p;
                    }
                }
            return best;
        }
    }
}
