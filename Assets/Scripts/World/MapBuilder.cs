using System.Collections.Generic;
using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// Строит карту по MapLayout: землю (сетка + шейдер с пятью слоями по карте весов), реку (шейдер воды),
    /// мосты, деревню (срубы, солома, тёс, побелённая церковь), мельницу, ДОТ, изгороди из листвы, леса из деревьев
    /// с кронами-карточками, траву и пшеницу (инстансинг), базы сторон. Неподвижные детали склеиваются (MeshCombiner).
    /// Коллайдеры есть только у земли, настилов, построек и стволов деревьев — для линии видимости и кликов.
    /// </summary>
    public static class MapBuilder
    {
        /// <summary>Цветная карта местности — подложка миникарты.</summary>
        public static Texture2D MapTexture { get; private set; }
        public static Mesh GroundMesh { get; private set; }
        public static Collider GroundCollider { get; private set; }

        const int TexSize = 1024;
        const float GridStep = 1.5f;

        public static Transform Build(Transform world)
        {
            MapLayout.Generate();
            var map = Shapes.Group("Map", world);
            var decor = Shapes.Group("Decor", map);
            var solids = Shapes.Group("Solids", map); // коллайдеры построек и деревьев, крутящиеся крылья

            BuildGround(map);
            BuildWater(map);
            BuildBridges(decor, solids);
            BuildRailways(decor);
            foreach (var b in MapLayout.Buildings) BuildBuilding(b, decor, solids);
            foreach (var b in MapLayout.Barriers) BuildBarrier(b, decor);
            BuildTrees(decor, solids);
            BuildProps(decor);
            BuildBase(Faction.USSR, decor);
            BuildBase(Faction.Germany, decor);
            MeshCombiner.Combine(decor);
            GrassField.Build(map, SurfaceAt);
            CoverMap.Build();
            return map;
        }

        // ---------- Что на земле ----------

        public static MapSurface SurfaceAt(float x, float z)
        {
            if (!MapLayout.InsideMap(new Vector3(x, 0f, z), 1f)) return MapSurface.Blocked;
            if (MapLayout.InWater(x, z)) return MapSurface.Water;
            float river = MapLayout.RiverDistance(x, z);
            if (river < MapLayout.RiverBankHalf + 1.5f) return MapSurface.Mud;
            if (MapLayout.RoadDistance(x, z) < 3.2f || MapLayout.RailDistance(x, z) < 3.5f) return MapSurface.Road;
            foreach (Faction f in new[] { Faction.USSR, Faction.Germany })
                if (Vector2.Distance(new Vector2(x, z), new Vector2(MapLayout.Base(f).x, MapLayout.Base(f).z)) < 20f) return MapSurface.Road;
            foreach (var p in MapLayout.PointXZ)
                if (Vector2.Distance(p, new Vector2(x, z)) < 5f) return MapSurface.Road; // утоптано у флага
            foreach (var b in MapLayout.Buildings)
            {
                var local = Quaternion.Euler(0f, -b.yaw, 0f) * (new Vector3(x, 0f, z) - new Vector3(b.center.x, 0f, b.center.z));
                if (Mathf.Abs(local.x) < b.size.x * 0.5f + 0.8f && Mathf.Abs(local.z) < b.size.z * 0.5f + 0.8f) return MapSurface.Blocked;
            }
            foreach (var f in MapLayout.Fields)
                if (f.rect.Contains(new Vector2(x, z)))
                    return f.kind == 0 ? MapSurface.Wheat : f.kind == 1 ? MapSurface.Plough : MapSurface.Grass;
            return MapSurface.Grass;
        }

        // ---------- Земля ----------

        static void BuildGround(Transform map)
        {
            int n = Mathf.RoundToInt(MapLayout.Size / GridStep) + 1;
            var verts = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            for (int iz = 0; iz < n; iz++)
                for (int ix = 0; ix < n; ix++)
                {
                    float x = -MapLayout.Half + ix * GridStep, z = -MapLayout.Half + iz * GridStep;
                    verts[iz * n + ix] = new Vector3(x, MapLayout.Height(x, z), z);
                    uvs[iz * n + ix] = new Vector2((float)ix / (n - 1), (float)iz / (n - 1));
                }
            var tris = new int[(n - 1) * (n - 1) * 6];
            int t = 0;
            for (int iz = 0; iz < n - 1; iz++)
                for (int ix = 0; ix < n - 1; ix++)
                {
                    int a = iz * n + ix, b = a + 1, c = a + n, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            var mesh = new Mesh { name = "Ground", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GroundMesh = mesh;

            var control = PaintMaps();
            var mat = new Material(Mats.TerrainShader) { name = "Ground" };
            mat.SetTexture("_ControlTex", control);
            void Layer(string prop, Texture2D tex, float normal)
            {
                mat.SetTexture(prop, tex);
                mat.SetTexture(prop.Replace("Tex", "N"), TextureFactory.NormalFromHeight(tex, normal));
            }
            Layer("_GrassTex", TextureFactory.GroundGrass, 4f);
            Layer("_DirtTex", TextureFactory.GroundDirt, 5f);
            Layer("_WheatTex", TextureFactory.GroundWheat, 4f);
            Layer("_MudTex", TextureFactory.GroundMud, 3f);
            Layer("_PloughTex", TextureFactory.GroundPlough, 7f);
            mat.SetFloat("_Tiling", 0.22f);
            mat.SetFloat("_NormalStrength", 1.1f);
            mat.SetColor("_GrassTint", MapLayout.Map.GrassTint);

            var go = Shapes.MeshObject("Ground", map, mesh, mat);
            go.GetComponent<MeshRenderer>().receiveShadows = true;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            GroundCollider = mc;
        }

        /// <summary>
        /// Рисует две карты: веса слоёв земли для шейдера (R — дорога, G — жнивьё, B — грязь, A — пашня; остальное трава)
        /// и цветную карту местности для миникарты.
        /// </summary>
        static Texture2D PaintMaps()
        {
            var ctrl = new Color32[TexSize * TexSize];
            var mini = new Color32[TexSize * TexSize];
            float scale = MapLayout.Size / TexSize;
            var grass = Shapes.Hex("#5a6e37");
            var wheat = Shapes.Hex("#b49a55");
            var plough = Shapes.Hex("#5a4632");
            var road = Shapes.Hex("#8a7656");
            var mud = Shapes.Hex("#4e4433");
            var water = Shapes.Hex("#3b5257");
            var forest = Shapes.Hex("#2f4325");

            System.Threading.Tasks.Parallel.For(0, TexSize, j =>
            {
                for (int i = 0; i < TexSize; i++)
                {
                    float x = -MapLayout.Half + (i + 0.5f) * scale, z = -MapLayout.Half + (j + 0.5f) * scale;
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    // Поля
                    Color mc = grass;
                    foreach (var f in MapLayout.Fields)
                    {
                        if (!f.rect.Contains(new Vector2(x, z))) continue;
                        // Мягкий край поля
                        float edge = Mathf.Min(Mathf.Min(x - f.rect.xMin, f.rect.xMax - x), Mathf.Min(z - f.rect.yMin, f.rect.yMax - z));
                        float k = MapLayout.Smooth01(edge / 2.5f);
                        if (f.kind == 0) { g = k; mc = Color.Lerp(grass, wheat, k); }
                        else if (f.kind == 1) { a = k; mc = Color.Lerp(grass, plough, k); }
                        break;
                    }
                    if (MapLayout.InForest(x, z)) mc = Color.Lerp(mc, forest, 0.75f);
                    // Утоптанная земля у баз и точек
                    foreach (Faction f in new[] { Faction.USSR, Faction.Germany })
                    {
                        float d = Vector2.Distance(new Vector2(x, z), new Vector2(MapLayout.Base(f).x, MapLayout.Base(f).z));
                        r = Mathf.Max(r, MapLayout.Smooth01((24f - d) / 8f) * 0.8f);
                    }
                    foreach (var p in MapLayout.PointXZ)
                        r = Mathf.Max(r, MapLayout.Smooth01((7f - Vector2.Distance(p, new Vector2(x, z))) / 3f) * 0.7f);
                    // Дороги
                    float rd = Mathf.Min(MapLayout.RoadDistance(x, z), MapLayout.RailDistance(x, z));
                    float roadK = MapLayout.Smooth01((3.4f - rd) / 1.4f);
                    r = Mathf.Max(r, roadK);
                    mc = Color.Lerp(mc, road, Mathf.Max(roadK, r * 0.6f));
                    // Река: грязные берега
                    float river = MapLayout.RiverDistance(x, z);
                    float mudK = MapLayout.Smooth01((MapLayout.RiverBankHalf + 3.5f - river) / 4f);
                    b = Mathf.Max(b, mudK);
                    mc = Color.Lerp(mc, mud, mudK);
                    if (MapLayout.InWater(x, z)) mc = water;
                    // Веса не больше единицы в сумме
                    float sum = r + g + b + a;
                    if (sum > 1f) { r /= sum; g /= sum; b /= sum; a /= sum; }
                    ctrl[j * TexSize + i] = new Color(r, g, b, a);
                    float shade = 0.9f + Mathf.PerlinNoise(x * 0.05f, z * 0.05f) * 0.2f;
                    mini[j * TexSize + i] = Shapes.Shade(mc, shade);
                }
            });

            var control = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, true, true) { name = "Control", wrapMode = TextureWrapMode.Clamp };
            control.SetPixels32(ctrl);
            control.Apply(true);
            MapTexture = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, true) { name = "MiniMap", wrapMode = TextureWrapMode.Clamp };
            MapTexture.SetPixels32(mini);
            MapTexture.Apply(true);
            return control;
        }

        // ---------- Река и мосты ----------

        static void BuildWater(Transform map)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float hw = MapLayout.RiverBedHalf + 3f;
            for (float z = -MapLayout.Half - 5f; z <= MapLayout.Half + 5.1f; z += 3f)
            {
                float x = MapLayout.RiverX(z);
                verts.Add(new Vector3(x - hw, MapLayout.WaterY, z));
                verts.Add(new Vector3(x + hw, MapLayout.WaterY, z));
                int s = verts.Count - 4;
                if (s >= 0)
                {
                    tris.Add(s); tris.Add(s + 2); tris.Add(s + 1);
                    tris.Add(s + 1); tris.Add(s + 2); tris.Add(s + 3);
                }
            }
            var mesh = new Mesh { name = "Water" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            var mat = new Material(Mats.WaterShader) { name = "Water" };
            mat.SetTexture("_WaveNormal", TextureFactory.WaveNormal);
            var go = Shapes.MeshObject("Water", map, mesh, mat);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void BuildBridges(Transform decor, Transform solids)
        {
            var plank = Shapes.Hex("#6b5a44");
            var dark = Shapes.Hex("#4a3d2e");
            using (Shapes.Brush(Surf.Planks))
                foreach (var b in MapLayout.Bridges)
                {
                    float cx = MapLayout.RiverX(b.z);
                    float len = MapLayout.BridgeHalfLength * 2f;
                    float w = b.halfWidth * 2f;
                    // Настил с коллайдером (по нему кликают и ходят) — поперечные доски
                    Shapes.Box(solids, new Vector3(cx, MapLayout.DeckY - 0.15f, b.z), new Vector3(len, 0.3f, w), plank, name: "BridgeDeck", keepCollider: true);
                    for (float x = -len / 2f + 0.15f; x < len / 2f; x += 0.32f)
                        Shapes.Box(decor, new Vector3(cx + x, MapLayout.DeckY + 0.02f, b.z), new Vector3(0.28f, 0.06f, w + 0.1f),
                            Shapes.Shade(plank, 0.85f + Mathf.PerlinNoise(x, b.z) * 0.3f));
                    // Перила, сваи, подкосы
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Shapes.Box(decor, new Vector3(cx, MapLayout.DeckY + 0.95f, b.z + s * (b.halfWidth - 0.1f)), new Vector3(len, 0.12f, 0.12f), dark);
                        Shapes.Box(decor, new Vector3(cx, MapLayout.DeckY + 0.5f, b.z + s * (b.halfWidth - 0.1f)), new Vector3(len, 0.08f, 0.08f), dark);
                        using (Shapes.Brush(Surf.Bark))
                            for (float x = -len / 2f; x <= len / 2f + 0.1f; x += b.vehicles ? 3f : 2.5f)
                            {
                                Shapes.Box(decor, new Vector3(cx + x, MapLayout.DeckY + 0.5f, b.z + s * (b.halfWidth - 0.1f)), new Vector3(0.15f, 1f, 0.15f), dark);
                                Shapes.Part(Shapes.LowCylinder, decor, new Vector3(cx + x, MapLayout.BedY + 1.4f, b.z + s * (b.halfWidth - 0.3f)), new Vector3(0.32f, 3.6f, 0.32f), dark);
                            }
                    }
                }
        }

        // ---------- Постройки ----------

        static float FootprintBase(Building b)
        {
            var rot = Quaternion.Euler(0f, b.yaw, 0f);
            float min = float.MaxValue;
            for (int sx = -1; sx <= 1; sx++)
                for (int sz = -1; sz <= 1; sz++)
                {
                    var p = b.center + rot * new Vector3(sx * b.size.x * 0.5f, 0f, sz * b.size.z * 0.5f);
                    min = Mathf.Min(min, MapLayout.Height(p.x, p.z));
                }
            return min;
        }

        static void BuildBuilding(Building b, Transform decor, Transform solids)
        {
            float y0 = FootprintBase(b);
            var root = Shapes.Group(b.kind.ToString(), decor, new Vector3(b.center.x, y0, b.center.z), new Vector3(0f, b.yaw, 0f));
            var s = b.size;
            float blockerHeight = s.y + 2f;

            switch (b.kind)
            {
                case BuildingKind.Izba: Izba(root, s, false, b.center); break;
                case BuildingKind.Ruin: Izba(root, s, true, b.center); blockerHeight = 1.8f; break;
                case BuildingKind.Barn: Barn(root, s); break;
                case BuildingKind.Church: Church(root, s); blockerHeight = 9f; break;
                case BuildingKind.Mill:
                    Mill(root, s);
                    root.Find("Sails").SetParent(solids, true); // крылья крутятся — их не склеиваем
                    break;
                case BuildingKind.Pillbox: Pillbox(root, s); blockerHeight = 1.6f; break;
                case BuildingKind.BrickHouse: BrickHouse(root, s, false, b.center); break;
                case BuildingKind.BrickRuin: BrickHouse(root, s, true, b.center); blockerHeight = 2.5f; break;
                case BuildingKind.Station: Station(root, s); break;
                case BuildingKind.Warehouse: Warehouse(root, s); break;
                case BuildingKind.Elevator: Elevator(root, s); blockerHeight = s.y + 4f; break;
                case BuildingKind.Chapel: Chapel(root, s); blockerHeight = 9f; break;
            }

            // Коллайдер для линии видимости и кликов (без сетки)
            var blocker = new GameObject("Blocker");
            blocker.transform.SetParent(solids, false);
            blocker.transform.position = new Vector3(b.center.x, y0 + blockerHeight / 2f - 0.5f, b.center.z);
            blocker.transform.rotation = Quaternion.Euler(0f, b.yaw, 0f);
            blocker.AddComponent<BoxCollider>().size = new Vector3(s.x, blockerHeight + 1f, s.z);
        }

        static readonly Color Log = Shapes.Hex("#8a7458");
        static readonly Color LogDark = Shapes.Hex("#5e4c3a");
        static readonly Color ThatchC = Shapes.Hex("#958662");
        static readonly Color Tes = Shapes.Hex("#8f897c");
        static readonly Color Shutter = Shapes.Hex("#4a6f8a");
        static readonly Color Frame = Shapes.Hex("#d8d2c0");
        static readonly Color GlassC = Shapes.Hex("#1a1f22");

        /// <summary>Изба: сруб из брёвен с выпусками по углам, двускатная крыша (солома или тёс), резные наличники со ставнями, труба.</summary>
        static void Izba(Transform root, Vector3 s, bool ruined, Vector3 seedPos)
        {
            bool thatch = Mathf.PerlinNoise(seedPos.x * 0.1f, seedPos.z * 0.1f) > 0.45f;
            // Брёвна: свежие, потемневшие и выцветшие до серебристо-серого — у каждой избы свои
            Color[] logs = { Shapes.Hex("#8a7458"), Shapes.Hex("#7a6650"), Shapes.Hex("#8e857a"), Shapes.Hex("#968672") };
            var logColor = logs[Mathf.Abs(Mathf.RoundToInt(seedPos.x * 7f + seedPos.z * 13f)) % logs.Length];
            using (Shapes.Brush(Surf.Stone))
                Shapes.Box(root, new Vector3(0f, -0.25f, 0f), new Vector3(s.x + 0.4f, 0.9f, s.z + 0.4f), Shapes.Hex("#7a756a"), name: "Foundation");
            if (ruined)
            {
                // Сгоревшая изба: обугленные венцы разной высоты, печь с трубой, груда брёвен
                var burnt = Shapes.Hex("#2e2620");
                using (Shapes.Brush(Surf.Logs))
                {
                    Shapes.Box(root, new Vector3(-s.x / 2f + 0.15f, 0.9f, 0f), new Vector3(0.32f, 1.8f, s.z), burnt);
                    Shapes.Box(root, new Vector3(0f, 0.55f, s.z / 2f - 0.15f), new Vector3(s.x, 1.1f, 0.32f), burnt);
                    Shapes.Box(root, new Vector3(s.x / 2f - 0.15f, 0.4f, -s.z * 0.2f), new Vector3(0.32f, 0.8f, s.z * 0.6f), Shapes.Shade(Log, 0.6f));
                }
                using (Shapes.Brush(Surf.Plaster))
                {
                    Shapes.Box(root, new Vector3(1.0f, 0.9f, s.z / 2f - 1.2f), new Vector3(1.8f, 1.8f, 1.6f), Shapes.Hex("#a8a196"), name: "Stove");
                    Shapes.Box(root, new Vector3(1.0f, 2.8f, s.z / 2f - 1.2f), new Vector3(0.7f, 2.4f, 0.7f), Shapes.Hex("#8f877c"), name: "Chimney");
                }
                using (Shapes.Brush(Surf.Bark))
                    for (int i = 0; i < 7; i++)
                        Shapes.Part(Shapes.LowCylinder, root, new Vector3(-1.5f + i * 0.5f, 0.2f + (i % 3) * 0.25f, -0.8f + i * 0.35f),
                            new Vector3(0.28f, 4.5f, 0.28f), i % 2 == 0 ? burnt : Shapes.Shade(Log, 0.5f), new Vector3(90f, 15f * i - 40f, 0f));
                return;
            }
            using (Shapes.Brush(Surf.Logs))
            {
                Shapes.Box(root, new Vector3(0f, s.y / 2f, 0f), s, logColor, name: "Walls");
                // Выпуски брёвен на углах сруба («в чашу»)
                for (int cx = -1; cx <= 1; cx += 2)
                    for (int cz = -1; cz <= 1; cz += 2)
                        for (float y = 0.15f; y < s.y; y += 0.25f)
                        {
                            bool alongX = Mathf.RoundToInt(y / 0.25f) % 2 == 0;
                            Shapes.Part(Shapes.LowCylinder, root, new Vector3(cx * (s.x / 2f + (alongX ? 0.18f : 0f)), y, cz * (s.z / 2f + (alongX ? 0f : 0.18f))),
                                new Vector3(0.26f, 0.5f, 0.26f), Shapes.Shade(logColor, 0.8f), alongX ? new Vector3(0f, 0f, 90f) : new Vector3(90f, 0f, 0f));
                        }
            }
            // Крыша: двускатная вдоль длинной стороны, со свесами; фронтоны — доски
            float roofH = 2.6f;
            var roof = Shapes.Prism(new List<Vector2> { new Vector2(0f, -s.x / 2f - 0.6f), new Vector2(0f, s.x / 2f + 0.6f), new Vector2(roofH, 0f) },
                s.z / 2f + 0.5f, s.z / 2f + 0.5f);
            Shapes.MeshObject("Roof", root, roof, Mats.Get(thatch ? ThatchC : Tes, thatch ? Surf.Thatch : Surf.Shingles), new Vector3(0f, s.y, 0f), new Vector3(0f, 90f, 0f));
            var gable = Shapes.Prism(new List<Vector2> { new Vector2(0f, -s.x / 2f), new Vector2(0f, s.x / 2f), new Vector2(roofH - 0.15f, 0f) }, 0.04f, 0.04f);
            for (int g = -1; g <= 1; g += 2)
                Shapes.MeshObject("Gable", root, gable, Mats.Get(Shapes.Hex("#5e4a36"), Surf.Planks), new Vector3(0f, s.y, g * (s.z / 2f + 0.02f)), new Vector3(0f, 90f, 0f));
            // Конёк
            using (Shapes.Brush(Surf.Bark))
                Shapes.Part(Shapes.LowCylinder, root, new Vector3(0f, s.y + roofH + 0.05f, 0f), new Vector3(0.3f, s.z + 1f, 0.3f), LogDark, new Vector3(90f, 0f, 0f));
            // Окна на торце (к дороге) и по бокам
            for (int i = -1; i <= 1; i += 2)
            {
                Window(root, new Vector3(i * s.x * 0.22f, s.y * 0.55f, s.z / 2f + 0.04f), 0f);
                Window(root, new Vector3(s.x / 2f + 0.04f, s.y * 0.55f, i * s.z * 0.22f), 90f);
                Window(root, new Vector3(-s.x / 2f - 0.04f, s.y * 0.55f, i * s.z * 0.22f), -90f);
            }
            // Дверь и крыльцо сбоку
            using (Shapes.Brush(Surf.Planks))
            {
                Shapes.Box(root, new Vector3(s.x / 2f + 0.05f, 1.0f, -s.z * 0.38f), new Vector3(0.08f, 2f, 1f), Shapes.Hex("#4c3c2c"), name: "Door");
                Shapes.Box(root, new Vector3(s.x / 2f + 0.6f, 0.2f, -s.z * 0.38f), new Vector3(1.1f, 0.4f, 1.4f), Shapes.Hex("#5e4c3a"), name: "Porch");
            }
            using (Shapes.Brush(Surf.Plaster))
                Shapes.Box(root, new Vector3(1.0f, s.y + 1.9f, -s.z * 0.15f), new Vector3(0.7f, 1.9f, 0.7f), Shapes.Hex("#9a9188"), name: "Chimney");
        }

        static void Window(Transform root, Vector3 pos, float yaw)
        {
            var w = Shapes.Group("Window", root, pos, new Vector3(0f, yaw, 0f));
            using (Shapes.Brush(Surf.Wood))
            {
                Shapes.Box(w, Vector3.zero, new Vector3(0.95f, 1.15f, 0.06f), Frame);
                Shapes.Box(w, new Vector3(0f, 0.68f, 0.02f), new Vector3(1.15f, 0.22f, 0.08f), Frame); // резной наличник
                Shapes.Box(w, new Vector3(-0.68f, 0f, 0f), new Vector3(0.4f, 1.15f, 0.05f), Shutter);
                Shapes.Box(w, new Vector3(0.68f, 0f, 0f), new Vector3(0.4f, 1.15f, 0.05f), Shutter);
            }
            Shapes.Box(w, new Vector3(0f, 0f, 0.02f), new Vector3(0.75f, 0.95f, 0.05f), GlassC, name: "Glass").GetComponent<MeshRenderer>().sharedMaterial = GlassMat;
            using (Shapes.Brush(Surf.Wood))
            {
                Shapes.Box(w, new Vector3(0f, 0f, 0.05f), new Vector3(0.05f, 0.95f, 0.03f), Frame);
                Shapes.Box(w, new Vector3(0f, 0.1f, 0.05f), new Vector3(0.75f, 0.05f, 0.03f), Frame);
            }
        }

        static Material glassMat;
        /// <summary>Стекло окон: тёмное, гладкое — отражает небо.</summary>
        static Material GlassMat
        {
            get
            {
                if (glassMat != null) return glassMat;
                glassMat = new Material(Mats.Get(GlassC, Surf.Paint)) { name = "WindowGlass" };
                glassMat.SetFloat("_Smoothness", 0.92f);
                glassMat.SetFloat("_Metallic", 0.3f);
                return glassMat;
            }
        }

        static void Barn(Transform root, Vector3 s)
        {
            using (Shapes.Brush(Surf.Stone))
                Shapes.Box(root, new Vector3(0f, -0.2f, 0f), new Vector3(s.x + 0.3f, 0.8f, s.z + 0.3f), Shapes.Hex("#77726a"));
            using (Shapes.Brush(Surf.Planks))
            {
                Shapes.Box(root, new Vector3(0f, s.y / 2f, 0f), s, Shapes.Hex("#5e4c38"), name: "Walls");
                Shapes.Box(root, new Vector3(0f, 1.6f, s.z / 2f + 0.05f), new Vector3(3.2f, 3.2f, 0.1f), Shapes.Hex("#4a3b2c"), name: "Gate");
            }
            var roof = Shapes.Prism(new List<Vector2> { new Vector2(0f, -s.x / 2f - 0.6f), new Vector2(0f, s.x / 2f + 0.6f), new Vector2(3.2f, 0f) },
                s.z / 2f + 0.5f, s.z / 2f + 0.5f);
            Shapes.MeshObject("Roof", root, roof, Mats.Get(Tes, Surf.Shingles), new Vector3(0f, s.y, 0f), new Vector3(0f, 90f, 0f));
            var gable = Shapes.Prism(new List<Vector2> { new Vector2(0f, -s.x / 2f), new Vector2(0f, s.x / 2f), new Vector2(3.05f, 0f) }, 0.04f, 0.04f);
            for (int g = -1; g <= 1; g += 2)
                Shapes.MeshObject("Gable", root, gable, Mats.Get(Shapes.Hex("#5a4834"), Surf.Planks), new Vector3(0f, s.y, g * (s.z / 2f + 0.02f)), new Vector3(0f, 90f, 0f));
        }

        /// <summary>Сельская церковь: белёные стены, зелёная кровля, колокольня с луковичным куполом и крестом, следы обстрела.</summary>
        static void Church(Transform root, Vector3 s)
        {
            var white = Shapes.Hex("#d6d0c2");
            var roofC = Shapes.Hex("#41604a");
            var gold = Shapes.Hex("#b0924a");
            using (Shapes.Brush(Surf.Stone))
                Shapes.Box(root, new Vector3(0f, -0.25f, 0f), new Vector3(s.x + 0.8f, 0.9f, s.z + 0.8f), Shapes.Hex("#7a766c"));
            using (Shapes.Brush(Surf.Plaster))
            {
                Shapes.Box(root, new Vector3(0f, s.y / 2f, -1f), new Vector3(s.x, s.y, s.z - 4f), white, name: "Nave");
                // Пилястры и карниз
                for (float z = -s.z / 2f + 2.5f; z < s.z / 2f - 2f; z += 2.5f)
                    for (int sx = -1; sx <= 1; sx += 2)
                        Shapes.Box(root, new Vector3(sx * (s.x / 2f + 0.06f), s.y / 2f, z), new Vector3(0.18f, s.y, 0.5f), Shapes.Shade(white, 0.96f));
                Shapes.Box(root, new Vector3(0f, s.y - 0.1f, -1f), new Vector3(s.x + 0.35f, 0.25f, s.z - 3.6f), Shapes.Shade(white, 0.92f));
                // Апсида
                Shapes.Part(Shapes.LowCylinder, root, new Vector3(0f, s.y * 0.4f, s.z / 2f - 0.6f), new Vector3(s.x * 0.7f, s.y * 0.8f, 3f), white);
            }
            var roof = Shapes.Prism(new List<Vector2> { new Vector2(0f, -s.x / 2f - 0.4f), new Vector2(0f, s.x / 2f + 0.4f), new Vector2(3f, 0f) },
                (s.z - 4f) / 2f + 0.3f, (s.z - 4f) / 2f + 0.3f);
            Shapes.MeshObject("Roof", root, roof, Mats.Get(roofC, Surf.Metal), new Vector3(0f, s.y, -1f), new Vector3(0f, 90f, 0f));
            // Окна-арки
            for (float z = -s.z / 2f + 3.6f; z < s.z / 2f - 2.5f; z += 2.5f)
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    Shapes.Box(root, new Vector3(sx * (s.x / 2f + 0.03f), s.y * 0.55f, z), new Vector3(0.05f, 1.8f, 0.8f), GlassC).GetComponent<MeshRenderer>().sharedMaterial = GlassMat;
                    Shapes.Part(Shapes.LowCylinder, root, new Vector3(sx * (s.x / 2f + 0.03f), s.y * 0.55f + 0.9f, z), new Vector3(0.8f, 0.05f, 0.8f), GlassC, new Vector3(0f, 0f, 90f));
                }
            // Колокольня спереди (к дороге — на юг, −Z)
            var tower = Shapes.Group("Tower", root, new Vector3(0f, 0f, -s.z / 2f + 1.6f));
            using (Shapes.Brush(Surf.Plaster))
            {
                Shapes.Box(tower, new Vector3(0f, (s.y + 6f) / 2f, 0f), new Vector3(3.6f, s.y + 6f, 3.6f), white);
                Shapes.Box(tower, new Vector3(0f, s.y + 2.2f, 0f), new Vector3(3.9f, 0.3f, 3.9f), Shapes.Shade(white, 0.92f));
            }
            Shapes.Box(tower, new Vector3(0f, s.y + 4.5f, 0f), new Vector3(3.7f, 1.6f, 1.2f), Shapes.Hex("#1c1a18")); // проёмы звонницы
            Shapes.Box(tower, new Vector3(0f, s.y + 4.5f, 0f), new Vector3(1.2f, 1.6f, 3.7f), Shapes.Hex("#1c1a18"));
            using (Shapes.Brush(Surf.Metal))
            {
                Shapes.Part(Shapes.LowCylinder, tower, new Vector3(0f, s.y + 6.5f, 0f), new Vector3(2.2f, 1f, 2.2f), roofC);
                Shapes.Part(Shapes.LowSphere, tower, new Vector3(0f, s.y + 7.8f, 0f), new Vector3(2.6f, 2.8f, 2.6f), roofC, name: "Dome");
                Shapes.Part(Shapes.Cone, tower, new Vector3(0f, s.y + 8.8f, 0f), new Vector3(1.2f, 1.6f, 1.2f), roofC);
                // Православный крест
                Shapes.Box(tower, new Vector3(0f, s.y + 11.2f, 0f), new Vector3(0.12f, 1.8f, 0.12f), gold);
                Shapes.Box(tower, new Vector3(0f, s.y + 11.5f, 0f), new Vector3(0.9f, 0.1f, 0.1f), gold);
                Shapes.Box(tower, new Vector3(0f, s.y + 11.85f, 0f), new Vector3(0.45f, 0.08f, 0.08f), gold);
                Shapes.Box(tower, new Vector3(0f, s.y + 10.8f, 0f), new Vector3(0.6f, 0.08f, 0.08f), gold, new Vector3(0f, 0f, -20f));
            }
            // Пробоина от снаряда с обнажённым кирпичом
            Shapes.Box(root, new Vector3(s.x / 2f + 0.02f, s.y * 0.62f, 1f), new Vector3(0.06f, 1.6f, 2f), Shapes.Hex("#1e1b18"));
            using (Shapes.Brush(Surf.Stone))
                Shapes.Box(root, new Vector3(s.x / 2f + 0.01f, s.y * 0.62f, 1f), new Vector3(0.05f, 2.2f, 2.7f), Shapes.Hex("#8a4a36"));
        }

        /// <summary>Ветряная мельница: сруб, корпус-шатёр, крылья с решёткой и парусиной (крутятся).</summary>
        static void Mill(Transform root, Vector3 s)
        {
            var wood = Shapes.Hex("#5a4834");
            using (Shapes.Brush(Surf.Logs))
                Shapes.Box(root, new Vector3(0f, 1.5f, 0f), new Vector3(s.x, 3f, s.z), Log);
            using (Shapes.Brush(Surf.Planks))
                Shapes.Box(root, new Vector3(0f, 5.2f, 0f), new Vector3(s.x * 0.75f, 4.6f, s.z * 0.75f), wood, name: "Body");
            using (Shapes.Brush(Surf.Shingles))
                Shapes.Part(Shapes.Cone, root, new Vector3(0f, 7.5f, 0f), new Vector3(s.x * 0.95f, 2.4f, s.z * 0.95f), Tes, new Vector3(0f, 45f, 0f));
            var hub = Shapes.Group("Sails", root, new Vector3(0f, 6.2f, -s.z * 0.45f));
            using (Shapes.Brush(Surf.Wood))
            {
                Shapes.Part(Shapes.LowCylinder, hub, Vector3.zero, new Vector3(0.5f, 0.6f, 0.5f), wood, new Vector3(90f, 0f, 0f));
                for (int i = 0; i < 4; i++)
                {
                    var arm = Shapes.Group("Arm", hub, Vector3.zero, new Vector3(0f, 0f, i * 90f + 15f));
                    Shapes.Box(arm, new Vector3(0f, 3.4f, -0.2f), new Vector3(0.18f, 6.8f, 0.12f), wood);
                    for (float y = 1.2f; y < 6.6f; y += 0.6f)
                        Shapes.Box(arm, new Vector3(0.55f, y, -0.22f), new Vector3(1.1f, 0.05f, 0.05f), wood);
                    using (Shapes.Brush(Surf.Burlap))
                        Shapes.Box(arm, new Vector3(0.55f, 4f, -0.26f), new Vector3(1.0f, 4.8f, 0.02f), Shapes.Hex("#c4bba3"));
                }
            }
            hub.gameObject.AddComponent<Spinner>().speed = 14f;
        }

        static void Pillbox(Transform root, Vector3 s)
        {
            var concrete = Shapes.Hex("#7d7b73");
            using (Shapes.Brush(Surf.Concrete))
            {
                Shapes.Box(root, new Vector3(0f, s.y / 2f - 0.3f, 0f), s, concrete);
                Shapes.Box(root, new Vector3(0f, s.y - 0.1f, 0f), new Vector3(s.x + 0.6f, 0.45f, s.z + 0.6f), Shapes.Shade(concrete, 0.92f));
            }
            Shapes.Box(root, new Vector3(0f, s.y * 0.55f, -s.z / 2f - 0.01f), new Vector3(2.2f, 0.35f, 0.05f), Shapes.Hex("#101010")); // амбразуры
            Shapes.Box(root, new Vector3(0f, s.y * 0.55f, s.z / 2f + 0.01f), new Vector3(2.2f, 0.35f, 0.05f), Shapes.Hex("#101010"));
            // Земляная обсыпка
            using (Shapes.Brush(Surf.Soil))
                Shapes.Part(Shapes.LowSphere, root, new Vector3(0f, -0.3f, 0f), new Vector3(s.x + 3f, 1.6f, s.z + 3f), Shapes.Hex("#56493a"));
        }

        // ---------- Изгороди, стены, мешки, заборы ----------

        static void BuildBarrier(Barrier b, Transform decor)
        {
            var a3 = new Vector3(b.a.x, 0f, b.a.y);
            var b3 = new Vector3(b.b.x, 0f, b.b.y);
            var dir = b3 - a3;
            float len = dir.magnitude;
            if (len < 0.1f) return;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            int pieces = Mathf.Max(1, Mathf.CeilToInt(len / 3f));
            var hedgeMat = Mats.Foliage("HedgeLeaves_" + MapLayout.Current, TextureFactory.LeafCluster("LeafHedge_" + MapLayout.Current, MapLayout.Map.HedgeLeaves, 0.022f, 701), new Color(1.2f, 1.18f, 1.0f), 0.08f, 0.7f);
            for (int i = 0; i < pieces; i++)
            {
                float t0 = (float)i / pieces, t1 = (i + 1f) / pieces;
                var p = Vector3.Lerp(a3, b3, (t0 + t1) / 2f);
                float pl = len / pieces;
                float y = MapLayout.Height(p.x, p.z);
                var g = Shapes.Group("Barrier", decor, new Vector3(p.x, y, p.z), new Vector3(0f, yaw, 0f));
                switch (b.kind)
                {
                    case BarrierKind.Hedge:
                        // Тёмная «сердцевина» не даёт просвечивать насквозь, снаружи — карточки листвы
                        Shapes.Box(g, new Vector3(0f, 0.75f, 0f), new Vector3(0.8f, 1.4f, pl), Shapes.Hex("#25351a"));
                        Shapes.MeshObject("Leaves", g, FoliageMeshes.Hedge(i % 4, pl + 0.4f, 1.8f, 1.3f), hedgeMat);
                        break;
                    case BarrierKind.StoneWall:
                        using (Shapes.Brush(Surf.Stone))
                        {
                            Shapes.Box(g, new Vector3(0f, 0.45f, 0f), new Vector3(0.6f, 1.1f, pl + 0.05f), Shapes.Hex("#8b867a"));
                            Shapes.Box(g, new Vector3(0f, 1.02f, 0f), new Vector3(0.72f, 0.12f, pl + 0.05f), Shapes.Hex("#77726a"));
                        }
                        break;
                    case BarrierKind.Sandbags:
                        using (Shapes.Brush(Surf.Burlap))
                            for (int row = 0; row < 3; row++)
                                for (float z = -pl / 2f + 0.3f; z < pl / 2f; z += 0.62f)
                                    Shapes.Part(Shapes.LowSphere, g, new Vector3(0f, 0.18f + row * 0.28f, z + (row % 2) * 0.3f - 0.15f),
                                        new Vector3(0.75f - row * 0.1f, 0.34f, 0.66f), row % 2 == 0 ? Shapes.Hex("#9c8a64") : Shapes.Hex("#8c7c58"));
                        break;
                    case BarrierKind.Fence:
                        using (Shapes.Brush(Surf.Wood))
                        {
                            var wood = Shapes.Hex("#6b5a46");
                            Shapes.Box(g, new Vector3(0f, 0.55f, 0f), new Vector3(0.06f, 0.08f, pl), wood);
                            Shapes.Box(g, new Vector3(0f, 0.95f, 0f), new Vector3(0.06f, 0.08f, pl), wood);
                            for (float z = -pl / 2f; z <= pl / 2f; z += 0.35f)
                                Shapes.Box(g, new Vector3(0f, 0.6f, z), new Vector3(0.05f, 1.2f + Mathf.PerlinNoise(z * 3f, p.x) * 0.2f, 0.12f), Shapes.Shade(wood, 0.85f + Mathf.PerlinNoise(z * 5f, p.z) * 0.3f));
                        }
                        break;
                }
            }
        }

        // ---------- Деревья ----------

        static void BuildTrees(Transform decor, Transform solids)
        {
            var rnd = new System.Random(7);
            var bark = Shapes.Hex("#4b3d30");
            var birchBark = Shapes.Hex("#d6d2c6");
            var oakMat = Mats.Foliage("OakLeaves_" + MapLayout.Current, TextureFactory.LeafCluster("LeafOak_" + MapLayout.Current, MapLayout.Map.OakLeaves, 0.032f, 711), new Color(1.25f, 1.22f, 1.0f), 0.35f, 1.0f);
            var birchMat = Mats.Foliage("BirchLeaves_" + MapLayout.Current, TextureFactory.LeafCluster("LeafBirch_" + MapLayout.Current, MapLayout.Map.BirchLeaves, 0.026f, 713), new Color(1.25f, 1.22f, 1.0f), 0.45f, 1.2f);
            var pineMat = Mats.Foliage("PineNeedles", TextureFactory.PineBranch, new Color(1.3f, 1.3f, 1.05f), 0.2f, 0.5f);
            foreach (var t in MapLayout.Trees)
            {
                float x = t.x, z = t.z, s = t.y;
                float y = MapLayout.Height(x, z) - 0.1f;
                var g = Shapes.Group("Tree", decor, new Vector3(x, y, z), new Vector3(0f, (float)rnd.NextDouble() * 360f, 0f));
                g.localScale = Vector3.one * s;
                double kind = rnd.NextDouble();
                int variant = rnd.Next(4);
                if (kind < 0.45)
                {
                    // Ель: высокий ствол, ярусы лап
                    using (Shapes.Brush(Surf.Bark))
                        Shapes.Part(Shapes.Cone, g, Vector3.zero, new Vector3(0.45f, 12f, 0.45f), bark);
                    Shapes.MeshObject("Needles", g, FoliageMeshes.Pine(variant, 12f, 3.2f), pineMat);
                }
                else if (kind < 0.72)
                {
                    // Берёза: белый ствол с чёрными пятнами, лёгкая крона
                    using (Shapes.Brush(Surf.Bark))
                    {
                        Shapes.Part(Shapes.LowCylinder, g, new Vector3(0f, 3.5f, 0f), new Vector3(0.3f, 7f, 0.3f), birchBark);
                        for (int i = 0; i < 4; i++)
                            Shapes.Box(g, new Vector3(0f, 1f + i * 1.4f, 0f), new Vector3(0.31f, 0.08f, 0.2f), Shapes.Hex("#2a2622"), new Vector3(0f, i * 50f, 0f));
                    }
                    Shapes.MeshObject("Leaves", g, FoliageMeshes.Broadleaf(variant, 9f, 2.6f, 85), birchMat);
                }
                else
                {
                    // Дуб/липа: толстый ствол, сучья, широкая крона
                    using (Shapes.Brush(Surf.Bark))
                    {
                        Shapes.Part(Shapes.LowCylinder, g, new Vector3(0f, 2.2f, 0f), new Vector3(0.6f, 4.4f, 0.6f), bark);
                        Shapes.Part(Shapes.LowCylinder, g, new Vector3(0.6f, 4.4f, 0f), new Vector3(0.28f, 2.4f, 0.28f), bark, new Vector3(0f, 0f, -35f));
                        Shapes.Part(Shapes.LowCylinder, g, new Vector3(-0.5f, 4.6f, 0.2f), new Vector3(0.25f, 2.2f, 0.25f), bark, new Vector3(10f, 0f, 30f));
                    }
                    Shapes.MeshObject("Leaves", g, FoliageMeshes.Broadleaf(variant + 10, 9.5f, 3.6f, 130), oakMat);
                }
                // Ствол заслоняет линию огня
                var trunk = new GameObject("Trunk");
                trunk.transform.SetParent(solids, false);
                trunk.transform.position = new Vector3(x, y + 2.5f, z);
                var cap = trunk.AddComponent<CapsuleCollider>();
                cap.radius = 0.3f * s;
                cap.height = 5f;
            }
        }


        // ---------- Городские постройки (Мелитополь) ----------

        static readonly Color[] BrickColors = { Shapes.Hex("#9a5a42"), Shapes.Hex("#8c5440"), Shapes.Hex("#a36a4e") };
        static readonly Color[] Whitewash = { Shapes.Hex("#d8d0bc"), Shapes.Hex("#cfc5a8"), Shapes.Hex("#c9c2b4") };

        /// <summary>Кирпичный дом в 1–2 этажа: кирпич или побелка, вальмовая жестяная крыша, окна с наличниками. Руина — стены без крыши.</summary>
        static void BrickHouse(Transform root, Vector3 s, bool ruined, Vector3 seed)
        {
            int k = Mathf.Abs(Mathf.RoundToInt(seed.x * 3f + seed.z * 7f));
            bool plastered = k % 3 == 0;
            var wall = plastered ? Whitewash[k % Whitewash.Length] : BrickColors[k % BrickColors.Length];
            var wallSurf = plastered ? Surf.Plaster : Surf.Brick;
            using (Shapes.Brush(Surf.Stone))
                Shapes.Box(root, new Vector3(0f, -0.2f, 0f), new Vector3(s.x + 0.3f, 0.9f, s.z + 0.3f), Shapes.Hex("#77726a"));
            bool twoFloors = s.y > 8f;
            if (ruined)
            {
                // Обгоревшие стены неравной высоты, проломы, груда кирпича
                using (Shapes.Brush(wallSurf))
                {
                    var burnt = Shapes.Shade(wall, 0.65f);
                    float h0 = s.y * 0.75f, h1 = s.y * 0.35f, h2 = s.y * 0.55f;
                    Shapes.Box(root, new Vector3(0f, h0 / 2f, s.z / 2f - 0.25f), new Vector3(s.x, h0, 0.5f), burnt);
                    Shapes.Box(root, new Vector3(-s.x / 2f + 0.25f, h2 / 2f, 0f), new Vector3(0.5f, h2, s.z), burnt);
                    Shapes.Box(root, new Vector3(s.x * 0.25f, h1 / 2f, -s.z / 2f + 0.25f), new Vector3(s.x * 0.5f, h1, 0.5f), burnt);
                    Shapes.Box(root, new Vector3(s.x / 2f - 0.25f, h1 * 0.6f, s.z * 0.2f), new Vector3(0.5f, h1 * 1.2f, s.z * 0.5f), burnt);
                }
                using (Shapes.Brush(Surf.Brick))
                    for (int i = 0; i < 6; i++)
                        Shapes.Part(Shapes.LowSphere, root, new Vector3(-s.x * 0.3f + i * s.x * 0.12f, 0.2f, -s.z * 0.1f + (i % 3) * 0.8f),
                            new Vector3(2.4f, 1.0f + (i % 2) * 0.4f, 2f), Shapes.Hex("#7d4c3a"));
                using (Shapes.Brush(Surf.Wood))
                    for (int i = 0; i < 4; i++)
                        Shapes.Box(root, new Vector3(-1f + i * 0.7f, 0.9f, 0.5f), new Vector3(0.2f, 0.2f, s.z * 0.7f), Shapes.Hex("#2b241e"), new Vector3(20f + i * 8f, 15f * i, 0f));
                return;
            }
            using (Shapes.Brush(wallSurf))
            {
                Shapes.Box(root, new Vector3(0f, s.y / 2f, 0f), s, wall, name: "Walls");
                // Карниз и межэтажный пояс
                Shapes.Box(root, new Vector3(0f, s.y - 0.15f, 0f), new Vector3(s.x + 0.3f, 0.3f, s.z + 0.3f), Shapes.Shade(wall, 0.9f));
                if (twoFloors) Shapes.Box(root, new Vector3(0f, s.y * 0.5f, 0f), new Vector3(s.x + 0.12f, 0.2f, s.z + 0.12f), Shapes.Shade(wall, 0.92f));
            }
            // Вальмовая крыша из жести
            float rh = 2.2f;
            var roofPts = new List<Vector2> { new Vector2(0f, -s.x / 2f - 0.4f), new Vector2(0f, s.x / 2f + 0.4f), new Vector2(rh, 0.6f), new Vector2(rh, -0.6f) };
            var roof = Shapes.Prism(roofPts, s.z / 2f + 0.4f, Mathf.Max(0.5f, s.z / 2f - s.x * 0.35f));
            Shapes.MeshObject("Roof", root, roof, Mats.Get(k % 2 == 0 ? Shapes.Hex("#5e6660") : Shapes.Hex("#6a4a3a"), Surf.Metal), new Vector3(0f, s.y, 0f), new Vector3(0f, 90f, 0f));
            // Окна: по фасадам, на одном или двух этажах
            int floors = twoFloors ? 2 : 1;
            for (int f = 0; f < floors; f++)
            {
                float y = (f + 0.55f) * s.y / floors;
                for (float x = -s.x / 2f + 1.6f; x < s.x / 2f - 1f; x += 2.4f)
                {
                    TallWindow(root, new Vector3(x, y, s.z / 2f + 0.05f), 0f, wall);
                    TallWindow(root, new Vector3(x, y, -s.z / 2f - 0.05f), 180f, wall);
                }
                for (float z = -s.z / 2f + 1.6f; z < s.z / 2f - 1f; z += 2.6f)
                {
                    TallWindow(root, new Vector3(s.x / 2f + 0.05f, y, z), 90f, wall);
                    TallWindow(root, new Vector3(-s.x / 2f - 0.05f, y, z), -90f, wall);
                }
            }
            // Следы боёв: выбоины от пуль и снарядов
            for (int i = 0; i < 3; i++)
                Shapes.Box(root, new Vector3(-s.x * 0.3f + i * s.x * 0.3f, s.y * (0.3f + 0.2f * (i % 2)), s.z / 2f + 0.06f), new Vector3(0.6f + i * 0.2f, 0.5f, 0.04f), Shapes.Hex("#4a3a32"));
        }

        static void TallWindow(Transform root, Vector3 pos, float yaw, Color wall)
        {
            var w = Shapes.Group("Window", root, pos, new Vector3(0f, yaw, 0f));
            using (Shapes.Brush(Surf.Plaster))
                Shapes.Box(w, new Vector3(0f, 0f, 0f), new Vector3(1.15f, 1.75f, 0.06f), Shapes.Hex("#d6cfc0"));
            Shapes.Box(w, new Vector3(0f, 0f, 0.02f), new Vector3(0.9f, 1.5f, 0.05f), GlassC, name: "Glass").GetComponent<MeshRenderer>().sharedMaterial = GlassMat;
            using (Shapes.Brush(Surf.Wood))
            {
                Shapes.Box(w, new Vector3(0f, 0f, 0.05f), new Vector3(0.06f, 1.5f, 0.03f), Shapes.Hex("#e0dccf"));
                Shapes.Box(w, new Vector3(0f, 0.3f, 0.05f), new Vector3(0.9f, 0.06f, 0.03f), Shapes.Hex("#e0dccf"));
            }
            using (Shapes.Brush(Surf.Stone))
                Shapes.Box(w, new Vector3(0f, -0.92f, 0.08f), new Vector3(1.25f, 0.1f, 0.18f), Shapes.Shade(wall, 0.85f));
        }

        /// <summary>Вокзал: двухэтажное здание с башенкой и часами, навес над перроном вдоль путей.</summary>
        static void Station(Transform root, Vector3 s)
        {
            var wall = Shapes.Hex("#d3c8a8");
            using (Shapes.Brush(Surf.Stone))
                Shapes.Box(root, new Vector3(0f, -0.2f, 0f), new Vector3(s.x + 0.6f, 0.9f, s.z + 0.6f), Shapes.Hex("#7a756c"));
            using (Shapes.Brush(Surf.Plaster))
            {
                Shapes.Box(root, new Vector3(0f, s.y / 2f, 0f), s, wall, name: "Walls");
                Shapes.Box(root, new Vector3(0f, s.y - 0.15f, 0f), new Vector3(s.x + 0.4f, 0.3f, s.z + 0.4f), Shapes.Shade(wall, 0.9f));
                // Центральная башенка
                Shapes.Box(root, new Vector3(0f, s.y + 2.5f, 0f), new Vector3(6f, 5f, 6f), wall);
                Shapes.Box(root, new Vector3(0f, s.y + 5.1f, 0f), new Vector3(6.4f, 0.3f, 6.4f), Shapes.Shade(wall, 0.9f));
            }
            using (Shapes.Brush(Surf.Metal))
            {
                var roof = Shapes.Prism(new List<Vector2> { new Vector2(0f, -s.z / 2f - 0.5f), new Vector2(0f, s.z / 2f + 0.5f), new Vector2(2.6f, 0f) }, s.x / 2f + 0.5f, s.x / 2f + 0.5f);
                Shapes.MeshObject("Roof", root, roof, Mats.Get(Shapes.Hex("#56605a"), Surf.Metal), new Vector3(0f, s.y, 0f));
                Shapes.Part(Shapes.Cone, root, new Vector3(0f, s.y + 5.2f, 0f), new Vector3(6.4f, 3.2f, 6.4f), Shapes.Hex("#56605a"), new Vector3(0f, 45f, 0f));
            }
            // Часы на башне
            Shapes.Part(Shapes.LowCylinder, root, new Vector3(0f, s.y + 3f, -3.05f), new Vector3(1.6f, 0.08f, 1.6f), Shapes.Hex("#e8e4d8"), new Vector3(90f, 0f, 0f));
            for (float x = -s.x / 2f + 2f; x < s.x / 2f - 1f; x += 3f)
                for (int f = 0; f < 2; f++)
                {
                    TallWindow(root, new Vector3(x, (f + 0.55f) * s.y / 2f, -s.z / 2f - 0.05f), 180f, wall);
                    TallWindow(root, new Vector3(x, (f + 0.55f) * s.y / 2f, s.z / 2f + 0.05f), 0f, wall);
                }
            // Навес над перроном (к путям, на юг)
            using (Shapes.Brush(Surf.Metal))
            {
                Shapes.Box(root, new Vector3(0f, 4.2f, -s.z / 2f - 3.2f), new Vector3(s.x + 6f, 0.15f, 6f), Shapes.Hex("#4e5650"), new Vector3(-6f, 0f, 0f));
                for (float x = -s.x / 2f - 2f; x <= s.x / 2f + 2f; x += 6f)
                    Shapes.Part(Shapes.LowCylinder, root, new Vector3(x, 2f, -s.z / 2f - 5.6f), new Vector3(0.2f, 4f, 0.2f), Shapes.Hex("#3c403c"));
            }
            using (Shapes.Brush(Surf.Concrete))
                Shapes.Box(root, new Vector3(0f, 0.25f, -s.z / 2f - 3.5f), new Vector3(s.x + 8f, 0.6f, 6.5f), Shapes.Hex("#8a867c"), name: "Platform");
        }

        /// <summary>Пакгауз: кирпичный склад с двускатной крышей и широкими воротами.</summary>
        static void Warehouse(Transform root, Vector3 s)
        {
            var wall = Shapes.Hex("#8e5a44");
            using (Shapes.Brush(Surf.Brick))
                Shapes.Box(root, new Vector3(0f, s.y / 2f, 0f), s, wall, name: "Walls");
            var roof = Shapes.Prism(new List<Vector2> { new Vector2(0f, -s.z / 2f - 0.4f), new Vector2(0f, s.z / 2f + 0.4f), new Vector2(2.2f, 0f) }, s.x / 2f + 0.4f, s.x / 2f + 0.4f);
            Shapes.MeshObject("Roof", root, roof, Mats.Get(Shapes.Hex("#5a5a52"), Surf.Metal), new Vector3(0f, s.y, 0f));
            using (Shapes.Brush(Surf.Planks))
                for (int i = -1; i <= 1; i += 2)
                    Shapes.Box(root, new Vector3(i * s.x * 0.25f, 1.7f, -s.z / 2f - 0.06f), new Vector3(3.2f, 3.4f, 0.1f), Shapes.Hex("#4a3a2c"), name: "Gate");
            for (float x = -s.x / 2f + 2f; x < s.x / 2f - 1f; x += 4f)
                Shapes.Box(root, new Vector3(x, s.y - 1.2f, s.z / 2f + 0.05f), new Vector3(1.2f, 0.7f, 0.05f), GlassC).GetComponent<MeshRenderer>().sharedMaterial = GlassMat;
        }

        /// <summary>Элеватор: ряды бетонных силосов, рабочая башня над ними, галерея. Самый высокий ориентир карты.</summary>
        static void Elevator(Transform root, Vector3 s)
        {
            var concrete = Shapes.Hex("#a8a294");
            using (Shapes.Brush(Surf.Concrete))
            {
                float r = 2.7f;
                for (int ix = 0; ix < 2; ix++)
                    for (int iz = 0; iz < 4; iz++)
                    {
                        var p = new Vector3(-s.x / 2f + r + ix * (r * 2f + 0.2f), (s.y - 4f) / 2f, -s.z / 2f + r + iz * (r * 2f + 0.3f));
                        Shapes.Part(Shapes.LowCylinder, root, p, new Vector3(r * 2f, s.y - 4f, r * 2f), Shapes.Shade(concrete, 0.95f + 0.05f * ((ix + iz) % 2)));
                    }
                Shapes.Box(root, new Vector3(0f, s.y - 3.7f, 0f), new Vector3(s.x, 0.6f, s.z), Shapes.Shade(concrete, 0.9f));
                // Рабочая башня
                Shapes.Box(root, new Vector3(s.x * 0.15f, s.y + 1f, -s.z * 0.2f), new Vector3(7f, 9f, 7f), concrete, name: "Tower");
                Shapes.Box(root, new Vector3(s.x * 0.15f, s.y + 5.7f, -s.z * 0.2f), new Vector3(7.6f, 0.5f, 7.6f), Shapes.Shade(concrete, 0.85f));
                // Галерея к приёмному складу
                Shapes.Box(root, new Vector3(s.x * 0.15f, s.y - 1.5f, s.z * 0.45f), new Vector3(2.5f, 2.5f, s.z * 0.5f), Shapes.Shade(concrete, 0.92f));
            }
            for (int i = 0; i < 3; i++)
                Shapes.Box(root, new Vector3(s.x * 0.15f - 3.55f, s.y + i * 2.4f, -s.z * 0.2f), new Vector3(0.05f, 1.2f, 1.6f), GlassC).GetComponent<MeshRenderer>().sharedMaterial = GlassMat;
            // Пробоина от снаряда в силосе
            Shapes.Box(root, new Vector3(-s.x / 2f - 0.05f, s.y * 0.6f, -s.z * 0.1f), new Vector3(0.1f, 2.6f, 2.2f), Shapes.Hex("#2a2622"));
        }

        /// <summary>Кладбищенская часовня: белёный объём, купол-луковка, крест.</summary>
        static void Chapel(Transform root, Vector3 s)
        {
            var white = Shapes.Hex("#d9d2c2");
            using (Shapes.Brush(Surf.Plaster))
                Shapes.Box(root, new Vector3(0f, s.y / 2f, 0f), s, white);
            var roof = Shapes.Prism(new List<Vector2> { new Vector2(0f, -s.x / 2f - 0.3f), new Vector2(0f, s.x / 2f + 0.3f), new Vector2(2f, 0f) }, s.z / 2f + 0.3f, s.z / 2f + 0.3f);
            Shapes.MeshObject("Roof", root, roof, Mats.Get(Shapes.Hex("#41604a"), Surf.Metal), new Vector3(0f, s.y, 0f), new Vector3(0f, 90f, 0f));
            using (Shapes.Brush(Surf.Metal))
            {
                Shapes.Part(Shapes.LowCylinder, root, new Vector3(0f, s.y + 2.3f, 0f), new Vector3(1.6f, 1f, 1.6f), Shapes.Hex("#41604a"));
                Shapes.Part(Shapes.LowSphere, root, new Vector3(0f, s.y + 3.3f, 0f), new Vector3(1.9f, 2f, 1.9f), Shapes.Hex("#b0924a"));
                Shapes.Box(root, new Vector3(0f, s.y + 5f, 0f), new Vector3(0.1f, 1.4f, 0.1f), Shapes.Hex("#b0924a"));
                Shapes.Box(root, new Vector3(0f, s.y + 5.2f, 0f), new Vector3(0.7f, 0.08f, 0.08f), Shapes.Hex("#b0924a"));
            }
            Shapes.Box(root, new Vector3(0f, 1.2f, s.z / 2f + 0.04f), new Vector3(1.4f, 2.4f, 0.06f), Shapes.Hex("#3a2e24"), name: "Door");
        }

        // ---------- Железная дорога ----------

        /// <summary>Насыпь из щебня, шпалы и рельсы вдоль ломаных MapLayout.Railways (на мосту — по настилу).</summary>
        static void BuildRailways(Transform decor)
        {
            foreach (var line in MapLayout.Railways)
                for (int i = 0; i + 1 < line.Length; i++)
                {
                    var a = line[i];
                    var b = line[i + 1];
                    var d = b - a;
                    float len = d.magnitude;
                    float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                    var dir = d / len;
                    // Шпалы через 0.75 м, щебень кусками по 6 м, рельсы кусками по 6 м — повторяют рельеф
                    for (float t = 0f; t < len; t += 6f)
                    {
                        var p = a + dir * (t + 3f);
                        float y = MapLayout.WalkHeight(p.x, p.y);
                        bool onBridge = MapLayout.RiverDistance(p.x, p.y) < MapLayout.BridgeHalfLength;
                        var g = Shapes.Group("Rail", decor, new Vector3(p.x, y, p.y), new Vector3(0f, yaw, 0f));
                        if (!onBridge)
                            using (Shapes.Brush(Surf.Soil))
                                Shapes.Box(g, new Vector3(0f, 0.05f, 0f), new Vector3(3.4f, 0.25f, 6.05f), Shapes.Hex("#6e675c"));
                        using (Shapes.Brush(Surf.Wood))
                            for (float k = -2.6f; k <= 2.7f; k += 0.75f)
                                Shapes.Box(g, new Vector3(0f, 0.2f, k), new Vector3(2.5f, 0.12f, 0.24f), Shapes.Hex("#3e3328"));
                        using (Shapes.Brush(Surf.Metal))
                            for (int sgn = -1; sgn <= 1; sgn += 2)
                                Shapes.Box(g, new Vector3(sgn * 0.76f, 0.33f, 0f), new Vector3(0.08f, 0.14f, 6.05f), Shapes.Hex("#5a514a"));
                    }
                }
        }

        // ---------- Мелочи: столбы, кресты, ежи, стога, подбитая техника, воронки ----------

        static void BuildProps(Transform decor)
        {
            var rnd = new System.Random(11);
            foreach (var pr in MapLayout.Props)
            {
                var p = MapLayout.Ground(pr.xz.x, pr.xz.y);
                var g = Shapes.Group(pr.kind.ToString(), decor, p, new Vector3(0f, pr.yaw, 0f));
                switch (pr.kind)
                {
                    case PropKind.Pole:
                        using (Shapes.Brush(Surf.Bark))
                        {
                            Shapes.Part(Shapes.LowCylinder, g, Vector3.up * 3.5f, new Vector3(0.22f, 7f, 0.22f), Shapes.Hex("#5a4a3a"));
                            Shapes.Box(g, Vector3.up * 6.6f, new Vector3(1.6f, 0.12f, 0.12f), Shapes.Hex("#5a4a3a"));
                        }
                        break;
                    case PropKind.Cross:
                        using (Shapes.Brush(Surf.Wood))
                        {
                            Shapes.Box(g, new Vector3(0f, 0.75f, 0f), new Vector3(0.1f, 1.5f, 0.1f), Shapes.Hex("#4a3e32"));
                            Shapes.Box(g, new Vector3(0f, 1.15f, 0f), new Vector3(0.6f, 0.08f, 0.08f), Shapes.Hex("#4a3e32"));
                            Shapes.Box(g, new Vector3(0f, 0.65f, 0f), new Vector3(0.35f, 0.06f, 0.06f), Shapes.Hex("#4a3e32"), new Vector3(0f, 0f, -20f));
                        }
                        using (Shapes.Brush(Surf.Soil))
                            Shapes.Box(g, new Vector3(0f, 0.08f, -0.9f), new Vector3(0.8f, 0.2f, 1.7f), Shapes.Hex("#5a4c3a"));
                        break;
                    case PropKind.Tombstone:
                        using (Shapes.Brush(Surf.Stone))
                        {
                            Shapes.Box(g, new Vector3(0f, 0.45f, 0f), new Vector3(0.6f, 0.9f, 0.18f), Shapes.Hex("#8e8a80"));
                            Shapes.Box(g, new Vector3(0f, 0.1f, -0.8f), new Vector3(0.75f, 0.2f, 1.6f), Shapes.Hex("#77736a"));
                        }
                        break;
                    case PropKind.TankTrap:
                        // Противотанковый «ёж»: три сваренные балки
                        using (Shapes.Brush(Surf.Metal))
                            for (int i = 0; i < 3; i++)
                                Shapes.Box(g, new Vector3(0f, 0.6f, 0f), new Vector3(0.14f, 1.9f, 0.14f), Shapes.Hex("#3a3632"),
                                    i == 0 ? new Vector3(0f, 0f, 55f) : i == 1 ? new Vector3(0f, 0f, -55f) : new Vector3(55f, 0f, 0f));
                        break;
                    case PropKind.HayStack:
                        using (Shapes.Brush(Surf.Thatch))
                        {
                            Shapes.Part(Shapes.LowSphere, g, Vector3.up * 1.1f, new Vector3(3f, 3.2f, 3f), Shapes.Hex("#a28d5a"));
                            Shapes.Part(Shapes.Cone, g, Vector3.up * 2.2f, new Vector3(1.8f, 1.4f, 1.8f), Shapes.Hex("#938050"));
                        }
                        break;
                    case PropKind.Wreck:
                        // Сгоревший грузовик: рама, кабина, кузов, колёса
                        var burnt = Shapes.Hex("#3a332c");
                        using (Shapes.Brush(Surf.Armor))
                        {
                            Shapes.Box(g, new Vector3(0f, 0.7f, 0f), new Vector3(1.9f, 0.25f, 6f), burnt);
                            Shapes.Box(g, new Vector3(0f, 1.55f, 1.9f), new Vector3(1.9f, 1.5f, 1.6f), Shapes.Shade(burnt, 1.1f));
                            Shapes.Box(g, new Vector3(0f, 1.1f, 3.0f), new Vector3(1.4f, 0.9f, 0.9f), burnt);
                            Shapes.Box(g, new Vector3(0f, 1.2f, -1.2f), new Vector3(2.1f, 0.8f, 3.4f), Shapes.Shade(burnt, 0.9f), new Vector3(0f, 0f, 6f));
                        }
                        using (Shapes.Brush(Surf.Leather))
                            foreach (var w in new[] { new Vector3(-0.95f, 0.45f, 2.4f), new Vector3(0.95f, 0.45f, 2.4f), new Vector3(-0.95f, 0.45f, -1.8f), new Vector3(0.95f, 0.45f, -1.8f) })
                                Shapes.Part(Shapes.LowCylinder, g, w, new Vector3(0.9f, 0.3f, 0.9f), Shapes.Hex("#1c1c1b"), new Vector3(0f, 0f, 90f));
                        break;
                }
            }
            // Старые воронки от прошлых боёв
            float H = MapLayout.Half;
            int craters = Mathf.RoundToInt(40f * (H * H) / (200f * 200f));
            for (int i = 0; i < craters; i++)
            {
                float x = -H * 0.85f + (float)rnd.NextDouble() * H * 1.7f, z = -H * 0.6f + (float)rnd.NextDouble() * H * 1.2f;
                if (MapLayout.RiverDistance(x, z) < 16f || MapLayout.RoadDistance(x, z) < 4f || MapLayout.RailDistance(x, z) < 5f) continue;
                if (SurfaceAt(x, z) == MapSurface.Blocked) continue;
                Effects.Crater(MapLayout.Ground(x, z), 1.2f + (float)rnd.NextDouble() * 1.5f, decor);
            }
        }

        // ---------- Базы ----------

        static void BuildBase(Faction f, Transform decor)
        {
            var c = MapLayout.Base(f);
            float s = Factions.NorthSign(f);
            var g = Shapes.Group("Base_" + f, decor, c, new Vector3(0f, s > 0 ? 180f : 0f, 0f));
            var canvas = f == Faction.USSR ? Shapes.Hex("#6b6a4c") : Shapes.Hex("#5f6152");
            // Штабные палатки
            var tent = Shapes.Prism(new List<Vector2> { new Vector2(0f, -3f), new Vector2(0f, 3f), new Vector2(2.6f, 0f) }, 3.5f, 3.5f);
            Shapes.MeshObject("Tent", g, tent, Mats.Get(canvas, Surf.Burlap), new Vector3(-14f, 0f, -6f), new Vector3(0f, 90f, 0f));
            Shapes.MeshObject("Tent", g, tent, Mats.Get(canvas, Surf.Burlap), new Vector3(14f, 0f, -6f), new Vector3(0f, 90f, 0f));
            // Кольцо мешков
            using (Shapes.Brush(Surf.Burlap))
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI / 6f;
                    var p = new Vector3(Mathf.Cos(a) * 22f, 0f, Mathf.Sin(a) * 16f);
                    if (Mathf.Abs(p.x) < 6f && p.z > 0f) continue; // проход к дороге
                    Shapes.Part(Shapes.LowSphere, g, p + Vector3.up * 0.3f, new Vector3(4f, 0.8f, 1.2f), Shapes.Hex("#958560"), name: "Sandbag");
                }
            // Ящики со снарядами и бочки
            using (Shapes.Brush(Surf.Wood))
                for (int i = 0; i < 5; i++)
                    Shapes.Box(g, new Vector3(-6f + i * 1.1f, 0.3f, -10f), new Vector3(1f, 0.6f, 0.6f), Shapes.Hex("#4f5a33"));
            using (Shapes.Brush(Surf.Metal))
                for (int i = 0; i < 4; i++)
                    Shapes.Part(Shapes.LowCylinder, g, new Vector3(5f + i * 0.75f, 0.45f, -10f), new Vector3(0.6f, 0.9f, 0.6f), Shapes.Hex("#3c4a30"));
        }
    }

    /// <summary>Вращает объект вокруг своей оси Z (крылья мельницы).</summary>
    public class Spinner : MonoBehaviour
    {
        public float speed = 10f;
        void Update() => transform.Rotate(0f, 0f, speed * Time.deltaTime, Space.Self);
    }
}
