// Листва, хвоя, трава и колосья: текстурные карточки с вырезом по альфе, двусторонние.
// Ветер качает вершины (вес — красный канал цвета вершины: у корня 0, на макушке 1),
// листья просвечивают против солнца, оттенок слегка гуляет по миру. Трава плавно уходит в землю вдали.
Shader "RealPeace/Foliage"
{
    Properties
    {
        _BaseMap ("Текстура (RGBA)", 2D) = "white" {}
        _BaseColor ("Цвет", Color) = (1,1,1,1)
        _Cutoff ("Порог альфы", Range(0, 1)) = 0.45
        _Smoothness ("Гладкость", Range(0, 1)) = 0.2
        _Translucency ("Просвечивание", Range(0, 2)) = 0.8
        _WindBend ("Ветер", Float) = 0.3
        _ColorVariation ("Разброс оттенка", Range(0, 1)) = 0.25
        _FadeStart ("Начало исчезновения, м (0 — нет)", Float) = 0
        _FadeEnd ("Конец исчезновения, м", Float) = 0
        _AOBottom ("Затенение у корня", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        Cull Off

        HLSLINCLUDE
        #include "RealPeaceCommon.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
            half _Smoothness;
            half _Translucency;
            float _WindBend;
            half _ColorVariation;
            float _FadeStart;
            float _FadeEnd;
            half _AOBottom;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            half2 heightAndFade : TEXCOORD3;
            float fogFactor : TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float3 FoliagePosition(Attributes v, out half fade)
        {
            float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
            positionWS += RP_Wind(positionWS, v.color.r * _WindBend);
            fade = 0;
            if (_FadeEnd > 0.0)
            {
                // Трава вдали «врастает» в землю: сжимаем к корню (начало координат экземпляра)
                float3 root = TransformObjectToWorld(float3(0, 0, 0));
                float d = distance(root, GetCameraPositionWS());
                fade = saturate((d - _FadeStart) / max(_FadeEnd - _FadeStart, 0.01));
                positionWS = lerp(positionWS, root, fade);
            }
            return positionWS;
        }

        Varyings Vert(Attributes v)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_TRANSFER_INSTANCE_ID(v, o);
            half fade;
            o.positionWS = FoliagePosition(v, fade);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
            o.heightAndFade = half2(v.color.r, fade);
            o.fogFactor = ComputeFogFactor(o.positionCS.z);
            return o;
        }

        half4 SampleAlbedo(Varyings i)
        {
            half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
            clip(c.a - _Cutoff);
            return c;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            AlphaToMask On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            half4 Frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 c = SampleAlbedo(i);
                // Нормали «шара» кроны не переворачиваем у изнанки карточки — иначе задние листья чернеют
                half3 n = normalize(i.normalWS);
                // Оттенок гуляет по миру: где-то сочнее, где-то суше
                float v = RP_ValueNoise(i.positionWS.xz * 0.07);
                half3 albedo = c.rgb * lerp(half3(1, 1, 1), lerp(half3(1.12, 1.06, 0.78), half3(0.92, 1.02, 0.86), v), _ColorVariation * 2.0);
                half ao = lerp(1.0 - _AOBottom, 1.0, saturate(i.heightAndFade.x * 1.4));
                half4 color = RP_Shade(i.positionWS, i.positionCS, n, albedo * ao, 0, _Smoothness, ao, 0, 1, i.fogFactor);

                // Просвечивание листьев против солнца (с учётом тени)
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light sun = GetMainLight(shadowCoord);
                half3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half back = pow(saturate(dot(V, -sun.direction)), 4.0) * _Translucency;
                half wrap = saturate(dot(-n, sun.direction) * 0.6 + 0.4) * _Translucency * 0.35;
                half3 trans = albedo * sun.color * (back + wrap) * sun.shadowAttenuation * ao;
                color.rgb += MixFog(trans, InitializeInputDataFog(float4(i.positionWS, 1.0), i.fogFactor)) * 0.6;
                // Резкая альфа для сглаживания по покрытию (alpha-to-coverage): край листа — в 1 пиксель,
                // а не полупрозрачная каша, из-за которой крона выглядела как туман
                half sharp = saturate((c.a - _Cutoff) / max(fwidth(c.a), 0.0001) + 0.5);
                return half4(color.rgb, sharp);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            Varyings ShadowVert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                half fade;
                float3 positionWS = FoliagePosition(v, fade);
                o.positionCS = RP_ShadowPositionCS(positionWS, TransformObjectToWorldNormal(v.normalOS));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 ShadowFrag(Varyings i) : SV_Target
            {
                SampleAlbedo(i);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            half4 DepthFrag(Varyings i) : SV_Target
            {
                SampleAlbedo(i);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment NormalsFrag
            #pragma multi_compile_instancing
            half4 NormalsFrag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                SampleAlbedo(i);
                return half4(NormalizeNormalPerPixel(normalize(i.normalWS)), 0.0);
            }
            ENDHLSL
        }
    }
}
