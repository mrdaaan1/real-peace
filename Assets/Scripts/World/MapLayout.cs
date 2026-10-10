using System.Collections.Generic;
using UnityEngine;

namespace RealPeace
{
    /// <summary>Дом/постройка: центр на земле, размер (ширина X, высота Y, длина Z), поворот вокруг Y.</summary>
    public struct Building
    {
        public Vector3 center;
        public Vector3 size;
        public float yaw;
        public BuildingKind kind;
    }

    public enum BuildingKind { Izba, Barn, Church, Mill, Ruin, Pillbox }

    /// <summary>Линейное препятствие-укрытие: живая изгородь, каменная стена, мешки с песком.</summary>
    public struct Barrier
    {
        public Vector2 a, b;
        public BarrierKind kind;
    }

    public enum BarrierKind { Hedge, StoneWall, Sandbags, Fence }

    public struct Forest
    {
        public Vector2 center;
        public float radius;
    }

    public struct Field
    {
        public Rect rect;
        public int kind; // 0 — пшеница, 1 — пашня, 2 — луг с цветами
    }

    /// <summary>
    /// Вся геометрия карты «Каменка, лето 1943» чистыми функциями и списками: рельеф, река, дороги, мосты,
    /// постройки, леса, изгороди, точки контроля и базы. По этим данным строятся графика (MapBuilder),
    /// навигация (NavBuilder) и укрытия (CoverMap) — так они никогда не расходятся.
    /// СССР — юг (−Z), Германия — север (+Z).
    /// </summary>
    public static class MapLayout
    {
        public const float Half = 200f;
        public const float Size = Half * 2f;

        // ---------- Река ----------
        public const float RiverBedHalf = 6.5f;   // ширина русла (вода) от оси
        public const float RiverBankHalf = 11f;   // до бровки берега
        public const float WaterY = -1.1f;
        public const float BedY = -2.6f;
        public const float DeckY = 0.55f;         // настил мостов

        /// <summary>Ось реки: течёт вдоль Z на западе карты.</summary>
        public static float RiverX(float z) => -62f + 11f * Mathf.Sin(z / 42f) + 4f * Mathf.Sin(z / 17f + 1f);

        public static float RiverDistance(float x, float z) => Mathf.Abs(x - RiverX(z));

        /// <summary>Мосты: главный (по нему идут танки) и два пешеходных мостика (только пехота).</summary>
        public struct Bridge
        {
            public float z, halfWidth;
            public bool vehicles;
        }

        public static readonly Bridge[] Bridges =
        {
            new Bridge { z = 0f, halfWidth = 3.4f, vehicles = true },
            new Bridge { z = 102f, halfWidth = 1.1f, vehicles = false },
            new Bridge { z = -102f, halfWidth = 1.1f, vehicles = false },
        };

        public const float BridgeHalfLength = RiverBankHalf + 2f;

        // ---------- Точки контроля и базы ----------
        public static readonly string[] PointNames = { "Мельница", "Деревня", "Высота 112" };
        public static readonly Vector2[] PointXZ = { new Vector2(-122f, 2f), new Vector2(14f, 4f), new Vector2(118f, 0f) };

        public static Vector3 Point(int i) => Ground(PointXZ[i].x, PointXZ[i].y);

        public static Vector3 Base(Faction f) => Ground(8f, 178f * Factions.NorthSign(f));

        /// <summary>Место появления подкреплений: у края карты за базой, на дороге.</summary>
        public static Vector3 SpawnPoint(Faction f) => Ground(8f, 194f * Factions.NorthSign(f));

        // ---------- Рельеф ----------

