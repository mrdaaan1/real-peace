using System.Collections.Generic;
using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// «Бои за Мелитополь» (октябрь 1943, прорыв «Линии Вотан» на реке Молочной): карта 560×560 м, пять точек —
    /// переправа у реки на западе, элеватор, вокзал в центре, кладбище и курган на востоке. Город — кирпичные дома
    /// по сетке улиц, окраины — избы и поля, через весь город идёт железная дорога с мостом через реку.
    /// Расстановка почти центрально-симметрична (x, z) → (−x, −z): у обеих сторон похожие подходы.
    /// </summary>
    public class MelitopolMap : MapDef
    {
        public override MapId Id => MapId.Melitopol;
        public override string Title => "Бои за Мелитополь";
        public override string Subtitle => "Бои за Мелитополь · октябрь 1943 · река Молочная";
        public override string Description => "Город, вокзал, элеватор, кладбище и курган. Пять точек — долгий манёвренный бой.";
        public override float Half => 280f;
        public override float BaseX => 0f;
        public override float TicketScale => 1.6f;
        public override int ExtraUnits => 4;

        static readonly string[] names = { "Переправа", "Элеватор", "Вокзал", "Кладбище", "Курган" };
        static readonly Vector2[] points =
        {
            new Vector2(-172f, 30f), new Vector2(-80f, 70f), new Vector2(0f, 22f), new Vector2(80f, -78f), new Vector2(188f, 12f),
        };
        static readonly MapLayout.Bridge[] bridges =
        {
            new MapLayout.Bridge { z = 30f, halfWidth = 3.4f, vehicles = true },    // автомобильный
            new MapLayout.Bridge { z = -14f, halfWidth = 3.0f, vehicles = true },   // железнодорожный
            new MapLayout.Bridge { z = 150f, halfWidth = 1.1f, vehicles = false },
            new MapLayout.Bridge { z = -150f, halfWidth = 1.1f, vehicles = false },
        };

        public override string[] PointNames => names;
        public override Vector2[] PointXZ => points;
        public override MapLayout.Bridge[] Bridges => bridges;

        const float RailZ = -14f;

        public override float RiverX(float z) => -205f + 9f * Mathf.Sin(z / 50f) + 5f * Mathf.Sin(z / 19f + 2f);

        // Октябрь: солнце ниже и бледнее, дымка гуще, листва пожелтела
        public override Vector3 SunEuler => new Vector3(28f, -52f, 0f);
        public override Color SunColor => Shapes.Hex("#ffdcb0");
        public override float SunIntensity => 1.9f;
        public override Color FogColor => Shapes.Hex("#b0aea3");
        public override float FogDensity => 0.0036f;
        public override Color OakLeaves => Shapes.Hex("#8a6a24");
        public override Color BirchLeaves => Shapes.Hex("#b08a2a");
        public override Color HedgeLeaves => Shapes.Hex("#6a5a26");
        public override Color GrassTint => new Color(1.12f, 1.0f, 0.78f);

        public override float Height(float x, float z)
        {
            float h = (Mathf.PerlinNoise(x * 0.010f + 71.7f, z * 0.010f + 2.3f) - 0.5f) * 4f
                    + (Mathf.PerlinNoise(x * 0.03f + 17.1f, z * 0.03f + 13.9f) - 0.5f) * 1.1f + 1.2f;
            // Курган на востоке, холмы по краям
            h += Hill(x, z, 188f, 12f, 30f, 13f);
            h += Hill(x, z, -150f, -200f, 40f, 5f) + Hill(x, z, 150f, 200f, 40f, 5f);
            h += Hill(x, z, 220f, -190f, 35f, 4f) + Hill(x, z, -40f, 230f, 35f, 3f);
            // Город на ровном месте
            float city = Smooth01((Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - 135f) / 35f);
            h = Mathf.Lerp(1.0f, h, city);
            // Насыпь железной дороги
            float rail = Mathf.Abs(z - RailZ);
            if (x > RiverX(z) + MapLayout.RiverBankHalf + 4f)
                h += 0.7f * (1f - Smooth01((rail - 2.5f) / 3.5f));
            h = RiverValley(x, z, h);
            return RaisedEdges(x, z, h);
        }

        public override void Generate()
        {
            var rnd = new System.Random(10_1943);
            float H = Half;

            // --- Железная дорога и улицы ---
            MapLayout.Railways.Add(new[] { new Vector2(-H, RailZ), new Vector2(H, RailZ) });
            Roads.Add(new[] { new Vector2(0f, -H), new Vector2(0f, H) });                       // главная улица
            Roads.Add(new[] { new Vector2(-H, 30f), new Vector2(-172f, 30f), new Vector2(-60f, 32f), new Vector2(0f, 30f) }); // к мосту
            Roads.Add(new[] { new Vector2(0f, 30f), new Vector2(120f, 28f), new Vector2(188f, 12f), new Vector2(H, 10f) });   // к кургану
            foreach (float x in new[] { -120f, -60f, 60f, 120f })
                Roads.Add(new[] { new Vector2(x, -150f), new Vector2(x, 150f) });
            foreach (float z in new[] { -120f, -60f, 90f, 150f })
                Roads.Add(new[] { new Vector2(-150f, z), new Vector2(150f, z) });
            Roads.Add(new[] { new Vector2(60f, -60f), new Vector2(80f, -78f) });
            Roads.Add(new[] { new Vector2(-60f, 90f), new Vector2(-80f, 70f) });

            // --- Ориентиры ---
            // Вокзал и пакгаузы у путей
            Buildings.Add(new Building { center = new Vector3(0f, 0f, 2f), size = new Vector3(34f, 8f, 12f), yaw = 0f, kind = BuildingKind.Station });
            Buildings.Add(new Building { center = new Vector3(-38f, 0f, 0f), size = new Vector3(22f, 6f, 11f), yaw = 0f, kind = BuildingKind.Warehouse });
            Buildings.Add(new Building { center = new Vector3(38f, 0f, -28f), size = new Vector3(22f, 6f, 11f), yaw = 0f, kind = BuildingKind.Warehouse });
            // Элеватор — высокие силосы, их видно с любого конца карты
            Buildings.Add(new Building { center = new Vector3(-82f, 0f, 104f), size = new Vector3(15f, 26f, 26f), yaw = 0f, kind = BuildingKind.Elevator });
            // Кладбище: стена с воротами, часовня, кресты и надгробия
            Buildings.Add(new Building { center = new Vector3(80f, 0f, -114f), size = new Vector3(7f, 6f, 10f), yaw = 0f, kind = BuildingKind.Chapel });
            float cx = 80f, cz = -104f, half = 24f;
            Wall(cx - half, cz + half, cx - 4f, cz + half, BarrierKind.StoneWall); Wall(cx + 4f, cz + half, cx + half, cz + half, BarrierKind.StoneWall);
            Wall(cx - half, cz - half, cx + half, cz - half, BarrierKind.StoneWall);
            Wall(cx - half, cz - half, cx - half, cz + half, BarrierKind.StoneWall);
            Wall(cx + half, cz - half, cx + half, cz - 4f, BarrierKind.StoneWall); Wall(cx + half, cz + 4f, cx + half, cz + half, BarrierKind.StoneWall);
            for (float gx = cx - half + 4f; gx < cx + half - 2f; gx += 4f)
                for (float gz = cz - half + 4f; gz < cz + half - 2f; gz += 3.5f)
                {
                    if (Mathf.Abs(gx - cx) < 3f || (Mathf.Abs(gx - cx) < 6f && gz < cz - 4f)) continue; // дорожка и часовня
                    if (rnd.NextDouble() < 0.25) continue;
                    Props.Add(new Prop { xz = new Vector2(gx + (float)rnd.NextDouble() - 0.5f, gz), yaw = (float)rnd.NextDouble() * 8f - 4f, kind = rnd.NextDouble() < 0.6 ? PropKind.Cross : PropKind.Tombstone });
                }
            for (int i = 0; i < 10; i++) // берёзы на кладбище
                Trees.Add(new Vector3(cx - half + 3f + (float)rnd.NextDouble() * (half * 2f - 6f), 0.8f, cz - half + 3f + (float)rnd.NextDouble() * (half * 2f - 6f)));
            // Курган: ДОТ и полукольцо окопов с обеих сторон
            Buildings.Add(new Building { center = new Vector3(196f, 0f, 20f), size = new Vector3(5f, 2.2f, 5f), yaw = 90f, kind = BuildingKind.Pillbox });
            for (int i = 0; i < 12; i++)
            {
                float a0 = i * Mathf.PI / 6f + 0.12f, a1 = a0 + 0.36f;
                Wall(188f + Mathf.Cos(a0) * 17f, 12f + Mathf.Sin(a0) * 17f, 188f + Mathf.Cos(a1) * 17f, 12f + Mathf.Sin(a1) * 17f, BarrierKind.Sandbags);
            }
            // Переправа: окопы на обоих берегах, мешки у моста
            foreach (float s in new[] { -1f, 1f })
            {
                Wall(RiverX(30f) + 20f, 30f + 8f * s, RiverX(30f) + 20f, 30f + 16f * s, BarrierKind.Sandbags);
                Wall(RiverX(30f) - 20f, 30f + 8f * s, RiverX(30f) - 20f, 30f + 16f * s, BarrierKind.Sandbags);
            }
            Wall(-160f, 42f, -150f, 42f, BarrierKind.Sandbags);
            Wall(-160f, 18f, -150f, 18f, BarrierKind.Sandbags);
            // Мешки у элеватора и вокзала
            Wall(-92f, 62f, -70f, 62f, BarrierKind.Sandbags);
            Wall(-10f, 40f, 10f, 40f, BarrierKind.Sandbags);
            Wall(-14f, 12f, -14f, 34f, BarrierKind.Sandbags);

            // --- Городская застройка по улицам: кирпич в центре, избы на окраинах ---
            var reserved = new List<Rect>
            {
                Rect.MinMaxRect(cx - half - 3f, cz - half - 3f, cx + half + 3f, cz + half + 3f), // кладбище
                Rect.MinMaxRect(-25f, 10f, 25f, 38f),                                           // площадь у вокзала
                Rect.MinMaxRect(-100f, 55f, -60f, 122f),                                        // элеватор
                Rect.MinMaxRect(160f, -15f, 216f, 40f),                                         // курган
            };
            foreach (var p in points) reserved.Add(new Rect(p.x - 9f, p.y - 9f, 18f, 18f));

            void House(float x, float z, float yaw)
            {
                float r = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
                bool brick = r < 110f && rnd.NextDouble() < 0.8;
                Vector3 size;
                BuildingKind kind;
                if (brick)
                {
                    size = new Vector3(9f + (float)rnd.NextDouble() * 4f, 6.2f + (rnd.NextDouble() < 0.3 ? 3f : 0f), 8f + (float)rnd.NextDouble() * 3f);
                    kind = rnd.NextDouble() < 0.28 ? BuildingKind.BrickRuin : BuildingKind.BrickHouse;
                }
                else
                {
                    size = new Vector3(6.5f + (float)rnd.NextDouble() * 1.5f, 3.2f, 8f + (float)rnd.NextDouble() * 2f);
                    kind = rnd.NextDouble() < 0.2 ? BuildingKind.Ruin : BuildingKind.Izba;
                }
                TryBuilding(new Building { center = new Vector3(x, 0f, z), size = size, yaw = yaw, kind = kind }, reserved, 2f);
            }
            // Вдоль вертикальных улиц
            foreach (float x in new[] { -120f, -60f, 0f, 60f, 120f })
                for (float z = -165f; z <= 170f; z += 16f)
                {
                    House(x - 13f, z + (float)rnd.NextDouble() * 3f, 90f);
                    House(x + 13f, z + 6f + (float)rnd.NextDouble() * 3f, -90f);
                }
            // Вдоль горизонтальных
            foreach (float z in new[] { -120f, -60f, 90f, 150f })
                for (float x = -165f; x <= 165f; x += 16f)
                {
                    House(x + (float)rnd.NextDouble() * 3f, z - 13f, 0f);
                    House(x + 6f + (float)rnd.NextDouble() * 3f, z + 13f, 180f);
                }
            YardFences();

            // --- Противотанковые ежи на подступах и разбитая техника ---
            foreach (float s in new[] { -1f, 1f })
            {
                for (float x = -140f; x <= 140f; x += 7f)
                {
                    if (Mathf.Abs(x) < 10f || Mathf.Abs(x % 60f) < 8f) continue; // проезды на улицах
                    if (rnd.NextDouble() < 0.35) continue;
                    Props.Add(new Prop { xz = new Vector2(x, s * 182f + (float)rnd.NextDouble() * 4f), yaw = (float)rnd.NextDouble() * 90f, kind = PropKind.TankTrap });
                }
                for (int i = 0; i < 4; i++)
                    Props.Add(new Prop { xz = new Vector2(s * (175f + i * 6f), 40f * s + i * 5f), yaw = 20f * i, kind = PropKind.TankTrap });
            }
            for (int i = 0; i < 8; i++)
            {
                var p = new Vector2(-150f + (float)rnd.NextDouble() * 300f, -150f + (float)rnd.NextDouble() * 300f);
                if (MapLayout.RoadDistance(p.x, p.y) < 6f && MapLayout.RailDistance(p.x, p.y) > 8f)
                    Props.Add(new Prop { xz = p + new Vector2(3.5f, 0f), yaw = (float)rnd.NextDouble() * 360f, kind = PropKind.Wreck });
            }

            // --- Окрестности: изгороди, лесополосы, поля (центрально-симметрично) ---
            void Hedge2(float ax, float az, float bx, float bz)
            {
                Hedge(new Vector2(ax, az), new Vector2(bx, bz));
                Hedge(new Vector2(-ax, -az), new Vector2(-bx, -bz));
            }
            Hedge2(-150f, 200f, -40f, 205f);
            Hedge2(30f, 195f, 150f, 198f);
            Hedge2(160f, 60f, 160f, 160f);
            Hedge2(210f, 70f, 260f, 72f);
            Hedge2(-240f, 80f, -240f, 200f);
            void Wood(float x, float z, float r)
            {
                Forests.Add(new Forest { center = new Vector2(x, z), radius = r });
                Forests.Add(new Forest { center = new Vector2(-x, -z), radius = r });
            }
            Wood(-180f, 215f, 34f);
            Wood(205f, 160f, 30f);
            Wood(-255f, 110f, 18f);
            Wood(110f, 235f, 26f);
            Wood(240f, -40f, 18f);
            Wood(-60f, 225f, 16f);
            PlantTrees(rnd, 220);
            void Fld(float x0, float z0, float x1, float z1, int kind)
            {
                AddField(x0, z0, x1, z1, kind);
                AddField(-x0, -z0, -x1, -z1, kind == 0 ? 1 : kind == 1 ? 0 : 2);
            }
            Fld(-150f, 205f, -40f, 255f, 0);
            Fld(30f, 198f, 150f, 250f, 1);
            Fld(165f, 60f, 260f, 150f, 2);
            Fld(-235f, 160f, -190f, 260f, 1);

            // Столбы вдоль железной дороги и главной улицы
            PolesAlong(MapLayout.Railways[0], 30f, 6f);
            PolesAlong(new[] { new Vector2(0f, -H), new Vector2(0f, -160f) }, 28f, 5f);
            PolesAlong(new[] { new Vector2(0f, 160f), new Vector2(0f, H) }, 28f, 5f);
        }
    }
}
