using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// Процедурные бесшовные текстуры для шейдеров: детали материалов (брёвна, доски, штукатурка, камень, броня,
    /// ткань, солома, кора, траки), слои земли (трава, грунт, жнивьё, грязь, пашня), карточки листвы и травы,
    /// частицы (дым, огонь, вспышка) и нормаль волн. Каждая карта высот превращается в карту нормалей.
    /// Генерируется один раз за запуск, параллельно по строкам.
    /// </summary>
    public static class TextureFactory
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        // ---------- Бесшовный шум ----------

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519u);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>Шум значений, периодичный с периодом period клеток (бесшовный).</summary>
        static float Noise(float x, float y, int period, int seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy);
            int x0 = ((ix % period) + period) % period, y0 = ((iy % period) + period) % period;
            int x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
            float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
        }

        /// <summary>Фрактальный бесшовный шум; u, v — 0…1, basePeriod — клеток на текстуру в первой октаве.</summary>
        static float Fbm(float u, float v, int basePeriod, int octaves, int seed, float gain = 0.5f)
        {
            float s = 0f, a = 0.5f, norm = 0f;
            int p = basePeriod;
            for (int i = 0; i < octaves; i++)
            {
                s += a * Noise(u * p, v * p, p, seed + i * 31);
                norm += a;
                a *= gain;
                p *= 2;
            }
            return s / norm;
        }

        /// <summary>Клеточный шум (Ворони) — бесшовный. Возвращает расстояние до ближайшей точки и до второй.</summary>
        static Vector2 Cells(float u, float v, int period, int seed, out float cellId)
        {
            float x = u * period, y = v * period;
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float d1 = 9f, d2 = 9f;
            cellId = 0f;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int cx = ix + i, cy = iy + j;
                    int wx = ((cx % period) + period) % period, wy = ((cy % period) + period) % period;
                    float px = cx + Hash(wx, wy, seed), py = cy + Hash(wx, wy, seed + 7);
                    float d = (px - x) * (px - x) + (py - y) * (py - y);
                    if (d < d1) { d2 = d1; d1 = d; cellId = Hash(wx, wy, seed + 13); }
                    else if (d < d2) d2 = d;
                }
            return new Vector2(Mathf.Sqrt(d1), Mathf.Sqrt(d2));
        }

        // ---------- Сборка текстур ----------

        delegate Color PixelFn(float u, float v);

        static Texture2D Make(string name, int size, PixelFn fn, bool linear = false, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            if (cache.TryGetValue(name, out var cached) && cached != null) return cached;
            var px = new Color32[size * size];
            Parallel.For(0, size, j =>
            {
                float v = (j + 0.5f) / size;
                for (int i = 0; i < size; i++)
                    px[j * size + i] = fn((i + 0.5f) / size, v);
            });
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, linear) { name = name, wrapMode = wrap, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            tex.SetPixels32(px);
            tex.Apply(true, false);
            cache[name] = tex;
            return tex;
        }

        /// <summary>Карта нормалей из альфа-канала (высоты) текстуры: разностная схема с заворачиванием краёв.</summary>
        public static Texture2D NormalFromHeight(Texture2D src, float strength)
        {
            string name = src.name + "_N";
            if (cache.TryGetValue(name, out var cached) && cached != null) return cached;
            int size = src.width;
            var c = src.GetPixels32();
            var h = new float[size * size];
            for (int i = 0; i < h.Length; i++) h[i] = c[i].a / 255f;
            var px = new Color32[size * size];
            Parallel.For(0, size, j =>
            {
                for (int i = 0; i < size; i++)
                {
                    float l = h[j * size + (i - 1 + size) % size], r = h[j * size + (i + 1) % size];
                    float d = h[((j - 1 + size) % size) * size + i], u = h[((j + 1) % size) * size + i];
                    var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                    px[j * size + i] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255), (byte)((n.y * 0.5f + 0.5f) * 255), (byte)((n.z * 0.5f + 0.5f) * 255), 255);
                }
            });
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = name, wrapMode = src.wrapMode, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            tex.SetPixels32(px);
            tex.Apply(true, false);
            cache[name] = tex;
            return tex;
        }

        static Color Gray(float g, float height) => new Color(g, g, g, height);
        static Color Tint(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        // ---------- Детали материалов (RGB около 0.5 — нейтрально, A — высота) ----------

        /// <summary>Брёвна сруба: горизонтальные валики, тёмные пазы, волокна вдоль, сучки.</summary>
        public static Texture2D Logs => Make("Logs", 512, (u, v) =>
        {
            float logs = v * 4f;
            float k = logs - Mathf.Floor(logs);
            int row = Mathf.FloorToInt(logs);
            float round = Mathf.Sin(k * Mathf.PI);
            float grain = Noise(u * 8f, v * 256f, 8, 11 + row) * 0.6f + Noise(u * 64f, v * 512f, 64, 3) * 0.4f;
            float knot = Mathf.Clamp01(1f - Cells(u + row * 0.21f, k * 0.25f, 4, 17 + row, out _).x * 6f);
            float g = 0.38f + round * 0.18f + (grain - 0.5f) * 0.25f - knot * 0.2f;
            if (k < 0.06f || k > 0.94f) g *= 0.45f; // паз между брёвнами
            float weather = Fbm(u, v, 3, 3, 5);
            return new Color(g * (1.02f + weather * 0.1f), g, g * 0.92f, round * 0.8f + grain * 0.2f);
        });

        /// <summary>Доски: вертикальные, со щелями, волокнами и разным оттенком каждой.</summary>
        public static Texture2D Planks => Make("Planks", 512, (u, v) =>
        {
            float b = u * 5f;
            int board = Mathf.FloorToInt(b);
            float k = b - board;
            float shade = 0.85f + Hash(board, 0, 21) * 0.3f;
            float grain = Noise(u * 512f, v * 16f + board * 3, 16, 23) * 0.5f + Fbm(u * 5f, v, 5, 4, 27 + board) * 0.5f;
            float g = 0.5f * shade + (grain - 0.5f) * 0.3f;
            float h = 0.7f + grain * 0.3f;
            if (k < 0.04f || k > 0.96f) { g *= 0.35f; h = 0.1f; }
            float nail = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(k, v * 4f % 1f), new Vector2(0.5f, 0.5f)) * 30f);
            return new Color(g, g * 0.97f, g * 0.92f, h - nail * 0.4f);
        });

        /// <summary>Побелённая штукатурка: пятна сырости, трещины, мелкая шероховатость.</summary>
        public static Texture2D Plaster => Make("Plaster", 512, (u, v) =>
        {
            float stain = Fbm(u, v, 3, 5, 31);
            float grain = Noise(u * 256f, v * 256f, 256, 33);
            var cells = Cells(u, v, 6, 35, out _);
            float crack = Mathf.Clamp01(1f - (cells.y - cells.x) * 25f) * Mathf.Clamp01((Fbm(u, v, 5, 3, 37) - 0.45f) * 5f);
            float drip = Mathf.Clamp01(Fbm(u * 6f, v, 6, 3, 39) - 0.55f) * (1f - v) * 1.5f;
            float g = 0.55f - (stain - 0.5f) * 0.25f + (grain - 0.5f) * 0.06f - crack * 0.35f - drip * 0.2f;
            return new Color(g, g * 0.99f, g * 0.95f, 0.6f + grain * 0.2f - crack * 0.5f);
        });

        /// <summary>Бутовый камень с раствором (стены, фундаменты).</summary>
        public static Texture2D Stone => Make("Stone", 512, (u, v) =>
        {
            var c = Cells(u, v, 7, 41, out float id);
            float edge = c.y - c.x;
            float mortar = Mathf.Clamp01(1f - edge * 9f);
            float rough = Fbm(u, v, 12, 4, 43);
            float g = 0.42f + id * 0.18f + (rough - 0.5f) * 0.2f;
            g = Mathf.Lerp(g, 0.55f, mortar * 0.8f);
            float h = Mathf.Lerp(0.5f + Mathf.Clamp01(edge * 3f) * 0.4f + rough * 0.1f, 0.05f, mortar);
            return new Color(g, g * 0.98f, g * 0.93f, h);
        });

        /// <summary>Бетон: поры, следы опалубки, пятна.</summary>
        public static Texture2D Concrete => Make("Concrete", 512, (u, v) =>
        {
            float n = Fbm(u, v, 4, 5, 51);
            float pores = Noise(u * 128f, v * 128f, 128, 53) > 0.86f ? 0.6f : 1f;
            float form = Mathf.Abs(Mathf.Sin(v * Mathf.PI * 3f)) < 0.04f ? 0.85f : 1f;
            float g = (0.48f + (n - 0.5f) * 0.25f) * pores * form;
            return new Color(g, g, g * 0.97f, n * pores);
        });

        /// <summary>Броня: литая шероховатость, вмятины, царапины — под краску.</summary>
        public static Texture2D Armor => Make("Armor", 512, (u, v) =>
        {
            float cast = Fbm(u, v, 16, 4, 61);
            float blotch = Fbm(u, v, 3, 4, 63);
            float scratch = 0f;
            for (int i = 0; i < 3; i++)
            {
                float line = Mathf.Abs(Mathf.Sin((u * (7f + i * 3f) + v * (2f + i)) * Mathf.PI * 2f + i));
                scratch += line < 0.012f && Noise(u * 10f, v * 10f, 10, 65 + i) > 0.6f ? 0.15f : 0f;
            }
            var dents = Cells(u, v, 5, 67, out _);
            float dent = Mathf.Clamp01(0.2f - dents.x) * 2f;
            float g = 0.5f + (blotch - 0.5f) * 0.18f + (cast - 0.5f) * 0.08f + scratch;
            return new Color(g, g, g, 0.55f + (cast - 0.5f) * 0.3f - dent);
        });

        /// <summary>Ткань формы: саржевое плетение, складки, потёртости.</summary>
        public static Texture2D Fabric => Make("Fabric", 256, (u, v) =>
        {
            float weave = (Mathf.Sin((u + v) * Mathf.PI * 160f) * 0.5f + 0.5f) * 0.5f + (Mathf.Sin((u - v) * Mathf.PI * 160f) * 0.5f + 0.5f) * 0.2f;
            float folds = Fbm(u, v * 0.5f, 3, 4, 71);
            float wear = Fbm(u, v, 8, 3, 73);
            float g = 0.5f + (weave - 0.35f) * 0.12f + (folds - 0.5f) * 0.3f + (wear - 0.5f) * 0.1f;
            return new Color(g, g, g, folds * 0.7f + weave * 0.3f);
        });

        /// <summary>Мешковина (мешки с песком, палатки).</summary>
        public static Texture2D Burlap => Make("Burlap", 256, (u, v) =>
        {
            float a = Mathf.Sin(u * Mathf.PI * 96f) * 0.5f + 0.5f, b = Mathf.Sin(v * Mathf.PI * 96f) * 0.5f + 0.5f;
            float weave = Mathf.Max(a * (1f - b), b * (1f - a));
            float dirt = Fbm(u, v, 4, 4, 81);
            float g = 0.45f + weave * 0.15f + (dirt - 0.5f) * 0.3f;
            return new Color(g, g * 0.97f, g * 0.9f, weave * 0.6f + dirt * 0.4f);
        });

        /// <summary>Соломенная крыша: пучки соломы сверху вниз, тёмные слежавшиеся слои.</summary>
        public static Texture2D Thatch => Make("Thatch", 512, (u, v) =>
        {
            float straw = Noise(u * 384f, v * 12f, 12, 91) * 0.6f + Noise(u * 192f, v * 6f + 3f, 6, 92) * 0.4f;
            float layers = Mathf.Sin(v * Mathf.PI * 12f + Fbm(u, v, 6, 3, 93) * 3f) * 0.5f + 0.5f;
            float moss = Mathf.Clamp01(Fbm(u, v, 4, 4, 95) - 0.55f) * 2f;
            float g = 0.38f + straw * 0.3f - (1f - layers) * 0.15f;
            return new Color(g * (1f - moss * 0.3f), g * (1f + moss * 0.05f), g * (0.85f - moss * 0.2f), straw * 0.6f + layers * 0.4f);
        });

        /// <summary>Тёс/дранка: ряды деревянных плашек со смещением.</summary>
        public static Texture2D Shingles => Make("Shingles", 512, (u, v) =>
        {
            float rows = v * 10f;
            int row = Mathf.FloorToInt(rows);
            float k = rows - row;
            float x = u * 8f + (row % 2) * 0.5f;
            int col = Mathf.FloorToInt(x);
            float kx = x - col;
            float shade = 0.8f + Hash(col, row, 101) * 0.4f;
            float grain = Noise(u * 256f, v * 16f, 16, 103);
            float g = 0.45f * shade + (grain - 0.5f) * 0.15f - (1f - k) * 0.12f;
            float h = k * 0.8f + grain * 0.2f;
            if (kx < 0.04f || kx > 0.96f || k > 0.95f) { g *= 0.4f; h = 0f; }
            return new Color(g, g * 0.98f, g * 0.95f, h);
        });

        /// <summary>Кора: вертикальные трещины.</summary>
        public static Texture2D Bark => Make("Bark", 256, (u, v) =>
        {
            float ridges = Mathf.Abs(Fbm(u * 4f, v, 12, 4, 111) - 0.5f) * 2f;
            float g = 0.3f + ridges * 0.4f;
            return new Color(g, g * 0.95f, g * 0.88f, ridges);
        });

        /// <summary>Гусеница: траки поперёк, грунтозацепы, грязь в щелях.</summary>
        public static Texture2D Track => Make("Track", 256, (u, v) =>
        {
            float links = v * 12f;
            float k = links - Mathf.Floor(links);
            float grouser = k > 0.2f && k < 0.45f ? 1f : 0.5f;
            float gap = k > 0.9f ? 0.2f : 1f;
            float rough = Fbm(u, v, 8, 3, 121);
            float g = (0.35f + grouser * 0.2f + (rough - 0.5f) * 0.2f) * gap;
            return new Color(g, g * 0.97f, g * 0.93f, grouser * gap);
        });

        /// <summary>Мелкая шероховатость (кожа, резина, металл без рисунка).</summary>
        public static Texture2D Fine => Make("Fine", 256, (u, v) =>
        {
            float n = Fbm(u, v, 16, 4, 131);
            return Gray(0.5f + (n - 0.5f) * 0.15f, n);
        });

        /// <summary>Земля в воронках и на брустверах: комья, камешки.</summary>
        public static Texture2D Soil => Make("Soil", 512, (u, v) =>
        {
            float clods = Fbm(u, v, 8, 5, 141);
            var c = Cells(u, v, 24, 143, out float id);
            float pebble = id > 0.8f ? Mathf.Clamp01(1f - c.x * 5f) : 0f;
            float g = 0.45f + (clods - 0.5f) * 0.35f + pebble * 0.25f;
            return new Color(g, g * 0.96f, g * 0.9f, clods * 0.7f + pebble * 0.3f);
        });

        // ---------- Слои земли (настоящие цвета, A — высота) ----------

        /// <summary>Трава: тысячи коротких штрихов-травинок разных оттенков поверх тёмной почвы.</summary>
        public static Texture2D GroundGrass => Make("GroundGrass", 512, (u, v) =>
        {
            var soil = new Color(0.17f, 0.15f, 0.09f);
            var g1 = new Color(0.24f, 0.31f, 0.11f);
            var g2 = new Color(0.33f, 0.38f, 0.15f);
            var dry = new Color(0.46f, 0.43f, 0.24f);
            float cover = Fbm(u, v, 6, 4, 201);
            // Травинки: вытянутый шум под разными углами
            float b1 = Noise(u * 256f + v * 32f, v * 64f, 32, 203);
            float b2 = Noise(u * 64f - v * 16f, v * 256f + u * 32f, 16, 205);
            float blades = Mathf.Max(b1, b2);
            float dryK = Mathf.Clamp01((Fbm(u, v, 4, 3, 207) - 0.5f) * 3f);
            var grass = Color.Lerp(Color.Lerp(g1, g2, blades), dry, dryK * 0.6f * blades);
            float soilK = Mathf.Clamp01((0.55f - blades - cover * 0.3f) * 3f);
            var col = Color.Lerp(grass, soil, soilK * 0.7f);
            col = Tint(col, 0.85f + Noise(u * 32f, v * 32f, 32, 209) * 0.3f);
            col.a = blades * 0.8f + cover * 0.2f;
            return col;
        });

        /// <summary>Грунтовая дорога: утрамбованная пыльная земля, редкие камешки, следы и мелкие комочки.</summary>
        public static Texture2D GroundDirt => Make("GroundDirt", 512, (u, v) =>
        {
            var dust = new Color(0.5f, 0.42f, 0.31f);
            var dark = new Color(0.33f, 0.27f, 0.2f);
            float big = Fbm(u, v, 3, 4, 211);
            float mid = Fbm(u, v, 12, 4, 212);
            float fine = Noise(u * 256f, v * 256f, 256, 213);
            var col = Color.Lerp(dark, dust, Mathf.Clamp01(big * 0.9f + mid * 0.4f - 0.15f));
            col = Tint(col, 0.9f + fine * 0.2f);
            float h = mid * 0.6f + fine * 0.15f;
            // Редкие камешки разного размера
            var c = Cells(u, v, 48, 214, out float id);
            if (id > 0.9f)
            {
                float r = 0.12f + (id - 0.9f) * 2.5f;
                float k = Mathf.Clamp01((r - c.x) * 8f);
                col = Color.Lerp(col, new Color(0.52f, 0.5f, 0.46f) * (0.75f + id * 0.3f), k);
                h = Mathf.Max(h, k * 0.9f);
            }
            col.a = h;
            return col;
        });

        /// <summary>Жнивьё: рядки срезанных стеблей по земле, солома.</summary>
        public static Texture2D GroundWheat => Make("GroundWheat", 512, (u, v) =>
        {
            var soil = new Color(0.36f, 0.3f, 0.2f);
            var straw = new Color(0.72f, 0.62f, 0.36f);
            float row = Mathf.Sin(u * Mathf.PI * 16f) * 0.5f + 0.5f;
            float stalks = Noise(u * 256f, v * 256f, 256, 221);
            float lying = Noise(u * 40f + v * 320f, v * 40f, 40, 223);
            float k = Mathf.Clamp01(row * 0.6f + stalks * 0.6f + lying * 0.3f - 0.4f);
            var col = Color.Lerp(soil, straw, k);
            col = Tint(col, 0.85f + Fbm(u, v, 4, 3, 225) * 0.3f);
            col.a = k;
            return col;
        });

        /// <summary>Речная грязь: тёмная, сырая, с лужицами и следами.</summary>
        public static Texture2D GroundMud => Make("GroundMud", 512, (u, v) =>
        {
            var mud = new Color(0.22f, 0.18f, 0.13f);
            float n = Fbm(u, v, 5, 5, 231);
            float puddle = Mathf.Clamp01((0.42f - n) * 6f);
            var col = Tint(mud, 0.8f + n * 0.5f);
            col = Color.Lerp(col, new Color(0.14f, 0.13f, 0.11f), puddle * 0.7f);
            col.a = n * (1f - puddle);
            return col;
        });

        /// <summary>Пашня: гребни борозд и комья.</summary>
        public static Texture2D GroundPlough => Make("GroundPlough", 512, (u, v) =>
        {
            var soil = new Color(0.3f, 0.23f, 0.16f);
            float furrow = Mathf.Sin(v * Mathf.PI * 20f + Fbm(u, v, 3, 2, 241) * 2f) * 0.5f + 0.5f;
            float clods = Fbm(u, v, 24, 4, 243);
            float h = furrow * 0.65f + clods * 0.35f;
            var col = Tint(soil, 0.65f + h * 0.55f);
            col.a = h;
            return col;
        });

        // ---------- Карточки растительности (альфа — вырез) ----------

        static Color32[] Canvas(int size) => new Color32[size * size];

        static void Blot(Color32[] px, int size, float cx, float cy, float rx, float ry, float angle, Color col, bool wrap = false)
        {
            float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
            float r = Mathf.Max(rx, ry);
            int x0 = Mathf.FloorToInt((cx - r) * size), x1 = Mathf.CeilToInt((cx + r) * size);
            int y0 = Mathf.FloorToInt((cy - r) * size), y1 = Mathf.CeilToInt((cy + r) * size);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int xi = x, yi = y;
                    if (wrap) { xi = (x % size + size) % size; yi = (y % size + size) % size; }
                    else if (x < 0 || y < 0 || x >= size || y >= size) continue;
                    float dx = (x + 0.5f) / size - cx, dy = (y + 0.5f) / size - cy;
                    float lx = (dx * ca + dy * sa) / rx, ly = (-dx * sa + dy * ca) / ry;
                    float d = lx * lx + ly * ly;
                    if (d > 1f) continue;
                    // Лист светлее к центру, жилка темнее
                    float shade = 1f - d * 0.25f - (Mathf.Abs(ly) < 0.08f ? 0.12f : 0f);
                    var c = col * shade;
                    c.a = 1f;
                    px[yi * size + xi] = c;
                }
        }

        static void Stroke(Color32[] px, int size, Vector2 a, Vector2 b, float w0, float w1, Color c0, Color c1)
        {
            float len = Vector2.Distance(a, b) * size;
            int steps = Mathf.Max(2, Mathf.CeilToInt(len * 1.5f));
            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / steps;
                var p = Vector2.Lerp(a, b, t);
                float w = Mathf.Lerp(w0, w1, t) * size;
                var c = Color.Lerp(c0, c1, t);
                c.a = 1f;
                int r = Mathf.CeilToInt(w);
                int px0 = Mathf.FloorToInt(p.x * size), py0 = Mathf.FloorToInt(p.y * size);
                for (int y = py0 - r; y <= py0 + r; y++)
                    for (int x = px0 - r; x <= px0 + r; x++)
                    {
                        if (x < 0 || y < 0 || x >= size || y >= size) continue;
                        float dx = x + 0.5f - p.x * size, dy = y + 0.5f - p.y * size;
                        if (dx * dx + dy * dy <= w * w) px[y * size + x] = c;
                    }
            }
        }

        const float FoliageCutoff = 0.45f;

        static Texture2D FromCanvas(string name, Color32[] px, int size)
        {
            // 1) Цвет под прозрачными пикселями — от ближайших листьев (иначе в мипмапах края чернеют)
            var filled = (Color32[])px.Clone();
            var has = new bool[px.Length];
            for (int i = 0; i < px.Length; i++) has[i] = px[i].a > 0;
            var next = new bool[px.Length];
            for (int pass = 0; pass < 64; pass++)
            {
                bool changed = false;
                System.Array.Copy(has, next, has.Length);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        int i = y * size + x;
                        if (has[i]) continue;
                        int r = 0, g = 0, b = 0, n = 0;
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                            if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
                            int j = ny * size + nx;
                            if (!has[j]) continue;
                            r += filled[j].r; g += filled[j].g; b += filled[j].b; n++;
                        }
                        if (n == 0) continue;
                        filled[i] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 0);
                        next[i] = true;
                        changed = true;
                    }
                System.Array.Copy(next, has, has.Length);
                if (!changed) break;
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            tex.SetPixels32(filled, 0);

            // 2) Мипмапы с сохранением покрытия: вдали крона остаётся такой же густой, а не «тает» в серую дымку
            float coverage0 = Coverage(filled, 1f);
            var prev = filled;
            int prevSize = size;
            for (int level = 1; level < tex.mipmapCount; level++)
            {
                int ms = Mathf.Max(1, prevSize / 2);
                var mip = new Color32[ms * ms];
                for (int y = 0; y < ms; y++)
                    for (int x = 0; x < ms; x++)
                    {
                        int r = 0, g = 0, b = 0, a = 0;
                        for (int dy = 0; dy < 2; dy++)
                            for (int dx = 0; dx < 2; dx++)
                            {
                                var c = prev[Mathf.Min(prevSize - 1, y * 2 + dy) * prevSize + Mathf.Min(prevSize - 1, x * 2 + dx)];
                                r += c.r; g += c.g; b += c.b; a += c.a;
                            }
                        mip[y * ms + x] = new Color32((byte)(r / 4), (byte)(g / 4), (byte)(b / 4), (byte)(a / 4));
                    }
                // Подбираем множитель альфы, чтобы доля непрозрачного совпала с исходной
                float lo = 0.5f, hi = 8f;
                for (int it = 0; it < 12; it++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (Coverage(mip, mid) < coverage0) lo = mid; else hi = mid;
                }
                float k = (lo + hi) * 0.5f;
                var scaled = new Color32[mip.Length];
                for (int i = 0; i < mip.Length; i++)
                {
                    var c = mip[i];
                    c.a = (byte)Mathf.Min(255, c.a * k);
                    scaled[i] = c;
                }
                tex.SetPixels32(scaled, level);
                prev = mip;
                prevSize = ms;
            }
            tex.Apply(false, false);
            cache[name] = tex;
            return tex;
        }

        static float Coverage(Color32[] px, float alphaScale)
        {
            int n = 0;
            float threshold = FoliageCutoff * 255f;
            foreach (var c in px) if (c.a * alphaScale > threshold) n++;
            return (float)n / px.Length;
        }

        /// <summary>Кисть листвы лиственного дерева: десятки листьев-эллипсов в круге, веточки.</summary>
        public static Texture2D LeafCluster(string name, Color baseColor, float leafSize, int seed)
        {
            if (cache.TryGetValue(name, out var cached) && cached != null) return cached;
            const int size = 512;
            var px = Canvas(size);
            var rnd = new System.Random(seed);
            // Веточки от центра низа
            for (int i = 0; i < 7; i++)
            {
                var a = new Vector2(0.5f, 0.05f);
                var b = new Vector2(0.15f + (float)rnd.NextDouble() * 0.7f, 0.3f + (float)rnd.NextDouble() * 0.6f);
                Stroke(px, size, a, b, 0.006f, 0.002f, new Color(0.25f, 0.2f, 0.14f), new Color(0.3f, 0.25f, 0.16f));
            }
            int count = (int)(260 / (leafSize * leafSize * 400f) + 120);
            for (int i = 0; i < count; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Sqrt((float)rnd.NextDouble()) * 0.42f;
                float cx = 0.5f + Mathf.Cos(a) * r, cy = 0.52f + Mathf.Sin(a) * r * 0.9f;
                float shade = 0.7f + (float)rnd.NextDouble() * 0.55f;
                var c = Tint(baseColor, shade);
                c.r += ((float)rnd.NextDouble() - 0.5f) * 0.06f;
                Blot(px, size, cx, cy, leafSize * (0.8f + (float)rnd.NextDouble() * 0.5f), leafSize * 0.45f, (float)rnd.NextDouble() * Mathf.PI, c);
            }
            return FromCanvas(name, px, size);
        }

        /// <summary>Еловая лапа: ствол-ветка с хвоинками, опущенная к краям.</summary>
        public static Texture2D PineBranch
        {
            get
            {
                const string name = "PineBranch";
                if (cache.TryGetValue(name, out var cached) && cached != null) return cached;
                const int size = 512;
                var px = Canvas(size);
                var rnd = new System.Random(301);
                for (int b = 0; b < 3; b++)
                {
                    float y0 = 0.35f + b * 0.15f;
                    var a = new Vector2(0.02f, y0);
                    var e = new Vector2(0.98f, y0 - 0.12f);
                    Stroke(px, size, a, e, 0.012f, 0.005f, new Color(0.24f, 0.18f, 0.12f), new Color(0.28f, 0.22f, 0.14f));
                    for (int i = 0; i < 260; i++)
                    {
                        float t = (float)rnd.NextDouble();
                        var p = Vector2.Lerp(a, e, t);
                        float ang = ((float)rnd.NextDouble() - 0.5f) * 2.4f + (rnd.NextDouble() < 0.5 ? Mathf.PI / 2f : -Mathf.PI / 2f);
                        float len = 0.04f + 0.06f * (1f - Mathf.Abs(t - 0.4f));
                        var q = p + new Vector2(Mathf.Cos(ang) * 0.3f, Mathf.Sin(ang)) * len;
                        var c = new Color(0.13f, 0.24f, 0.14f) * (0.75f + (float)rnd.NextDouble() * 0.5f);
                        Stroke(px, size, p, q, 0.006f, 0.0035f, c, c * 1.15f);
                    }
                }
                return FromCanvas(name, px, size);
            }
        }

        /// <summary>Пучок травы: травинки веером от низа, тёмные у корня, светлые к кончикам.</summary>
        public static Texture2D GrassClump(string name, Color root, Color tip, int blades, int seed, bool ears)
        {
            if (cache.TryGetValue(name, out var cached) && cached != null) return cached;
            const int size = 256;
            var px = Canvas(size);
            var rnd = new System.Random(seed);
            for (int i = 0; i < blades; i++)
            {
                float x = 0.2f + (float)rnd.NextDouble() * 0.6f;
                float h = 0.55f + (float)rnd.NextDouble() * 0.42f;
                float lean = ((float)rnd.NextDouble() - 0.5f) * 0.35f;
                var a = new Vector2(x, 0f);
                var mid = new Vector2(x + lean * 0.4f, h * 0.55f);
                var top = new Vector2(Mathf.Clamp(x + lean, 0.03f, 0.97f), h);
                float shade = 0.8f + (float)rnd.NextDouble() * 0.4f;
                Stroke(px, size, a, mid, 0.014f, 0.01f, Tint(root, shade), Tint(Color.Lerp(root, tip, 0.5f), shade));
                Stroke(px, size, mid, top, 0.01f, 0.002f, Tint(Color.Lerp(root, tip, 0.5f), shade), Tint(tip, shade));
                if (ears)
                    Blot(px, size, top.x, top.y - 0.04f, 0.022f, 0.06f, Mathf.PI / 2f + lean, Tint(tip * 1.1f, shade));
            }
            return FromCanvas(name, px, size);
        }

        // ---------- Частицы ----------

        /// <summary>Клуб дыма: мягкое облако с рваными краями (альфа — плотность).</summary>
        public static Texture2D SmokePuff => Make("SmokePuff", 128, (u, v) =>
        {
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float n = Fbm(u, v, 4, 5, 401);
            float a = Mathf.Clamp01((1f - d) * 1.6f - (1f - n) * 0.9f);
            a = a * a * (3f - 2f * a);
            return new Color(1f, 1f, 1f, a * 0.85f);
        }, false, TextureWrapMode.Clamp);

        /// <summary>Огонь: яркое ядро и языки по краям.</summary>
        public static Texture2D Fire => Make("Fire", 128, (u, v) =>
        {
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float n = Fbm(u, v, 5, 4, 411);
            float a = Mathf.Clamp01((1f - d) * 1.8f - (1f - n) * 0.8f);
            var c = Color.Lerp(new Color(1f, 0.35f, 0.08f), new Color(1f, 0.9f, 0.6f), Mathf.Clamp01(a * 1.4f));
            c.a = a;
            return c;
        }, false, TextureWrapMode.Clamp);

        /// <summary>Вспышка выстрела: звезда лучей.</summary>
        public static Texture2D Flash => Make("Flash", 128, (u, v) =>
        {
            var p = new Vector2(u - 0.5f, v - 0.5f) * 2f;
            float d = p.magnitude;
            float ang = Mathf.Atan2(p.y, p.x);
            float rays = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 3f)), 8f);
            float a = Mathf.Clamp01(1f - d * 1.6f) + Mathf.Clamp01(1f - d) * rays * 0.8f;
            return new Color(1f, 0.85f, 0.55f, Mathf.Clamp01(a));
        }, false, TextureWrapMode.Clamp);

        /// <summary>Нормаль волн для воды (бесшовная).</summary>
        public static Texture2D WaveNormal
        {
            get
            {
                var h = Make("WaveHeight", 256, (u, v) =>
                {
                    float w = Fbm(u, v, 4, 5, 501) * 0.6f + (Mathf.Sin((u * 3f + v * 5f) * Mathf.PI * 2f) * 0.5f + 0.5f) * 0.2f
                              + (Mathf.Sin((u * 7f - v * 2f) * Mathf.PI * 2f) * 0.5f + 0.5f) * 0.2f;
                    return Gray(0.5f, w);
                }, true);
                return NormalFromHeight(h, 6f);
            }
        }
    }
}