        /// <summary>Высота земли в точке (без учёта настила мостов).</summary>
        public static float Height(float x, float z)
        {
            // Мягкие холмы
            float h = (Mathf.PerlinNoise(x * 0.012f + 31.7f, z * 0.012f + 12.3f) - 0.5f) * 4.5f
                    + (Mathf.PerlinNoise(x * 0.035f + 7.1f, z * 0.035f + 3.9f) - 0.5f) * 1.2f + 1.2f;
            // Высота 112 на востоке и пологие холмы у краёв
            h += Hill(x, z, 118f, 0f, 30f, 12f);
            h += Hill(x, z, 160f, 125f, 30f, 5f) + Hill(x, z, 160f, -125f, 30f, 5f);
            h += Hill(x, z, -165f, 140f, 34f, 5f) + Hill(x, z, -165f, -140f, 34f, 5f);
            // Деревня стоит на ровном месте
            float village = Smooth01((Vector2.Distance(new Vector2(x, z), new Vector2(10f, 0f)) - 40f) / 30f);
            h = Mathf.Lerp(1.1f, h, village);
            // Пойма реки: ровная низина, а в ней русло
            float d = RiverDistance(x, z);
            float plain = Smooth01((d - RiverBankHalf) / 26f);
            h = Mathf.Lerp(0.35f, h, plain);
            if (d < RiverBankHalf)
            {
                float k = Smooth01((d - RiverBedHalf) / (RiverBankHalf - RiverBedHalf));
                h = Mathf.Lerp(BedY, 0.35f, k);
            }
            // Края карты слегка приподняты — горизонт не обрывается
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            if (edge > Half - 12f) h += (edge - (Half - 12f)) * 0.35f;
            return h;
        }

        static float Hill(float x, float z, float cx, float cz, float r, float height)
        {
            float d2 = (x - cx) * (x - cx) + (z - cz) * (z - cz);
            return height * Mathf.Exp(-d2 / (2f * r * r));
        }

        public static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Высота поверхности, по которой ходят: на мостах — настил.</summary>
        public static float WalkHeight(float x, float z)
        {
            float h = Height(x, z);
            foreach (var b in Bridges)
                if (Mathf.Abs(z - b.z) <= b.halfWidth + 0.3f && Mathf.Abs(x - RiverX(b.z)) <= BridgeHalfLength)
                    h = Mathf.Max(h, DeckY);
            return h;
        }

        public static Vector3 Ground(float x, float z) => new Vector3(x, WalkHeight(x, z), z);
        public static Vector3 Ground(Vector3 p) => Ground(p.x, p.z);

        public static bool InWater(float x, float z) => Height(x, z) < WaterY + 0.15f && WalkHeight(x, z) < 0f;

        public static bool InsideMap(Vector3 p, float margin = 0f) =>
            Mathf.Abs(p.x) <= Half - margin && Mathf.Abs(p.z) <= Half - margin;

        public static Vector3 ClampToMap(Vector3 p, float margin)
        {
            p.x = Mathf.Clamp(p.x, -Half + margin, Half - margin);
            p.z = Mathf.Clamp(p.z, -Half + margin, Half - margin);
            return p;
        }

        // ---------- Дороги ----------

        /// <summary>Дороги — ломаные. Главная идёт с юга на север через деревню, поперечная — от мельницы до высоты.</summary>
        public static readonly List<Vector2[]> Roads = new List<Vector2[]>();

        public static float MainRoadX(float z) => 8f + 5f * Mathf.Sin(z / 55f);

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        public static float RoadDistance(float x, float z)
        {
            var p = new Vector2(x, z);
            float best = float.MaxValue;
            foreach (var road in Roads)
                for (int i = 0; i + 1 < road.Length; i++)
                    best = Mathf.Min(best, DistToSegment(p, road[i], road[i + 1]));
            return best;
        }

        // ---------- Постройки, леса, изгороди, поля ----------

        public static readonly List<Building> Buildings = new List<Building>();
        public static readonly List<Barrier> Barriers = new List<Barrier>();
        public static readonly List<Forest> Forests = new List<Forest>();
        public static readonly List<Vector3> Trees = new List<Vector3>(); // x, z, масштаб (в y)
        public static readonly List<Field> Fields = new List<Field>();

        static bool generated;

