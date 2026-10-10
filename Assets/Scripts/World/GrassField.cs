using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealPeace
{
    /// <summary>
    /// Трава и пшеница: сотни тысяч пучков, рисуются GPU-инстансингом только рядом с камерой.
    /// Карта разбита на квадраты 16×16 м, в каждом заранее разложены матрицы пучков (по карте слоёв земли:
    /// на дорогах, в грязи, на пашне и в воде травы нет; на полях пшеницы — колосья по пояс).
    /// Шейдер Foliage качает их ветром и «втягивает» в землю вдали.
    /// </summary>
    public class GrassField : MonoBehaviour
    {
        const float Chunk = 16f;
        const float DrawDistance = 140f;

        class Layer
        {
            public Mesh mesh;
            public Material material;
            public readonly Dictionary<Vector2Int, Matrix4x4[]> chunks = new Dictionary<Vector2Int, Matrix4x4[]>();
        }

        readonly List<Layer> layers = new List<Layer>();
        readonly Plane[] planes = new Plane[6];

        public static GrassField Build(Transform parent, System.Func<float, float, MapSurface> surfaceAt)
        {
            var go = new GameObject("Grass");
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<GrassField>();

            var grassTex = TextureFactory.GrassClump("GrassClump", new Color(0.16f, 0.22f, 0.07f), new Color(0.48f, 0.53f, 0.24f), 34, 601, false);
            var dryTex = TextureFactory.GrassClump("DryClump", new Color(0.3f, 0.29f, 0.14f), new Color(0.66f, 0.6f, 0.36f), 26, 603, false);
            var wheatTex = TextureFactory.GrassClump("WheatClump", new Color(0.45f, 0.4f, 0.2f), new Color(0.86f, 0.74f, 0.42f), 22, 605, true);

            var grass = new Layer { mesh = FoliageMeshes.Clump("Grass", 0.9f, 0.55f), material = Mats.Foliage("GrassMat", grassTex, Color.white, 0.12f, 0.9f, 90f, DrawDistance - 5f) };
            var dry = new Layer { mesh = FoliageMeshes.Clump("Dry", 0.8f, 0.45f), material = Mats.Foliage("DryMat", dryTex, Color.white, 0.1f, 0.7f, 90f, DrawDistance - 5f) };
            var wheat = new Layer { mesh = FoliageMeshes.Clump("Wheat", 0.8f, 1.1f), material = Mats.Foliage("WheatMat", wheatTex, Color.white, 0.25f, 0.8f, 100f, DrawDistance - 5f) };
            g.layers.Add(grass);
            g.layers.Add(dry);
            g.layers.Add(wheat);

            var rnd = new System.Random(77);
            var lists = new[] { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
            int n = Mathf.CeilToInt(MapLayout.Size / Chunk);
            for (int cz = 0; cz < n; cz++)
                for (int cx = 0; cx < n; cx++)
                {
                    foreach (var l in lists) l.Clear();
                    float x0 = -MapLayout.Half + cx * Chunk, z0 = -MapLayout.Half + cz * Chunk;
                    const float step = 0.85f;
                    for (float z = z0; z < z0 + Chunk; z += step)
                        for (float x = x0; x < x0 + Chunk; x += step)
                        {
                            float px = x + (float)rnd.NextDouble() * step, pz = z + (float)rnd.NextDouble() * step;
                            var s = surfaceAt(px, pz);
                            int layer;
                            float scale;
                            if (s == MapSurface.Wheat) { layer = 2; scale = 0.85f + (float)rnd.NextDouble() * 0.35f; }
                            else if (s == MapSurface.Grass)
                            {
                                // Трава гуще и выше в низинах, реже на буграх; кое-где сухая
                                float dens = Mathf.PerlinNoise(px * 0.08f + 3f, pz * 0.08f + 9f);
                                if (rnd.NextDouble() > 0.35 + dens * 0.65) continue;
                                layer = Mathf.PerlinNoise(px * 0.03f, pz * 0.03f) > 0.62f ? 1 : 0;
                                scale = 0.6f + dens * 0.8f + (float)rnd.NextDouble() * 0.3f;
                            }
                            else continue;
                            var pos = new Vector3(px, MapLayout.Height(px, pz) - 0.03f, pz);
                            var rot = Quaternion.Euler(((float)rnd.NextDouble() - 0.5f) * 14f, (float)rnd.NextDouble() * 360f, ((float)rnd.NextDouble() - 0.5f) * 14f);
                            lists[layer].Add(Matrix4x4.TRS(pos, rot, new Vector3(scale, scale * (0.8f + (float)rnd.NextDouble() * 0.4f), scale)));
                        }
                    for (int i = 0; i < 3; i++)
                        if (lists[i].Count > 0) g.layers[i].chunks[new Vector2Int(cx, cz)] = lists[i].ToArray();
                }
            return g;
        }

        public static GrassField I { get; private set; }

        void Awake() => I = this;

        void LateUpdate()
        {
            if (Camera.main != null) Submit(Camera.main);
        }

        /// <summary>Отправить траву на отрисовку для камеры (автотест вызывает перед ручным рендером скриншота).</summary>
        public void Submit(Camera cam)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            // Центр — точка земли, на которую смотрит камера, и сама камера: трава рисуется вокруг обоих
            var camPos = cam.transform.position;
            int n = Mathf.CeilToInt(MapLayout.Size / Chunk);
            int ccx = Mathf.FloorToInt((camPos.x + MapLayout.Half) / Chunk), ccz = Mathf.FloorToInt((camPos.z + MapLayout.Half) / Chunk);
            int r = Mathf.CeilToInt(DrawDistance / Chunk) + 1;
            foreach (var layer in layers)
            {
                var rp = new RenderParams(layer.material)
                {
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = true,
                    layer = 0,
                };
                for (int cz = Mathf.Max(0, ccz - r); cz <= Mathf.Min(n - 1, ccz + r); cz++)
                    for (int cx = Mathf.Max(0, ccx - r); cx <= Mathf.Min(n - 1, ccx + r); cx++)
                    {
                        if (!layer.chunks.TryGetValue(new Vector2Int(cx, cz), out var mats)) continue;
                        var center = new Vector3(-MapLayout.Half + (cx + 0.5f) * Chunk, 0f, -MapLayout.Half + (cz + 0.5f) * Chunk);
                        center.y = MapLayout.Height(center.x, center.z);
                        if (Vector3.Distance(center, camPos) > DrawDistance + Chunk) continue;
                        var bounds = new Bounds(center + Vector3.up * 1f, new Vector3(Chunk + 2f, 12f, Chunk + 2f));
                        if (!GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                        rp.worldBounds = bounds;
                        Graphics.RenderMeshInstanced(rp, layer.mesh, 0, mats);
                    }
            }
        }
    }

    /// <summary>Что лежит на земле в точке — для травы и звуков шагов.</summary>
    public enum MapSurface { Grass, Road, Mud, Plough, Wheat, Water, Blocked }
}
