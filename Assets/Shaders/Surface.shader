// Основной материал мира и юнитов: PBR (URP) с трипланарной детальной текстурой и картой нормалей
// в метрах объекта — брёвна, штукатурка, камень, краска брони, ткань формы. Плюс износ краски
// (сколы с металлом на краях по шуму) и пыль/грязь снизу — чтобы техника выглядела побывавшей в бою.
Shader "RealPeace/Surface"
{
    Properties
    {
        _BaseColor ("Цвет", Color) = (1,1,1,1)
        _DetailTex ("Детальная текстура (RGB — оттенок, A — высота)", 2D) = "gray" {}
        _DetailNormal ("Детальная нормаль", 2D) = "bump" {}
        _DetailScale ("Повторов на метр", Float) = 1
        _DetailStrength ("Сила детали", Range(0, 2)) = 1
        _NormalStrength ("Сила нормали", Range(0, 3)) = 1
        _Smoothness ("Гладкость", Range(0, 1)) = 0.3
        _Metallic ("Металл", Range(0, 1)) = 0
        _Wear ("Износ краски", Range(0, 1)) = 0
        _WearColor ("Цвет под краской", Color) = (0.25, 0.24, 0.22, 1)
        _Dirt ("Грязь снизу", Range(0, 1)) = 0
        _DirtHeight ("Высота грязи, м", Float) = 1
        _DirtColor ("Цвет грязи", Color) = (0.36, 0.31, 0.24, 1)
        _Camo ("Камуфляж", Range(0, 1)) = 0
        _CamoColor ("Цвет пятен", Color) = (0.3, 0.33, 0.2, 1)
        _CamoScale ("Масштаб пятен", Float) = 0.35
        [HDR] _EmissionColor ("Свечение", Color) = (0,0,0,0)
        _Cull ("Отсечение", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "RealPeaceCommon.hlsl"

        TEXTURE2D(_DetailTex); SAMPLER(sampler_DetailTex);
        TEXTURE2D(_DetailNormal); SAMPLER(sampler_DetailNormal);

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float4 _DetailTex_ST;
            float4 _DetailNormal_ST;
            float _DetailScale;
            half _DetailStrength;
            half _NormalStrength;
            half _Smoothness;
            half _Metallic;
            half _Wear;
            half4 _WearColor;
            half _Dirt;
            float _DirtHeight;
            half4 _DirtColor;
            half _Camo;
            half4 _CamoColor;
            float _CamoScale;
            half4 _EmissionColor;
            float _Cull;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float3 positionOSs : TEXCOORD2; // координаты объекта в метрах
            float3 normalOS : TEXCOORD3;
            float fogFactor : TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes v)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_TRANSFER_INSTANCE_ID(v, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            float3 scale = RP_ObjectScale();
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.positionOSs = v.positionOS.xyz * scale;
            o.normalOS = normalize(v.normalOS / scale);
            o.fogFactor = ComputeFogFactor(o.positionCS.z);
            return o;
        }

        // Поверхность: цвет, нормаль (в мире), гладкость, металл
        void Surface(Varyings i, out half3 albedo, out half3 normalWS, out half smoothness, out half metallic, out half occlusion)
        {
            float3 nOS = normalize(i.normalOS);
            RP_Triplanar t = RP_MakeTriplanar(i.positionOSs, nOS, _DetailScale);
            half4 d = RP_SampleTriplanar(TEXTURE2D_ARGS(_DetailTex, sampler_DetailTex), t);
            // Деталь модулирует цвет вокруг серого 0.5: светлее/темнее, а не перекрашивает
            albedo = _BaseColor.rgb * lerp(half3(1, 1, 1), d.rgb * 2.0, _DetailStrength);
            smoothness = _Smoothness * lerp(1.0, d.a * 1.6, 0.5);
            metallic = _Metallic;
            occlusion = lerp(1.0, saturate(d.a * 1.5 + 0.25), 0.6);

            // Камуфляж: размытые полосы второй краской (распылитель — мягкий край)
            if (_Camo > 0.001)
            {
                float3 p = i.positionOSs * _CamoScale;
                float n = RP_Fbm(float2(p.x + p.y * 0.7, p.z * 0.6 - p.y * 0.4) + 11.0);
                float band = smoothstep(0.47, 0.55, n) * (1.0 - smoothstep(0.62, 0.7, n));
                albedo = lerp(albedo, _CamoColor.rgb * lerp(half3(1, 1, 1), d.rgb * 2.0, _DetailStrength), band * _Camo);
            }
            // Износ: пятна сколотой краски по шуму, на выступах (по высоте детали) — сильнее
            if (_Wear > 0.001)
            {
                float n = RP_Fbm(i.positionOSs.xz * 4.5 + i.positionOSs.y * 5.1);
                float chip = smoothstep(0.62, 0.7, n + (1.0 - d.a) * 0.15 + _Wear * 0.35 - 0.3);
                albedo = lerp(albedo, _WearColor.rgb, chip * _Wear);
                metallic = lerp(metallic, 0.7, chip * _Wear);
                smoothness = lerp(smoothness, 0.45, chip * _Wear);
                // Потёки и выцветание
                float streak = RP_ValueNoise(float2(i.positionOSs.x * 6.0, i.positionOSs.y * 0.6)) * 0.12;
                albedo *= 1.0 - streak * _Wear;
            }
            // Пыль и грязь снизу (катки, нижняя часть корпуса, сапоги)
            if (_Dirt > 0.001)
            {
                float h = i.positionOSs.y;
                float dirt = saturate((_DirtHeight - h) / max(_DirtHeight, 0.01));
                dirt *= saturate(RP_Fbm(i.positionOSs.xz * 3.1 + h * 4.0) * 1.4 + 0.2);
                albedo = lerp(albedo, _DirtColor.rgb, dirt * _Dirt);
                smoothness *= 1.0 - dirt * _Dirt * 0.8;
            }

            float3 nDetailOS = RP_TriplanarNormal(TEXTURE2D_ARGS(_DetailNormal, sampler_DetailNormal), t, nOS, _NormalStrength);
            float3 scale = RP_ObjectScale();
            normalWS = TransformObjectToWorldNormal(nDetailOS * scale);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 albedo, normalWS; half smoothness, metallic, occlusion;
                Surface(i, albedo, normalWS, smoothness, metallic, occlusion);
                return RP_Shade(i.positionWS, i.positionCS, normalWS, albedo, metallic, smoothness, occlusion,
                    _EmissionColor.rgb, 1.0, i.fogFactor);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float4 ShadowVert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                return RP_ShadowPositionCS(positionWS, normalWS);
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            half4 DepthFrag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment NormalsFrag
            #pragma multi_compile_instancing

            half4 NormalsFrag(Varyings i) : SV_Target
            {
                half3 albedo, normalWS; half smoothness, metallic, occlusion;
                Surface(i, albedo, normalWS, smoothness, metallic, occlusion);
                return half4(NormalizeNormalPerPixel(normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
