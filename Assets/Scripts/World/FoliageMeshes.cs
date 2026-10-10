using System.Collections.Generic;
using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// Сетки растительности из текстурных карточек: кроны лиственных деревьев (десятки карточек по объёму кроны
    /// со «сферическими» нормалями — крона освещается как облако листвы, а не как набор плоскостей),
    /// еловые ярусы из лап, пучки травы и колосьев (три скрещённые карточки).
    /// Красный канал цвета вершины — вес ветра: у ствола/корня 0, на краю кроны/кончике 1.
    /// </summary>
    public static class FoliageMeshes
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<Color> c = new List<Color>();
            public readonly List<int> t = new List<int>();

            /// <summary>Карточка: центр, оси (полуразмеры), нормали — от центра кроны, ветер по вершинам.</summary>
            public void Card(Vector3 center, Vector3 right, Vector3 up, Vector3 normalOrigin, float windBase, float windTop, bool uvFromBottom = false)
            {
                int s = v.Count;
                Vector3[] corners = { center - right - up, center + right - up, center + right + up, center - right + up };
                Vector2[] uvs = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                for (int i = 0; i < 4; i++)
                {
                    v.Add(corners[i]);
                    var nn = corners[i] - normalOrigin;
                    n.Add(nn.sqrMagnitude > 1e-6f ? nn.normalized : Vector3.up);
                    uv.Add(uvs[i]);
                    float w = i < 2 ? windBase : windTop;
                    c.Add(new Color(w, 0, 0, 1));
                }
                t.Add(s); t.Add(s + 2); t.Add(s + 1);
                t.Add(s); t.Add(s + 3); t.Add(s + 2);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetUVs(0, uv);
                m.SetColors(c);
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        /// <summary>Крона лиственного дерева (берёза, дуб): карточки по объёму эллипсоида. Начало — у земли.</summary>
        public static Mesh Broadleaf(int variant, float height, float radius, int cards)
        {
            string key = "Broad_" + variant + "_" + height + "_" + radius;
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            var rnd = new System.Random(variant * 97 + 3);
            var b = new Builder();
            var center = new Vector3(0f, height - radius * 0.85f, 0f);
            // Крона из нескольких пучков — неровный силуэт вместо идеального шара
            int clumps = 4 + rnd.Next(3);
            var clumpPos = new Vector3[clumps];
            var clumpR = new float[clumps];
            for (int c = 0; c < clumps; c++)
            {
                var d = Random3(rnd);
                d.y = d.y * 0.6f + 0.15f;
                clumpPos[c] = center + d * radius * 0.45f;
                clumpR[c] = radius * Mathf.Lerp(0.5f, 0.75f, (float)rnd.NextDouble());
            }
            for (int i = 0; i < cards; i++)
            {
                int c = rnd.Next(clumps);
                var dir = Random3(rnd);
                float r = Mathf.Lerp(0.45f, 1f, Mathf.Sqrt((float)rnd.NextDouble()));
                var p = clumpPos[c] + new Vector3(dir.x, dir.y * 0.8f, dir.z) * clumpR[c] * r;
                float size = radius * Mathf.Lerp(0.24f, 0.38f, (float)rnd.NextDouble());
                var outward = (p - center).normalized;
                var tangent = Vector3.Cross(outward, Random3(rnd)).normalized;
                var bitangent = Vector3.Cross(outward, tangent).normalized;
                float roll = (float)rnd.NextDouble() * Mathf.PI * 2f;
                var right = (tangent * Mathf.Cos(roll) + bitangent * Mathf.Sin(roll)) * size;
                var up = (bitangent * Mathf.Cos(roll) - tangent * Mathf.Sin(roll)) * size;
                float wind = Mathf.Clamp01(0.3f + Vector3.Distance(p, center) / radius * 0.7f);
                // Нормаль — от центра своего пучка: каждый пучок освещается как отдельное облачко листвы
                b.Card(p, right, up, Vector3.Lerp(center, clumpPos[c], 0.6f) - Vector3.up * radius * 0.2f, wind * 0.8f, wind);
            }
            m = b.Build(key);
            cache[key] = m;
            return m;
        }

        static Vector3 Random3(System.Random rnd)
        {
            var v = new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f);
            if (v.sqrMagnitude < 1e-4f) v = Vector3.right;
            return v.normalized;
        }

        /// <summary>Ель: ярусы лап, свисающих от ствола, к макушке короче. Начало — у земли.</summary>
        public static Mesh Pine(int variant, float height, float radius)
        {
            string key = "Pine_" + variant + "_" + height + "_" + radius;
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            var rnd = new System.Random(variant * 131 + 7);
            var b = new Builder();
            float bottom = height * 0.18f;
            int whorls = Mathf.RoundToInt(height / 1.1f);
            for (int w = 0; w < whorls; w++)
            {
                float t = (float)w / whorls;
                float y = Mathf.Lerp(bottom, height - 0.4f, t);
                float len = radius * (1f - t * 0.85f) + 0.25f;
                int arms = 7 - Mathf.RoundToInt(t * 3f);
                float spin = (float)rnd.NextDouble() * Mathf.PI * 2f;
                for (int a = 0; a < arms; a++)
                {
                    float ang = spin + a * Mathf.PI * 2f / arms + ((float)rnd.NextDouble() - 0.5f) * 0.4f;
                    var outDir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    // Лапа: от ствола наружу и вниз; карточка лежит почти горизонтально, текстура — ветка вдоль U
                    var droop = -0.35f - (float)rnd.NextDouble() * 0.2f;
                    var along = (outDir + Vector3.up * droop).normalized * len * 0.5f;
                    var side = Vector3.Cross(Vector3.up, outDir).normalized * len * 0.32f;
                    var tilt = Vector3.up * len * 0.12f;
                    var c = new Vector3(0f, y, 0f) + along;
                    b.Card(c, along, side + tilt, new Vector3(0f, y - 1.5f, 0f), 0.15f, 0.9f);
                }
            }
            // Макушка — пара вертикальных карточек
            for (int i = 0; i < 2; i++)
            {
                var r = Quaternion.Euler(0f, i * 90f, 0f) * Vector3.right * 0.45f;
                b.Card(new Vector3(0f, height - 0.3f, 0f), r, Vector3.up * 0.6f, new Vector3(0f, height - 2f, 0f), 0.6f, 1f);
            }
            m = b.Build(key);
            cache[key] = m;
            return m;
        }

        /// <summary>Пучок травы/колосьев: три скрещённые карточки, корень в начале координат.</summary>
        public static Mesh Clump(string name, float width, float height)
        {
            string key = "Clump_" + name;
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            var b = new Builder();
            for (int i = 0; i < 3; i++)
            {
                var right = Quaternion.Euler(0f, i * 60f, 0f) * Vector3.right * width * 0.5f;
                var up = Vector3.up * height * 0.5f;
                b.Card(up, right, up, new Vector3(0f, -height * 1.5f, 0f), 0f, 1f);
            }
            m = b.Build(key);
            m.bounds = new Bounds(Vector3.up * height * 0.5f, new Vector3(width + 1f, height + 1f, width + 1f));
            cache[key] = m;
            return m;
        }

        /// <summary>Кусок живой изгороди: карточки листвы по объёму вытянутого бокса (длина по Z).</summary>
        public static Mesh Hedge(int variant, float length, float height, float width)
        {
            string key = "Hedge_" + variant + "_" + length.ToString("F1");
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            var rnd = new System.Random(variant * 53 + 11);
            var b = new Builder();
            int cards = Mathf.RoundToInt(length * 6f);
            for (int i = 0; i < cards; i++)
            {
                var p = new Vector3(((float)rnd.NextDouble() - 0.5f) * width, (float)rnd.NextDouble() * height * 0.9f + 0.25f, ((float)rnd.NextDouble() - 0.5f) * length);
                var axis = new Vector3(0f, height * 0.45f, p.z);
                var outward = (p - axis);
                outward.z *= 0.2f;
                if (outward.sqrMagnitude < 1e-3f) outward = Vector3.right;
                outward.Normalize();
                var tangent = Vector3.Cross(outward, Random3(rnd)).normalized;
                var bitangent = Vector3.Cross(outward, tangent).normalized;
                float size = Mathf.Lerp(0.45f, 0.75f, (float)rnd.NextDouble());
                b.Card(p, tangent * size, bitangent * size, axis - outward * 0.2f, Mathf.Clamp01(p.y / height) * 0.5f, Mathf.Clamp01(p.y / height));
            }
            m = b.Build(key);
            cache[key] = m;
            return m;
        }
    }
}