        /// <summary>Сгенерировать списки (детерминированно, один раз за запуск).</summary>
        public static void Generate()
        {
            if (generated) return;
            generated = true;
            var rnd = new System.Random(1943);

            // Дороги
            var main = new List<Vector2>();
            for (float z = -Half; z <= Half + 0.1f; z += 10f) main.Add(new Vector2(MainRoadX(z), z));
            Roads.Add(main.ToArray());
            Roads.Add(new[] { new Vector2(-150f, 6f), new Vector2(-122f, 2f), new Vector2(-90f, 0f), new Vector2(-30f, 0f), new Vector2(MainRoadX(0f), 0f) });
            Roads.Add(new[] { new Vector2(MainRoadX(0f), 0f), new Vector2(60f, -3f), new Vector2(95f, 2f), new Vector2(118f, 0f) });

            // Особые постройки ставим первыми, избы — вокруг них
            // Церковь у перекрёстка — точка контроля «Деревня»
            Buildings.Add(new Building { center = new Vector3(24f, 0f, 20f), size = new Vector3(9f, 7f, 14f), yaw = 0f, kind = BuildingKind.Church });
            // Мельница и сарай за рекой
            Buildings.Add(new Building { center = new Vector3(-126f, 0f, 14f), size = new Vector3(6f, 9f, 6f), yaw = 20f, kind = BuildingKind.Mill });
            Buildings.Add(new Building { center = new Vector3(-138f, 0f, -12f), size = new Vector3(9f, 4.5f, 14f), yaw = 5f, kind = BuildingKind.Barn });
            Buildings.Add(new Building { center = new Vector3(-108f, 0f, -16f), size = new Vector3(6.5f, 3.2f, 8.5f), yaw = 95f, kind = BuildingKind.Izba });
            // ДОТ на высоте
            Buildings.Add(new Building { center = new Vector3(126f, 0f, 9f), size = new Vector3(5f, 2.2f, 5f), yaw = 0f, kind = BuildingKind.Pillbox });

            var churchYard = Rect.MinMaxRect(13f, 6f, 35f, 33f);
            // Изба вдоль дорог деревни (если место свободно)
            void Izba(float x, float z, float yaw)
            {
                var size = new Vector3(6.5f + (float)rnd.NextDouble() * 1.5f, 3.2f, 8f + (float)rnd.NextDouble() * 2f);
                float r = new Vector2(size.x, size.z).magnitude * 0.5f;
                var c = new Vector2(x, z);
                if (churchYard.Overlaps(new Rect(x - r, z - r, r * 2f, r * 2f))) return;
                foreach (var o in Buildings)
                {
                    float ro = new Vector2(o.size.x, o.size.z).magnitude * 0.5f;
                    if (Vector2.Distance(c, new Vector2(o.center.x, o.center.z)) < (r + ro) * 0.9f + 2f) return;
                }
                if (RoadDistance(x, z) < r + 1.5f) return;
                Buildings.Add(new Building
                {
                    center = new Vector3(x, 0f, z),
                    size = size,
                    yaw = yaw,
                    kind = rnd.NextDouble() < 0.18 ? BuildingKind.Ruin : BuildingKind.Izba,
                });
            }
            for (float z = -46f; z <= 46f; z += 15f)
            {
                if (Mathf.Abs(z) < 10f) continue;
                Izba(MainRoadX(z) - 13f, z, 90f);
                Izba(MainRoadX(z) + 13f, z + 4f, -90f);
            }
            for (float x = -40f; x <= 55f; x += 15f)
            {
                if (Mathf.Abs(x - 8f) < 14f) continue;
                Izba(x, -12f, 0f);
                Izba(x + 3f, 13f, 180f);
            }

            // Стены и мешки с песком
            void Wall(float ax, float az, float bx, float bz, BarrierKind k) =>
                Barriers.Add(new Barrier { a = new Vector2(ax, az), b = new Vector2(bx, bz), kind = k });
            // Ограда церкви (с проходами)
            Wall(16f, 9f, 21f, 9f, BarrierKind.StoneWall); Wall(27f, 9f, 32f, 9f, BarrierKind.StoneWall);
            Wall(16f, 9f, 16f, 30f, BarrierKind.StoneWall); Wall(32f, 9f, 32f, 30f, BarrierKind.StoneWall);
            Wall(16f, 30f, 32f, 30f, BarrierKind.StoneWall);
            // Двор мельницы
            Wall(-140f, 22f, -114f, 22f, BarrierKind.StoneWall); Wall(-114f, 22f, -114f, 10f, BarrierKind.StoneWall);
            Wall(-140f, -4f, -140f, 22f, BarrierKind.StoneWall);
            // Окопы из мешков на высоте: полукольцо с обеих сторон
            for (int i = 0; i < 10; i++)
            {
                float a0 = i * Mathf.PI / 5f + 0.15f, a1 = a0 + 0.42f;
                Wall(118f + Mathf.Cos(a0) * 16f, Mathf.Sin(a0) * 16f, 118f + Mathf.Cos(a1) * 16f, Mathf.Sin(a1) * 16f, BarrierKind.Sandbags);
            }
            // Мешки у мостов
            foreach (float s in new[] { -1f, 1f })
            {
                Wall(RiverX(0f) + 18f, 9f * s, RiverX(0f) + 18f, 16f * s, BarrierKind.Sandbags);
                Wall(RiverX(0f) - 18f, 9f * s, RiverX(0f) - 18f, 16f * s, BarrierKind.Sandbags);
            }

            // Изгороди между полями — зеркально для обеих сторон (честная карта), с проходами
            void Hedge(float ax, float az, float bx, float bz)
            {
                foreach (float s in new[] { 1f, -1f })
                {
                    var a = new Vector2(ax, az * s);
                    var b = new Vector2(bx, bz * s);
                    float len = Vector2.Distance(a, b);
                    int pieces = Mathf.Max(1, Mathf.RoundToInt(len / 22f));
                    for (int i = 0; i < pieces; i++)
                    {
                        float t0 = (float)i / pieces, t1 = (i + 1f) / pieces;
                        float gap = 2.5f / len;
                        var p0 = Vector2.Lerp(a, b, t0 + (i == 0 ? 0f : gap));
                        var p1 = Vector2.Lerp(a, b, t1 - (i == pieces - 1 ? 0f : gap));
                        if (Vector2.Distance(p0, p1) > 2f)
                            Barriers.Add(new Barrier { a = p0, b = p1, kind = BarrierKind.Hedge });
                    }
                }
            }
            Hedge(35f, 38f, 100f, 42f);
            Hedge(40f, 42f, 44f, 92f);
            Hedge(95f, 62f, 175f, 58f);
            Hedge(-38f, 30f, -34f, 80f);
            Hedge(-30f, 58f, 0f, 60f);
            Hedge(-160f, 42f, -100f, 40f);
            Hedge(-96f, 44f, -98f, 92f);
            Hedge(60f, 110f, 120f, 115f);
            Hedge(-30f, 130f, -80f, 128f);
            Hedge(85f, 18f, 85f, 34f);

            // Заборы вокруг дворов изб (низкие, лёгкое укрытие)
            foreach (var bld in Buildings.ToArray())
            {
                if (bld.kind != BuildingKind.Izba) continue;
                var rot = Quaternion.Euler(0f, bld.yaw, 0f);
                var back = rot * new Vector3(0f, 0f, -1f);
                var side = rot * Vector3.right;
                var c = new Vector2(bld.center.x, bld.center.z);
                var b2 = new Vector2(back.x, back.z);
                var s2 = new Vector2(side.x, side.z);
                var p0 = c + b2 * (bld.size.z * 0.5f + 5f) - s2 * 5.5f;
                var p1 = c + b2 * (bld.size.z * 0.5f + 5f) + s2 * 5.5f;
                Barriers.Add(new Barrier { a = p0, b = p1, kind = BarrierKind.Fence });
            }

            // Леса — тоже зеркально
            void Wood(float x, float z, float r)
            {
                Forests.Add(new Forest { center = new Vector2(x, z), radius = r });
                Forests.Add(new Forest { center = new Vector2(x, -z), radius = r });
            }
            Wood(-155f, 75f, 30f);
            Wood(-28f, 102f, 20f);
            Wood(66f, 80f, 24f);
            Wood(168f, 92f, 26f);
            Wood(-105f, 158f, 34f);
            Wood(118f, 165f, 28f);
            Wood(-178f, 10f, 16f);
            Wood(178f, 18f, 14f);
            Wood(70f, 22f, 9f);

            // Деревья: внутри лесов плотно, по краям реже; не на дорогах, не в реке, не в домах
            foreach (var f in Forests)
            {
                int count = Mathf.RoundToInt(f.radius * f.radius * 0.11f);
                for (int i = 0; i < count; i++)
                {
                    float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    float r = Mathf.Sqrt((float)rnd.NextDouble()) * f.radius * 1.1f;
                    float x = f.center.x + Mathf.Cos(a) * r;
                    float z = f.center.y + Mathf.Sin(a) * r;
                    if (!TreeAllowed(x, z)) continue;
                    Trees.Add(new Vector3(x, 0.75f + (float)rnd.NextDouble() * 0.6f, z));
                }
            }
            // Одиночные деревья вдоль реки и дорог
            for (int i = 0; i < 160; i++)
            {
                float z = -Half + 8f + (float)rnd.NextDouble() * (Size - 16f);
                float side = rnd.NextDouble() < 0.5 ? -1f : 1f;
                float x = RiverX(z) + side * (RiverBankHalf + 2f + (float)rnd.NextDouble() * 8f);
                if (TreeAllowed(x, z)) Trees.Add(new Vector3(x, 0.7f + (float)rnd.NextDouble() * 0.5f, z));
            }

            // Поля (для текстуры): прямоугольники между изгородями
            void Fld(float x0, float z0, float x1, float z1, int kind)
            {
                Fields.Add(new Field { rect = Rect.MinMaxRect(Mathf.Min(x0, x1), Mathf.Min(z0, z1), Mathf.Max(x0, x1), Mathf.Max(z0, z1)), kind = kind });
                Fields.Add(new Field { rect = Rect.MinMaxRect(Mathf.Min(x0, x1), Mathf.Min(-z0, -z1), Mathf.Max(x0, x1), Mathf.Max(-z0, -z1)), kind = kind == 0 ? 1 : kind == 1 ? 0 : 2 });
            }
            Fld(46f, 44f, 100f, 92f, 0);
            Fld(100f, 62f, 175f, 110f, 1);
            Fld(-34f, 62f, 0f, 92f, 2);
            Fld(-160f, 44f, -100f, 60f, 1);
            Fld(-96f, 46f, -40f, 85f, 0);
            Fld(20f, 120f, 70f, 150f, 1);
        }

        static bool TreeAllowed(float x, float z)
        {
            if (!InsideMap(new Vector3(x, 0f, z), 3f)) return false;
            if (RoadDistance(x, z) < 5f) return false;
            if (RiverDistance(x, z) < RiverBankHalf + 1f) return false;
            foreach (var p in PointXZ)
                if (Vector2.Distance(p, new Vector2(x, z)) < 14f) return false;
            foreach (var b in Buildings)
                if (Vector2.Distance(new Vector2(b.center.x, b.center.z), new Vector2(x, z)) < Mathf.Max(b.size.x, b.size.z) * 0.8f + 2f) return false;
            return true;
        }

        public static bool InForest(float x, float z)
        {
            foreach (var f in Forests)
                if ((new Vector2(x, z) - f.center).sqrMagnitude < f.radius * f.radius) return true;
            return false;
        }
    }
}
