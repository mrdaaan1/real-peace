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

    public enum BuildingKind { Izba, Barn, Church, Mill, Ruin, Pillbox, BrickHouse, BrickRuin, Station, Elevator, Chapel, Warehouse }

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

    /// <summary>Мелкий объект карты: столб, крест, надгробие, противотанковый ёж, стог, разбитый грузовик.</summary>
    public struct Prop
    {
        public Vector2 xz;
        public float yaw;
        public PropKind kind;
    }

    public enum PropKind { Pole, Cross, Tombstone, TankTrap, HayStack, Wreck }

    public enum MapId { Tokmachka, Melitopol }


    /// <summary>
    /// Геометрия текущей карты чистыми функциями и списками: рельеф, река, дороги, железная дорога, мосты, постройки,
    /// леса, изгороди, точки контроля, базы. По этим данным строятся графика (MapBuilder), навигация (NavBuilder)
    /// и укрытия (CoverMap) — так они никогда не расходятся. Сами карты — наследники MapDef
    /// (TokmachkaMap, MelitopolMap); MapLayout — общий фасад, через который к ним обращается весь код.
    /// СССР — юг (−Z), Германия — север (+Z). Река всегда течёт вдоль Z.
    /// </summary>
    public static class MapLayout
    {
        static MapDef map = new TokmachkaMap();
        public static MapDef Map => map;
        public static MapId Current => map.Id;

        /// <summary>Выбрать карту (до постройки мира). Списки будут сгенерированы заново.</summary>
        public static void Select(MapId id)
        {
            if (map.Id == id) return;
            map = id == MapId.Melitopol ? new MelitopolMap() : (MapDef)new TokmachkaMap();
            generated = false;
        }

        public static MapDef Create(MapId id) => id == MapId.Melitopol ? new MelitopolMap() : (MapDef)new TokmachkaMap();

        public static float Half => map.Half;
        public static float Size => map.Half * 2f;

        // ---------- Река ----------
        public const float RiverBedHalf = 6.5f;   // ширина русла (вода) от оси
        public const float RiverBankHalf = 11f;   // до бровки берега
        public const float WaterY = -1.1f;
        public const float BedY = -2.6f;
        public const float DeckY = 0.55f;         // настил мостов
        public const float BridgeHalfLength = RiverBankHalf + 2f;

        public static float RiverX(float z) => map.RiverX(z);
        public static float RiverDistance(float x, float z) => Mathf.Abs(x - RiverX(z));

        public struct Bridge
        {
            public float z, halfWidth;
            public bool vehicles;
        }

        public static Bridge[] Bridges => map.Bridges;

        // ---------- Точки контроля и базы ----------
        public static string[] PointNames => map.PointNames;
        public static Vector2[] PointXZ => map.PointXZ;
        public static Vector3 Point(int i) => Ground(PointXZ[i].x, PointXZ[i].y);
        public static Vector3 Base(Faction f) => Ground(map.BaseX, (Half - 22f) * Factions.NorthSign(f));
        /// <summary>Место появления подкреплений: у края карты за базой, на дороге.</summary>
        public static Vector3 SpawnPoint(Faction f) => Ground(map.BaseX, (Half - 6f) * Factions.NorthSign(f));

        // ---------- Рельеф ----------
        public static float Height(float x, float z) => map.Height(x, z);

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

        /// <summary>Дороги — ломаные.</summary>
        public static readonly List<Vector2[]> Roads = new List<Vector2[]>();
        /// <summary>Железные дороги — ломаные (насыпь со шпалами и рельсами, по ней можно ходить и ездить).</summary>
        public static readonly List<Vector2[]> Railways = new List<Vector2[]>();

        public static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        static float LinesDistance(List<Vector2[]> lines, float x, float z)
        {
            var p = new Vector2(x, z);
            float best = float.MaxValue;
            foreach (var line in lines)
                for (int i = 0; i + 1 < line.Length; i++)
                    best = Mathf.Min(best, DistToSegment(p, line[i], line[i + 1]));
            return best;
        }

        public static float RoadDistance(float x, float z) => LinesDistance(Roads, x, z);
        public static float RailDistance(float x, float z) => Railways.Count == 0 ? float.MaxValue : LinesDistance(Railways, x, z);

        // ---------- Постройки, леса, изгороди, поля ----------

        public static readonly List<Building> Buildings = new List<Building>();
        public static readonly List<Barrier> Barriers = new List<Barrier>();
        public static readonly List<Forest> Forests = new List<Forest>();
        public static readonly List<Vector3> Trees = new List<Vector3>(); // x, z, масштаб (в y)
        public static readonly List<Field> Fields = new List<Field>();
        public static readonly List<Prop> Props = new List<Prop>();

        static bool generated;

        /// <summary>Сгенерировать списки текущей карты (детерминированно, один раз).</summary>
        public static void Generate()
        {
            if (generated) return;
            generated = true;
            Roads.Clear(); Railways.Clear(); Buildings.Clear(); Barriers.Clear(); Forests.Clear(); Trees.Clear(); Fields.Clear(); Props.Clear();
            map.Generate();
        }

        public static bool InForest(float x, float z)
        {
            foreach (var f in Forests)
                if ((new Vector2(x, z) - f.center).sqrMagnitude < f.radius * f.radius) return true;
            return false;
        }
    }
}
