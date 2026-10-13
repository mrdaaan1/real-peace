using System.Collections.Generic;
using UnityEngine;

namespace RealPeace
{
    /// <summary>Тип поверхности — какая детальная текстура, нормаль, гладкость, износ и грязь у материала.</summary>
    public enum Surf
    {
        Paint,      // крашеное дерево/металл без особого рисунка
        Logs, Planks, Plaster, Stone, Concrete, Thatch, Shingles,
        Armor,      // броня танка: износ краски, пыль снизу
        Metal,      // голый металл (ствол, траки)
        Track,
        Fabric,     // форма
        Burlap,     // мешки, палатки
        Skin,
        Leather,
        Bark,
        Soil,
        Wood,       // приклады, ящики
        Brick,      // кирпичная кладка
    }

    /// <summary>
    /// Библиотека материалов на шейдерах Real Peace. Материалы кэшируются по цвету и типу —
    /// одинаковые детали мира рисуются одним материалом (SRP Batcher).
    /// </summary>
    public static class Mats
    {
        static readonly Dictionary<(Color, Surf), Material> surf = new Dictionary<(Color, Surf), Material>();
        static readonly Dictionary<Color, Material> glow = new Dictionary<Color, Material>();
        static readonly Dictionary<string, Material> named = new Dictionary<string, Material>();

        static Shader Find(string name)
        {
            // В сборке шейдер гарантированно есть через материал-образец в Resources (его сохраняет редактор)
            var template = Resources.Load<Material>("RealPeaceGenerated/" + name.Replace("RealPeace/", ""));
            if (template != null) return template.shader;
            var s = Shader.Find(name);
            if (s == null) Debug.LogError("[Real Peace] Нет шейдера " + name);
            return s;
        }

        static Shader surfaceShader, foliageShader, glowShader, particleShader, terrainShader, waterShader;
        public static Shader SurfaceShader => surfaceShader != null ? surfaceShader : (surfaceShader = Find("RealPeace/Surface"));
        public static Shader FoliageShader => foliageShader != null ? foliageShader : (foliageShader = Find("RealPeace/Foliage"));
        public static Shader GlowShader => glowShader != null ? glowShader : (glowShader = Find("RealPeace/Glow"));
        public static Shader ParticleShader => particleShader != null ? particleShader : (particleShader = Find("RealPeace/Particle"));
        public static Shader TerrainShader => terrainShader != null ? terrainShader : (terrainShader = Find("RealPeace/Terrain"));
        public static Shader WaterShader => waterShader != null ? waterShader : (waterShader = Find("RealPeace/Water"));

