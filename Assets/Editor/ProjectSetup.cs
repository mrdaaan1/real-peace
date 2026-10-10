using System.IO;
using System.Linq;
using RealPeace;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Настройка проекта кодом — чтобы после открытия оставалось нажать Play:
/// URP (каскадные тени 4096, мягкие тени, SSAO, HDR, MSAA ×4, Forward+, буферы глубины и картинки для воды),
/// линейное цветовое пространство, образцы материалов на каждый шейдер в Resources (иначе Unity выкинет шейдеры
/// из сборки), сцена Battle с объектом Game. Меню: Real Peace → …
/// Из командной строки: -executeMethod ProjectSetup.RunBatch (настроить и выйти),
/// -executeMethod ProjectSetup.Autotest (настроить и запустить бой-автотест, см. GameBootstrap).
/// </summary>
[InitializeOnLoad]
public static class ProjectSetup
{
    const string ScenePath = "Assets/Scenes/Battle.unity";
    const string SettingsDir = "Assets/Settings";
    const string AssetPath = SettingsDir + "/RealPeaceURP.asset";
    const string RendererPath = SettingsDir + "/RealPeaceURP_Renderer.asset";
    const string ResourcesDir = "Assets/Resources/RealPeaceGenerated";

    static ProjectSetup()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EnsureAll(false);
        };
    }

    [MenuItem("Real Peace/Настроить проект (URP, сцена, материалы)")]
    public static void SetupMenu() => EnsureAll(true);

    [MenuItem("Real Peace/Открыть сцену боя")]
    public static void OpenScene()
    {
        if (!File.Exists(ScenePath)) CreateScene();
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
    }

    public static void RunBatch()
    {
        EnsureAll(true);
        AssetDatabase.SaveAssets();
        Debug.Log("[Real Peace] Настройка завершена");
        EditorApplication.Exit(0);
    }

    public static void Autotest()
    {
        EnsureAll(true);
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(ScenePath);
        EditorApplication.EnterPlaymode();
    }

    static void EnsureAll(bool force)
    {
        if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.productName = "Real Peace";
        PlayerSettings.companyName = "Real Peace";
        EnsurePipeline(force);
        SaveTemplates();
        if (!File.Exists(ScenePath)) CreateScene();
        UsePlayScene();
    }

    // ---------- URP ----------

    static void EnsurePipeline(bool force)
    {
        Directory.CreateDirectory(SettingsDir);
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
        if (asset == null)
        {
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            AssetDatabase.CreateAsset(renderer, RendererPath);
            ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);

            var ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            ssao.name = "SSAO";
            AssetDatabase.AddObjectToAsset(ssao, renderer);
            renderer.rendererFeatures.Add(ssao);
            var so = new SerializedObject(renderer);
            var map = so.FindProperty("m_RendererFeatureMap");
            if (map != null)
            {
                map.arraySize = 1;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out _, out long localId);
                map.GetArrayElementAtIndex(0).longValue = localId;
            }
            var mode = so.FindProperty("m_RenderingMode");
            if (mode != null) mode.intValue = 2; // Forward+: много точечных источников (вспышки, пожары)
            so.ApplyModifiedPropertiesWithoutUndo();

            var ssaoSo = new SerializedObject(ssao);
            SetFloat(ssaoSo, "m_Settings.Intensity", 1.8f);
            SetFloat(ssaoSo, "m_Settings.DirectLightingStrength", 0.3f);
            ssaoSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);

            asset = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(asset, AssetPath);
            force = true;
        }

        if (force)
        {
            asset.supportsCameraDepthTexture = true;
            asset.supportsCameraOpaqueTexture = true;
            asset.supportsHDR = true;
            asset.msaaSampleCount = 4;
            asset.renderScale = 1f;
            asset.shadowDistance = 230f;
            asset.shadowCascadeCount = 4;
            asset.mainLightShadowmapResolution = 4096;
            SetBoolAsset(asset, "m_SoftShadowsSupported", true);
            var so = new SerializedObject(asset);
            SetInt(so, "m_SoftShadowQuality", 3);          // высокое качество мягких теней
            SetVector3(so, "m_Cascade4Split", new Vector3(0.06f, 0.16f, 0.4f));
            SetInt(so, "m_AdditionalLightsPerObjectLimit", 8);
            SetInt(so, "m_ColorGradingMode", 1);            // HDR-цветокоррекция
            SetInt(so, "m_ColorGradingLutSize", 32);
            SetBool(so, "m_SupportsTerrainHoles", false);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        if (GraphicsSettings.defaultRenderPipeline != asset) GraphicsSettings.defaultRenderPipeline = asset;
        int current = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            if (QualitySettings.renderPipeline != null && QualitySettings.renderPipeline != asset) QualitySettings.renderPipeline = asset;
        }
        QualitySettings.SetQualityLevel(current, false);
        AssetDatabase.SaveAssets();
    }

    static void SetBoolAsset(Object o, string path, bool v)
    {
        var so = new SerializedObject(o);
        SetBool(so, path, v);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetFloat(SerializedObject so, string path, float v) { var p = so.FindProperty(path); if (p != null) p.floatValue = v; }
    static void SetInt(SerializedObject so, string path, int v) { var p = so.FindProperty(path); if (p != null) p.intValue = v; }
    static void SetBool(SerializedObject so, string path, bool v) { var p = so.FindProperty(path); if (p != null) p.boolValue = v; }
    static void SetVector3(SerializedObject so, string path, Vector3 v) { var p = so.FindProperty(path); if (p != null) p.vector3Value = v; }

    // ---------- Образцы материалов ----------

    static void SaveTemplates()
    {
        Directory.CreateDirectory(ResourcesDir);
        foreach (var name in new[] { "Surface", "Foliage", "Glow", "Particle", "Terrain", "Water" })
            SaveTemplate(name, "RealPeace/" + name);
        SaveTemplate("Sky", "Skybox/Procedural");
    }

    static void SaveTemplate(string file, string shaderName)
    {
        string path = ResourcesDir + "/" + file + ".mat";
        var shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogWarning("[Real Peace] Шейдер не найден: " + shaderName);
            return;
        }
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            if (existing.shader != shader) { existing.shader = shader; EditorUtility.SetDirty(existing); }
            return;
        }
        AssetDatabase.CreateAsset(new Material(shader) { name = file }, path);
    }

    // ---------- Сцена ----------

    static void CreateScene()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var game = new GameObject("Game");
        game.AddComponent<GameSettings>();
        game.AddComponent<GameBootstrap>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == ScenePath))
        {
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
        Debug.Log("[Real Peace] Сцена создана: " + ScenePath + ". Нажмите Play ▶");
    }

    /// <summary>Play всегда запускает игру со сцены Battle, даже если открыта пустая.</summary>
    static void UsePlayScene()
    {
        var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        if (scene != null) EditorSceneManager.playModeStartScene = scene;
    }
}
