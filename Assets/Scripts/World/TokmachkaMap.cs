using System.Collections.Generic;
using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// «Битва за Токмачку» (Запорожская обл., осень 1943): карта 400×400 м, три точки — мельница за рекой,
    /// село с церковью в центре, высота 112 на востоке. Река на западе с мостом и двумя мостиками.
    /// Изгороди, леса и поля зеркальны для обеих сторон.
    /// </summary>
    public class TokmachkaMap : MapDef
    {
        public override MapId Id => MapId.Tokmachka;
        public override string Title => "Битва за Токмачку";
        public override string Subtitle => "Битва за Токмачку · осень 1943 · Запорожье";
        public override string Description => "Село, мельница за рекой и высота 112. Три точки, бой на 10–15 минут.";
        public override float Half => 200f;

        static readonly string[] names = { "Мельница", "Токмачка", "Высота 112" };
        static readonly Vector2[] points = { new Vector2(-122f, 2f), new Vector2(14f, 4f), new Vector2(118f, 0f) };
        static readonly MapLayout.Bridge[] bridges =
        {
            new MapLayout.Bridge { z = 0f, halfWidth = 3.4f, vehicles = true },
            new MapLayout.Bridge { z = 102f, halfWidth = 1.1f, vehicles = false },
            new MapLayout.Bridge { z = -102f, halfWidth = 1.1f, vehicles = false },
        };

        public override string[] PointNames => names;
        public override Vector2[] PointXZ => points;
        public override MapLayout.Bridge[] Bridges => bridges;

        public override float RiverX(float z) => -62f + 11f * Mathf.Sin(z / 42f) + 4f * Mathf.Sin(z / 17f + 1f);

        static float MainRoadX(float z) => 8f + 5f * Mathf.Sin(z / 55f);

        public override float Height(float x, float z)
        {
            // Мягкие холмы
            float h = (Mathf.PerlinNoise(x * 0.012f + 31.7f, z * 0.012f + 12.3f) - 0.5f) * 4.5f
                    + (Mathf.PerlinNoise(x * 0.035f + 7.1f, z * 0.035f + 3.9f) - 0.5f) * 1.2f + 1.2f;
            // Высота 112 на востоке и пологие холмы у краёв
            h += Hill(x, z, 118f, 0f, 30f, 12f);
            h += Hill(x, z, 160f, 125f, 30f, 5f) + Hill(x, z, 160f, -125f, 30f, 5f);
            h += Hill(x, z, -165f, 140f, 34f, 5f) + Hill(x, z, -165f, -140f, 34f, 5f);
            // Село стоит на ровном месте
            float village = Smooth01((Vector2.Distance(new Vector2(x, z), new Vector2(10f, 0f)) - 40f) / 30f);
            h = Mathf.Lerp(1.1f, h, village);
            h = RiverValley(x, z, h);
            return RaisedEdges(x, z, h);
        }

        public override void Generate()
        {
            var rnd = new System.Random(1943);

            // Дороги
            var main = new List<Vector2>();
            for (float z = -Half; z <= Half + 0.1f; z += 10f) main.Add(new Vector2(MainRoadX(z), z));
            Roads.Add(main.ToArray());
            Roads.Add(new[] { new Vector2(-150f, 6f), new Vector2(-122f, 2f), new Vector2(-90f, 0f), new Vector2(-30f, 0f), new Vector2(MainRoadX(0f), 0f) });
            Roads.Add(new[] { new Vector2(MainRoadX(0f), 0f), new Vector2(60f, -3f), new Vector2(95f, 2f), new Vector2(118f, 0f) });

            // Особые постройки: церковь (точка «Токмачка»), мельница и сарай за рекой, ДОТ на высоте
            Buildings.Add(new Building { center = new Vector3(24f, 0f, 20f), size = new Vector3(9f, 7f, 14f), yaw = 0f, kind = BuildingKind.Church });
            Buildings.Add(new Building { center = new Vector3(-126f, 0f, 14f), size = new Vector3(6f, 9f, 6f), yaw = 20f, kind = BuildingKind.Mill });
            Buildings.Add(new Building { center = new Vector3(-138f, 0f, -12f), size = new Vector3(9f, 4.5f, 14f), yaw = 5f, kind = BuildingKind.Barn });
            Buildings.Add(new Building { center = new Vector3(-108f, 0f, -16f), size = new Vector3(6.5f, 3.2f, 8.5f), yaw = 95f, kind = BuildingKind.Izba });
            Buildings.Add(new Building { center = new Vector3(126f, 0f, 9f), size = new Vector3(5f, 2.2f, 5f), yaw = 0f, kind = BuildingKind.Pillbox });

            var reserved = new[] { Rect.MinMaxRect(13f, 6f, 35f, 33f) }; // церковный двор
            void Izba(float x, float z, float yaw)
            {
                var size = new Vector3(6.5f + (float)rnd.NextDouble() * 1.5f, 3.2f, 8f + (float)rnd.NextDouble() * 2f);
                var kind = rnd.NextDouble() < 0.18 ? BuildingKind.Ruin : BuildingKind.Izba;
                TryBuilding(new Building { center = new Vector3(x, 0f, z), size = size, yaw = yaw, kind = kind }, reserved);
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

            // Ограда церкви (с проходами), двор мельницы
            Wall(16f, 9f, 21f, 9f, BarrierKind.StoneWall); Wall(27f, 9f, 32f, 9f, BarrierKind.StoneWall);
            Wall(16f, 9f, 16f, 30f, BarrierKind.StoneWall); Wall(32f, 9f, 32f, 30f, BarrierKind.StoneWall);
            Wall(16f, 30f, 32f, 30f, BarrierKind.StoneWall);
            Wall(-140f, 22f, -114f, 22f, BarrierKind.StoneWall); Wall(-114f, 22f, -114f, 10f, BarrierKind.StoneWall);
            Wall(-140f, -4f, -140f, 22f, BarrierKind.StoneWall);
            // Окопы из мешков на высоте: полукольцо с обеих сторон
            for (int i = 0; i < 10; i++)
            {
                float a0 = i * Mathf.PI / 5f + 0.15f, a1 = a0 + 0.42f;
                Wall(118f + Mathf.Cos(a0) * 16f, Mathf.Sin(a0) * 16f, 118f + Mathf.Cos(a1) * 16f, Mathf.Sin(a1) * 16f, BarrierKind.Sandbags);
            }
            // Мешки у моста
            foreach (float s in new[] { -1f, 1f })
            {
                Wall(RiverX(0f) + 18f, 9f * s, RiverX(0f) + 18f, 16f * s, BarrierKind.Sandbags);
                Wall(RiverX(0f) - 18f, 9f * s, RiverX(0f) - 18f, 16f * s, BarrierKind.Sandbags);
            }

            // Изгороди между полями — зеркально для обеих сторон
            void Hedge2(float ax, float az, float bx, float bz)
            {
                Hedge(new Vector2(ax, az), new Vector2(bx, bz));
                Hedge(new Vector2(ax, -az), new Vector2(bx, -bz));
            }
            Hedge2(35f, 38f, 100f, 42f);
            Hedge2(40f, 42f, 44f, 92f);
            Hedge2(95f, 62f, 175f, 58f);
            Hedge2(-38f, 30f, -34f, 80f);
            Hedge2(-30f, 58f, 0f, 60f);
            Hedge2(-160f, 42f, -100f, 40f);
            Hedge2(-96f, 44f, -98f, 92f);
            Hedge2(60f, 110f, 120f, 115f);
            Hedge2(-30f, 130f, -80f, 128f);
            Hedge2(85f, 18f, 85f, 34f);
            YardFences();

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
            PlantTrees(rnd, 160);

            // Поля: на одной стороне пшеница — на другой пашня, и наоборот
            void Fld(float x0, float z0, float x1, float z1, int kind)
            {
                AddField(x0, z0, x1, z1, kind);
                AddField(x0, -z0, x1, -z1, kind == 0 ? 1 : kind == 1 ? 0 : 2);
            }
            Fld(46f, 44f, 100f, 92f, 0);
            Fld(100f, 62f, 175f, 110f, 1);
            Fld(-34f, 62f, 0f, 92f, 2);
            Fld(-160f, 44f, -100f, 60f, 1);
            Fld(-96f, 46f, -40f, 85f, 0);
            Fld(20f, 120f, 70f, 150f, 1);

            // Телеграфные столбы вдоль главной дороги (кроме села) и стога на лугах
            var north = new List<Vector2>();
            var south = new List<Vector2>();
            for (float z = 50f; z <= Half; z += 10f) north.Add(new Vector2(MainRoadX(z), z));
            for (float z = -Half; z <= -50f; z += 10f) south.Add(new Vector2(MainRoadX(z), z));
            PolesAlong(north.ToArray(), 28f, 5f);
            PolesAlong(south.ToArray(), 28f, 5f);
            foreach (var f in Fields.ToArray())
            {
                if (f.kind != 2) continue;
                for (int i = 0; i < 3; i++)
                    Props.Add(new Prop
                    {
                        xz = new Vector2(Mathf.Lerp(f.rect.xMin + 4f, f.rect.xMax - 4f, (float)rnd.NextDouble()), Mathf.Lerp(f.rect.yMin + 4f, f.rect.yMax - 4f, (float)rnd.NextDouble())),
                        kind = PropKind.HayStack,
                    });
            }
        }
    }
}
