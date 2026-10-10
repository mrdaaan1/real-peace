// Частицы: дым, пыль, огонь, вспышки. Дым освещается солнцем как объём (сферическая нормаль по UV карточки),
// мягко «растворяется» у земли (мягкие частицы по буферу глубины). Огонь и вспышки — аддитивные,
// с HDR-яркостью, чтобы ловиться bloom'ом.
Shader "RealPeace/Particle"
{
    Properties
    {
        _BaseMap ("Текстура (A — плотность)", 2D) = "white" {}
        _BaseColor ("Цвет", Color) = (1,1,1,1)
        _Emission ("Яркость свечения", Float) = 0
        _Lit ("Освещение солнцем", Range(0, 1)) = 1
        _SoftDistance ("Мягкость у земли, м", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 10
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Particle"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "RealPeaceCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Emission;
                half _Lit;
                float _SoftDistance;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half4 color : COLOR;
                float4 screenPos : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color * _BaseColor;
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half alpha = tex.a * i.color.a;

                // Мягкие частицы: тают, пересекаясь с землёй и стенами
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                alpha *= saturate((sceneEye - i.screenPos.w) / max(_SoftDistance, 0.01));

                half3 color = tex.rgb * i.color.rgb;
                if (_Lit > 0.001)
                {
                    // Объём: нормаль «шара» из UV карточки, повёрнутая к камере
                    float2 c = i.uv * 2.0 - 1.0;
                    half3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                    half3 right = normalize(cross(half3(0, 1, 0), V));
                    half3 up = cross(V, right);
                    half3 n = normalize(right * c.x + up * c.y + V * sqrt(saturate(1.0 - dot(c, c)) + 0.15));
                    Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                    half ndl = saturate(dot(n, sun.direction) * 0.5 + 0.5);
                    half back = pow(saturate(dot(-V, sun.direction)), 6.0) * 0.6;
                    half3 lit = sun.color * (ndl * lerp(0.55, 1.0, sun.shadowAttenuation) + back) + SampleSH(n) * 0.9;
                    color *= lerp(half3(1, 1, 1), lit, _Lit);
                }
                color *= 1.0 + _Emission;
                color = MixFog(color, InitializeInputDataFog(float4(i.positionWS, 1.0), i.fogFactor));

                // Аддитивный огонь — смешивание SrcAlpha + One (задаётся из C#), здесь ничего особого
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