        public static Material Get(Color color, Surf kind = Surf.Paint)
        {
            var key = (color, kind);
            if (surf.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(SurfaceShader) { name = kind + "_" + ColorUtility.ToHtmlStringRGB(color), enableInstancing = true };
            m.SetColor("_BaseColor", color);
            Texture2D detail = TextureFactory.Fine;
            float scale = 1f, strength = 1f, normal = 1f, smooth = 0.25f, metal = 0f, wear = 0f, dirt = 0f, dirtHeight = 1f;
            switch (kind)
            {
                case Surf.Paint: detail = TextureFactory.Fine; scale = 2f; strength = 0.6f; normal = 0.4f; smooth = 0.3f; break;
                case Surf.Logs: detail = TextureFactory.Logs; scale = 1f; strength = 0.85f; normal = 2.2f; smooth = 0.12f; break;
                case Surf.Planks: detail = TextureFactory.Planks; scale = 0.8f; strength = 1f; normal = 1.6f; smooth = 0.15f; break;
                case Surf.Plaster: detail = TextureFactory.Plaster; scale = 0.4f; strength = 1f; normal = 1.2f; smooth = 0.12f; break;
                case Surf.Stone: detail = TextureFactory.Stone; scale = 1.3f; strength = 1.1f; normal = 2.4f; smooth = 0.18f; break;
                case Surf.Concrete: detail = TextureFactory.Concrete; scale = 0.35f; strength = 1f; normal = 1.2f; smooth = 0.15f; break;
                case Surf.Thatch: detail = TextureFactory.Thatch; scale = 1.2f; strength = 1.1f; normal = 2.5f; smooth = 0.08f; break;
                case Surf.Shingles: detail = TextureFactory.Shingles; scale = 0.7f; strength = 1f; normal = 2f; smooth = 0.12f; break;
                case Surf.Armor: detail = TextureFactory.Armor; scale = 0.6f; strength = 0.9f; normal = 1.1f; smooth = 0.32f; metal = 0.15f; wear = 0.3f; dirt = 0.85f; dirtHeight = 1.3f; break;
                case Surf.Metal: detail = TextureFactory.Armor; scale = 1.5f; strength = 0.7f; normal = 0.8f; smooth = 0.5f; metal = 0.85f; wear = 0.2f; break;
                case Surf.Track: detail = TextureFactory.Track; scale = 1.2f; strength = 1.2f; normal = 2.5f; smooth = 0.35f; metal = 0.6f; dirt = 0.6f; dirtHeight = 0.9f; break;
                case Surf.Fabric: detail = TextureFactory.Fabric; scale = 3f; strength = 0.9f; normal = 1.2f; smooth = 0.08f; dirt = 0.5f; dirtHeight = 0.5f; break;
                case Surf.Burlap: detail = TextureFactory.Burlap; scale = 2f; strength = 1f; normal = 1.6f; smooth = 0.05f; break;
                case Surf.Skin: detail = TextureFactory.Fine; scale = 6f; strength = 0.3f; normal = 0.3f; smooth = 0.35f; break;
                case Surf.Leather: detail = TextureFactory.Fine; scale = 5f; strength = 0.6f; normal = 0.6f; smooth = 0.45f; break;
                case Surf.Bark: detail = TextureFactory.Bark; scale = 1.5f; strength = 1.1f; normal = 2.5f; smooth = 0.1f; break;
                case Surf.Soil: detail = TextureFactory.Soil; scale = 0.6f; strength = 1.1f; normal = 2f; smooth = 0.1f; break;
                case Surf.Wood: detail = TextureFactory.Planks; scale = 2.5f; strength = 0.8f; normal = 0.8f; smooth = 0.3f; break;
                case Surf.Brick: detail = TextureFactory.Brick; scale = 0.5f; strength = 1f; normal = 2f; smooth = 0.12f; break;
            }
            m.SetTexture("_DetailTex", detail);
            m.SetTexture("_DetailNormal", TextureFactory.NormalFromHeight(detail, NormalStrengthFor(kind)));
            m.SetFloat("_DetailScale", scale);
            m.SetFloat("_DetailStrength", strength);
            m.SetFloat("_NormalStrength", normal);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_Wear", wear);
            m.SetFloat("_Dirt", dirt);
            m.SetFloat("_DirtHeight", dirtHeight);
            surf[key] = m;
            return m;
        }

        /// <summary>Краска брони с камуфляжными полосами (немецкая техника: тёмно-жёлтый + оливковый).</summary>
        public static Material Camo(Color baseColor, Color camoColor)
        {
            string key = "Camo_" + ColorUtility.ToHtmlStringRGB(baseColor) + ColorUtility.ToHtmlStringRGB(camoColor);
            if (named.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Get(baseColor, Surf.Armor)) { name = key };
            m.SetFloat("_Camo", 1f);
            m.SetColor("_CamoColor", camoColor);
            named[key] = m;
            return m;
        }

        /// <summary>Тот же материал почти без грязи снизу (башня, пушка — у них своё начало координат).</summary>
        public static Material Clean(Material source)
        {
            string key = "Clean_" + source.name;
            if (named.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(source) { name = key };
            m.SetFloat("_Dirt", 0.15f);
            named[key] = m;
            return m;
        }

        static float NormalStrengthFor(Surf kind) => kind == Surf.Logs || kind == Surf.Brick || kind == Surf.Stone || kind == Surf.Thatch || kind == Surf.Bark ? 6f : 3.5f;

        /// <summary>Светящийся материал (трассеры, кольца, маркеры). intensity > 1 — светится через bloom.</summary>
        public static Material Glow(Color color, float intensity = 1f, bool onTop = false)
        {
            var key = new Color(color.r * intensity, color.g * intensity, color.b * intensity, onTop ? -color.a : color.a);
            if (glow.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(GlowShader) { name = "Glow", enableInstancing = true };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_ZTest", onTop ? (float)UnityEngine.Rendering.CompareFunction.Always : (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            glow[key] = m;
            return m;
        }

        /// <summary>Листва/трава на карточках.</summary>
        public static Material Foliage(string name, Texture2D tex, Color tint, float wind, float translucency, float fadeStart = 0f, float fadeEnd = 0f)
        {
            if (named.TryGetValue(name, out var m) && m != null) return m;
            m = new Material(FoliageShader) { name = name, enableInstancing = true };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_WindBend", wind);
            m.SetFloat("_Translucency", translucency);
            m.SetFloat("_FadeStart", fadeStart);
            m.SetFloat("_FadeEnd", fadeEnd);
            named[name] = m;
            return m;
        }

        /// <summary>Материал частиц: additive — огонь и вспышки, иначе дым и пыль.</summary>
        public static Material Particle(string name, Texture2D tex, Color tint, bool additive, float emission, float lit)
        {
            if (named.TryGetValue(name, out var m) && m != null) return m;
            m = new Material(ParticleShader) { name = name, enableInstancing = true };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Emission", emission);
            m.SetFloat("_Lit", lit);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.renderQueue = 3000;
            named[name] = m;
            return m;
        }
    }
}
