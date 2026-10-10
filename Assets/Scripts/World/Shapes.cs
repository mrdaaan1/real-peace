using System.Collections.Generic;
using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// Сборка мира из примитивов Unity (кубы, цилиндры, сферы) и простых сеток.
    /// Материалы кэшируются по цвету — на всю карту их немного.
    /// </summary>
    public static class Shapes
    {
        /// <summary>Тип поверхности для деталей, которые создаются сейчас (см. Brush).</summary>
        public static Surf CurrentSurf = Surf.Paint;

        /// <summary>
        /// «Кисть»: всё, что создано внутри using (Shapes.Brush(Surf.Logs)) { ... }, получает материал этого типа.
        /// Так строители изб, танков и солдат задают фактуру, не передавая её в каждый вызов.
        /// </summary>
        public static System.IDisposable Brush(Surf kind) => new BrushScope(kind);

        sealed class BrushScope : System.IDisposable
        {
            readonly Surf previous;
            public BrushScope(Surf kind) { previous = CurrentSurf; CurrentSurf = kind; }
            public void Dispose() => CurrentSurf = previous;
        }

        public static Material Mat(Color color) => Mats.Get(color, CurrentSurf);
        public static Material Mat(Color color, Surf kind) => Mats.Get(color, kind);

        /// <summary>Светящийся материал без освещения (вспышки, трассеры, кольца выделения).</summary>
        public static Material Unlit(Color color) => Mats.Glow(color, 1.6f);

        public static GameObject Make(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale,
            Color color, Vector3 localEuler = default, string name = null, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            if (name != null) go.name = name;
            if (!keepCollider)
            {
                var col = go.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = Mat(color);
            return go;
        }

        public static GameObject Box(Transform parent, Vector3 pos, Vector3 size, Color color, Vector3 euler = default, string name = null, bool keepCollider = false)
            => Make(PrimitiveType.Cube, parent, pos, size, color, euler, name, keepCollider);

        /// <summary>Цилиндр по диаметру и длине (примитив Unity имеет высоту 2).</summary>
        public static GameObject Cyl(Transform parent, Vector3 pos, float diameter, float length, Color color, Vector3 euler = default, string name = null)
            => Make(PrimitiveType.Cylinder, parent, pos, new Vector3(diameter, length / 2f, diameter), color, euler, name);

        public static GameObject Ball(Transform parent, Vector3 pos, Vector3 size, Color color, string name = null)
            => Make(PrimitiveType.Sphere, parent, pos, size, color, default, name);

        /// <summary>Пустой объект-контейнер.</summary>
        public static Transform Group(string name, Transform parent, Vector3 localPos = default, Vector3 localEuler = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            return go.transform;
        }

        public static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat, Vector3 pos = default, Vector3 euler = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        public static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        // ---------- Сетки ----------

        /// <summary>
        /// Призма по профилю вида сбоку: точки (y — высота, z — вперёд), вытянутая по X на ширину 2·halfWidth
        /// (снизу и сверху ширина может различаться — скаты). Профиль — выпуклый многоугольник в любом порядке обхода.
        /// Так делаются скошенные лобовые листы танков, крыши домов.
        /// </summary>
        public static Mesh Prism(IList<Vector2> profile, float halfWidthBottom, float halfWidthTop)
        {
            int n = profile.Count;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in profile) { minY = Mathf.Min(minY, p.x); maxY = Mathf.Max(maxY, p.x); }
            Vector3 V(int i, float side)
            {
                var p = profile[i];
                float k = maxY > minY ? (p.x - minY) / (maxY - minY) : 0f;
                float hw = Mathf.Lerp(halfWidthBottom, halfWidthTop, k);
                return new Vector3(side * hw, p.x, p.y);
            }

            var verts = new List<Vector3>();
            var tris = new List<int>();
            // Ориентированная площадь профиля в осях (y, z): от её знака зависит, куда смотрят нормали
            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                var a = profile[i]; var b = profile[(i + 1) % n];
                area += a.x * b.y - a.y * b.x;
            }
            bool ccw = area > 0f;

            // Боковые крышки: веер (0, i, i+1) смотрит в +X, если обход положительный
            for (int s = -1; s <= 1; s += 2)
            {
                int start = verts.Count;
                for (int i = 0; i < n; i++) verts.Add(V(i, s));
                bool forward = (s > 0) == ccw;
                for (int i = 1; i < n - 1; i++)
                {
                    if (forward) { tris.Add(start); tris.Add(start + i); tris.Add(start + i + 1); }
                    else { tris.Add(start); tris.Add(start + i + 1); tris.Add(start + i); }
                }
            }
            // Грани по периметру (отдельные вершины — острые рёбра)
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int start = verts.Count;
                verts.Add(V(i, -1)); verts.Add(V(j, -1)); verts.Add(V(j, 1)); verts.Add(V(i, 1));
                if (ccw) { tris.Add(start); tris.Add(start + 1); tris.Add(start + 2); tris.Add(start); tris.Add(start + 2); tris.Add(start + 3); }
                else { tris.Add(start); tris.Add(start + 2); tris.Add(start + 1); tris.Add(start); tris.Add(start + 3); tris.Add(start + 2); }
            }
            var mesh = new Mesh { name = "Prism" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Объект с готовой сеткой и цветом (low-poly детали: сферы, цилиндры, конусы).</summary>
        public static GameObject Part(Mesh mesh, Transform parent, Vector3 pos, Vector3 scale, Color color, Vector3 euler = default, string name = null)
        {
            var go = MeshObject(name ?? mesh.name, parent, mesh, Mat(color), pos, euler);
            go.transform.localScale = scale;
            return go;
        }

        static Mesh lowSphere, lowCylinder, cone;

        /// <summary>Сфера диаметром 1 из ~80 треугольников с плоским затенением (low-poly).</summary>
        public static Mesh LowSphere
        {
            get
            {
                if (lowSphere != null) return lowSphere;
                const int lat = 6, lon = 9;
                var grid = new Vector3[(lat + 1) * (lon + 1)];
                for (int i = 0; i <= lat; i++)
                    for (int j = 0; j <= lon; j++)
                    {
                        float th = Mathf.PI * i / lat, ph = 2f * Mathf.PI * j / lon;
                        grid[i * (lon + 1) + j] = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)) * 0.5f;
                    }
                var verts = new List<Vector3>();
                var tris = new List<int>();
                void Tri(Vector3 a, Vector3 b, Vector3 c)
                {
                    if ((a - b).sqrMagnitude < 1e-8f || (b - c).sqrMagnitude < 1e-8f || (a - c).sqrMagnitude < 1e-8f) return;
                    int s = verts.Count;
                    verts.Add(a); verts.Add(b); verts.Add(c);
                    tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
                }
                for (int i = 0; i < lat; i++)
                    for (int j = 0; j < lon; j++)
                    {
                        var a = grid[i * (lon + 1) + j];
                        var b = grid[i * (lon + 1) + j + 1];
                        var c = grid[(i + 1) * (lon + 1) + j];
                        var d = grid[(i + 1) * (lon + 1) + j + 1];
                        Tri(a, b, c);
                        Tri(b, d, c);
                    }
                lowSphere = Finish("LowSphere", verts, tris);
                return lowSphere;
            }
        }

        /// <summary>Цилиндр диаметром 1 и высотой 1 (от −0.5 до 0.5 по Y), 8 граней.</summary>
        public static Mesh LowCylinder
        {
            get
            {
                if (lowCylinder != null) return lowCylinder;
                lowCylinder = Lathe("LowCylinder", 8, 0.5f, 0.5f, -0.5f, 0.5f);
                return lowCylinder;
            }
        }

        /// <summary>Конус: основание диаметром 1 на y = 0, вершина на y = 1, 8 граней.</summary>
        public static Mesh Cone
        {
            get
            {
                if (cone != null) return cone;
                cone = Lathe("Cone", 8, 0.5f, 0f, 0f, 1f);
                return cone;
            }
        }

        /// <summary>Тело вращения: усечённый конус с радиусами r0 (низ) и r1 (верх), с крышками.</summary>
        static Mesh Lathe(string name, int sides, float r0, float r1, float y0, float y1)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                int s = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
            }
            for (int i = 0; i < sides; i++)
            {
                float a0 = 2f * Mathf.PI * i / sides, a1 = 2f * Mathf.PI * (i + 1) / sides;
                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                var b0 = d0 * r0 + Vector3.up * y0; var b1 = d1 * r0 + Vector3.up * y0;
                var t0 = d0 * r1 + Vector3.up * y1; var t1 = d1 * r1 + Vector3.up * y1;
                // Бок (обход по часовой, если смотреть снаружи)
                if (r1 > 0f) { Tri(b0, t1, b1); Tri(b0, t0, t1); }
                else Tri(b0, t0, b1);
                // Крышки
                Tri(Vector3.up * y0, b0, b1);
                if (r1 > 0f) Tri(Vector3.up * y1, t1, t0);
            }
            return Finish(name, verts, tris);
        }

        static Mesh Finish(string name, List<Vector3> verts, List<int> tris)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh star;

        /// <summary>Пятиконечная звезда в плоскости XY, радиус 0.5, лицевая сторона смотрит в +Z.</summary>
        public static Mesh Star
        {
            get
            {
                if (star != null) return star;
                var verts = new List<Vector3> { Vector3.zero };
                for (int i = 0; i < 10; i++)
                {
                    float a = Mathf.PI / 2f + i * Mathf.PI / 5f;
                    float r = i % 2 == 0 ? 0.5f : 0.2f;
                    verts.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
                }
                var tris = new List<int>();
                for (int i = 0; i < 10; i++) { tris.Add(0); tris.Add(1 + i); tris.Add(1 + (i + 1) % 10); }
                star = new Mesh { name = "Star" };
                star.SetVertices(verts);
                star.SetTriangles(tris, 0);
                star.RecalculateNormals();
                return star;
            }
        }

        static Mesh disc;

        /// <summary>Плоский круг радиусом 0.5 в плоскости XZ, лицом вверх (кольца выделения, воронки).</summary>
        public static Mesh Disc
        {
            get
            {
                if (disc != null) return disc;
                disc = Ring(0f, 0.5f, 28);
                return disc;
            }
        }

        static readonly Dictionary<int, Mesh> rings = new Dictionary<int, Mesh>();

        /// <summary>Плоское кольцо (внутренний и внешний радиус) в плоскости XZ, лицом вверх.</summary>
        public static Mesh Ring(float inner, float outer, int segments = 40)
        {
            int key = Mathf.RoundToInt(inner * 1000f) * 100000 + Mathf.RoundToInt(outer * 1000f) * 10 + segments % 10;
            if (rings.TryGetValue(key, out var cached) && cached != null) return cached;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts.Add(d * inner);
                verts.Add(d * outer);
                if (i < segments)
                {
                    int b = i * 2;
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                }
            }
            var mesh = new Mesh { name = "Ring" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            rings[key] = mesh;
            return mesh;
        }
    }

    public static class Fonts
    {
        static Font font;

        public static Font Default
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        /// <summary>Надпись в 3D-мире (всегда поверх — чтобы читалась).</summary>
        public static TextMesh WorldText(Transform parent, Vector3 localPos, string text, Color color, float size = 0.05f)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            tm.font = Default;
            tm.fontSize = 64;
            tm.characterSize = size;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            tm.text = text;
            go.GetComponent<MeshRenderer>().sharedMaterial = Default.material;
            return tm;
        }
    }
}
