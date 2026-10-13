using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RealPeace
{
    /// <summary>
    /// Атмосфера и «киношная» картинка: низкое тёплое солнце с мягкими тенями, процедурное небо и дымка,
    /// отражения неба (realtime reflection probe), ветер для травы и листвы, и постобработка URP:
    /// тонмаппинг ACES, bloom (вспышки, трассеры, огонь), цветокоррекция под фронтовую хронику —
    /// чуть приглушённые цвета, тёплые света и холодные тени, виньетка и лёгкое зерно плёнки.
    /// </summary>
    public static class Atmosphere
    {
        public static Light Sun { get; private set; }

        public static void Setup(Transform world)
        {
            // Солнце: вторая половина дня, свет косой — рельеф и тени читаются
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(world, false);
            sunGo.transform.rotation = Quaternion.Euler(MapLayout.Map.SunEuler);
            Sun = sunGo.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.color = MapLayout.Map.SunColor;
            Sun.intensity = MapLayout.Map.SunIntensity;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = 0.92f;
            Sun.shadowBias = 0.04f;
            Sun.shadowNormalBias = 0.35f;
            RenderSettings.sun = Sun;

            // Небо
            var skyShader = Shader.Find("Skybox/Procedural");
            var skyTemplate = Resources.Load<Material>("RealPeaceGenerated/Sky");
            var sky = skyTemplate != null ? new Material(skyTemplate) : (skyShader != null ? new Material(skyShader) : null);
            if (sky != null)
            {
                sky.SetFloat("_SunSize", 0.035f);
                sky.SetFloat("_SunSizeConvergence", 6f);
                sky.SetFloat("_AtmosphereThickness", 1.05f);
                sky.SetColor("_SkyTint", Shapes.Hex("#8c96a3"));
                sky.SetColor("_GroundColor", Shapes.Hex("#5f5b50"));
                sky.SetFloat("_Exposure", 1.15f);
                RenderSettings.skybox = sky;
            }

            // Рассеянный свет: небо голубоватое, от земли — тёплый отражённый
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Shapes.Hex("#93a3b8") * 1.7f;
            RenderSettings.ambientEquatorColor = Shapes.Hex("#99998a") * 1.5f;
            RenderSettings.ambientGroundColor = Shapes.Hex("#5e5644") * 1.4f;
            RenderSettings.ambientIntensity = 1f;

            // Дымка у горизонта
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = MapLayout.Map.FogColor;
            RenderSettings.fogDensity = MapLayout.Map.FogDensity;

            // Отражения неба — реальное время, один раз при загрузке
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            var probeGo = new GameObject("SkyReflection");
            probeGo.transform.SetParent(world, false);
            probeGo.transform.position = new Vector3(0f, 30f, 0f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.size = new Vector3(MapLayout.Size * 1.5f, 400f, MapLayout.Size * 1.5f);
            probe.cullingMask = 0; // только небо
            probe.resolution = 128;
            probe.hdr = true;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.RenderProbe();
            DynamicGI.UpdateEnvironment();

            // Ветер
            Shader.SetGlobalFloat("_RP_WindStrength", 1f);
            Shader.SetGlobalVector("_RP_WindDirection", new Vector4(0.8f, 0f, 0.6f, 0f));

            BuildPostProcessing(world);
        }

        static void BuildPostProcessing(Transform world)
        {
            var go = new GameObject("PostProcessing");
            go.transform.SetParent(world, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "RealPeaceLook";

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.7f);
            bloom.tint.Override(Shapes.Hex("#ffe2c0"));

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.35f);
            color.contrast.Override(14f);
            color.saturation.Override(-14f);
            color.colorFilter.Override(Shapes.Hex("#fff6e8"));

            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(6f);
            wb.tint.Override(2f);

            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.93f, 0.97f, 1.06f, -0.02f));
            smh.highlights.Override(new Vector4(1.04f, 1.0f, 0.94f, 0.0f));

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.24f);
            vignette.smoothness.Override(0.45f);

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.18f);
            grain.response.Override(0.8f);

            volume.sharedProfile = profile;
        }

        /// <summary>Настройка камеры под URP: постобработка, сглаживание, буферы глубины для воды и частиц.</summary>
        public static void SetupCamera(Camera cam)
        {
            cam.allowHDR = true;
            cam.allowMSAA = true;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.requiresDepthTexture = true;
            data.requiresColorTexture = true;
            data.renderShadows = true;
        }
    }
}
