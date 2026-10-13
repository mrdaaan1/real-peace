using System.Collections.Generic;
using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// Описание карты: размер, рельеф, река и мосты, точки контроля, база, погода, и генерация списков
    /// (дороги, постройки, изгороди, леса, деревья, поля, мелочи) в общие списки MapLayout.
    /// Здесь же общие инструменты расстановки, которыми пользуются все карты.
    /// </summary>
    public abstract class MapDef
    {
        public abstract MapId Id { get; }
        public abstract string Title { get; }
        public abstract string Subtitle { get; }
        public abstract string Description { get; }
        public abstract float Half { get; }
        public virtual float BaseX => 8f;
        /// <summary>Множитель очков победы (больше точек — длиннее бой) и добавка к лимиту юнитов.</summary>
        public virtual float TicketScale => 1f;
        public virtual int ExtraUnits => 0;

        public abstract string[] PointNames { get; }
        public abstract Vector2[] PointXZ { get; }
        public abstract MapLayout.Bridge[] Bridges { get; }
        public abstract float RiverX(float z);
        public abstract float Height(float x, float z);
        public abstract void Generate();

        // ---------- Погода и время года ----------
        public virtual Vector3 SunEuler => new Vector3(38f, -38f, 0f);
        public virtual Color SunColor => Shapes.Hex("#ffe7c4");
        public virtual float SunIntensity => 2.1f;
        public virtual Color FogColor => Shapes.Hex("#a9aea6");
        public virtual float FogDensity => 0.0032f;
        /// <summary>Листва: летняя зелень или осенняя желтизна (оттенки крон).</summary>
        public virtual Color OakLeaves => Shapes.Hex("#405c26");
        public virtual Color BirchLeaves => Shapes.Hex("#5a7a2c");
        public virtual Color HedgeLeaves => Shapes.Hex("#3e5a28");
        /// <summary>Множитель цвета травы (осенью — суше и желтее).</summary>
        public virtual Color GrassTint => Color.white;

        // ---------- Общие инструменты ----------

        protected static List<Vector2[]> Roads => MapLayout.Roads;
        protected static List<Building> Buildings => MapLayout.Buildings;
        protected static List<Barrier> Barriers => MapLayout.Barriers;
        protected static List<Forest> Forests => MapLayout.Forests;
        protected static List<Vector3> Trees => MapLayout.Trees;
        protected static List<Field> Fields => MapLayout.Fields;
        protected static List<Prop> Props => MapLayout.Props;

        protected static float Hill(float x, float z, float cx, float cz, float r, float height)
        {
            float d2 = (x - cx) * (x - cx) + (z - cz) * (z - cz);
            return height * Mathf.Exp(-d2 / (2f * r * r));
        }

        protected static float Smooth01(float t) => MapLayout.Smooth01(t);

        /// <summary>Пойма и русло реки поверх рельефа h.</summary>
        protected float RiverValley(float x, float z, float h)
        {
            float d = Mathf.Abs(x - RiverX(z));
            float plain = Smooth01((d - MapLayout.RiverBankHalf) / 26f);
            h = Mathf.Lerp(0.35f, h, plain);
            if (d < MapLayout.RiverBankHalf)
            {
                float k = Smooth01((d - MapLayout.RiverBedHalf) / (MapLayout.RiverBankHalf - MapLayout.RiverBedHalf));
                h = Mathf.Lerp(MapLayout.BedY, 0.35f, k);
            }
            return h;
        }

        /// <summary>Края карты слегка приподняты — горизонт не обрывается.</summary>
        protected float RaisedEdges(float x, float z, float h)
        {
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            if (edge > Half - 12f) h += (edge - (Half - 12f)) * 0.35f;
            return h;
        }

        protected static void Wall(float ax, float az, float bx, float bz, BarrierKind k) =>
            Barriers.Add(new Barrier { a = new Vector2(ax, az), b = new Vector2(bx, bz), kind = k });

        /// <summary>Поставить постройку, если место свободно (не на дороге, не в другом доме, не в запретных прямоугольниках).</summary>
        protected static bool TryBuilding(Building b, IList<Rect> reserved = null, float roadClearance = 1.5f)
        {
            float r = new Vector2(b.size.x, b.size.z).magnitude * 0.5f;
            var c = new Vector2(b.center.x, b.center.z);
            if (reserved != null)
                foreach (var rect in reserved)
                    if (rect.Overlaps(new Rect(c.x - r, c.y - r, r * 2f, r * 2f))) return false;
            foreach (var o in Buildings)
            {
                float ro = new Vector2(o.size.x, o.size.z).magnitude * 0.5f;
                if (Vector2.Distance(c, new Vector2(o.center.x, o.center.z)) < (r + ro) * 0.9f + 2f) return false;
            }
            if (MapLayout.RoadDistance(c.x, c.y) < r + roadClearance) return false;
            if (MapLayout.RailDistance(c.x, c.y) < r + 4f) return false;
            if (MapLayout.RiverDistance(c.x, c.y) < MapLayout.RiverBankHalf + r + 3f) return false;
            if (!MapLayout.InsideMap(b.center, r + 4f)) return false;
            Buildings.Add(b);
            return true;
        }

        /// <summary>Изгородь с проходами; mirror — ещё и зеркальная копия для другой стороны (честная карта).</summary>
        protected static void Hedge(Vector2 a, Vector2 b)
        {
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

        /// <summary>Заборы позади изб (низкие, лёгкое укрытие).</summary>
        protected static void YardFences()
        {
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
        }

        /// <summary>Деревья по лесам и одиночные вдоль реки.</summary>
        protected void PlantTrees(System.Random rnd, int riverTrees)
        {
            foreach (var f in Forests)
            {
                int count = Mathf.RoundToInt(f.radius * f.radius * 0.11f);
                for (int i = 0; i < count; i++)
                {
                    float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    float r = Mathf.Sqrt((float)rnd.NextDouble()) * f.radius * 1.1f;
                    float x = f.center.x + Mathf.Cos(a) * r;
                    float z = f.center.y + Mathf.Sin(a) * r;
                    if (TreeAllowed(x, z)) Trees.Add(new Vector3(x, 0.75f + (float)rnd.NextDouble() * 0.6f, z));
                }
            }
            for (int i = 0; i < riverTrees; i++)
            {
                float z = -Half + 8f + (float)rnd.NextDouble() * (Half * 2f - 16f);
                float side = rnd.NextDouble() < 0.5 ? -1f : 1f;
                float x = RiverX(z) + side * (MapLayout.RiverBankHalf + 2f + (float)rnd.NextDouble() * 8f);
                if (TreeAllowed(x, z)) Trees.Add(new Vector3(x, 0.7f + (float)rnd.NextDouble() * 0.5f, z));
            }
        }

        protected bool TreeAllowed(float x, float z)
        {
            if (!MapLayout.InsideMap(new Vector3(x, 0f, z), 3f)) return false;
            if (MapLayout.RoadDistance(x, z) < 5f) return false;
            if (MapLayout.RailDistance(x, z) < 6f) return false;
            if (MapLayout.RiverDistance(x, z) < MapLayout.RiverBankHalf + 1f) return false;
            foreach (var p in PointXZ)
                if (Vector2.Distance(p, new Vector2(x, z)) < 14f) return false;
            foreach (var b in Buildings)
                if (Vector2.Distance(new Vector2(b.center.x, b.center.z), new Vector2(x, z)) < Mathf.Max(b.size.x, b.size.z) * 0.8f + 2f) return false;
            return true;
        }

        protected static void AddField(float x0, float z0, float x1, float z1, int kind) =>
            Fields.Add(new Field { rect = Rect.MinMaxRect(Mathf.Min(x0, x1), Mathf.Min(z0, z1), Mathf.Max(x0, x1), Mathf.Max(z0, z1)), kind = kind });

        /// <summary>Столбы вдоль ломаной через каждые step метров, со сдвигом вбок.</summary>
        protected static void PolesAlong(Vector2[] line, float step, float offset)
        {
            float acc = 0f;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                var a = line[i];
                var b = line[i + 1];
                var d = b - a;
                float len = d.magnitude;
                var n = new Vector2(-d.y, d.x).normalized;
                for (float t = (step - acc); t < len; t += step)
                    Props.Add(new Prop { xz = a + d / len * t + n * offset, yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, kind = PropKind.Pole });
                acc = (acc + len) % step;
            }
        }
    }
}
