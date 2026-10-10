// Речная вода: преломление дна (буфер непрозрачной картинки), поглощение цвета с глубиной (буфер глубины),
// отражение неба по Френелю, блики солнца, течение (бегущие волны вдоль реки) и пена у берега.
Shader "RealPeace/Water"
{
    Properties
    {
        _WaveNormal ("Нормаль волн", 2D) = "bump" {}
        _ShallowColor ("Мелководье", Color) = (0.36, 0.42, 0.34, 1)
        _DeepColor ("Глубина", Color) = (0.07, 0.12, 0.11, 1)
        _Density ("Мутность", Float) = 0.9
        _Distortion ("Преломление", Float) = 0.03
        _WaveScale ("Масштаб волн", Float) = 0.12
        _FlowSpeed ("Скорость течения", Float) = 0.35
        _Smoothness ("Гладкость", Range(0, 1)) = 0.93
        _FoamColor ("Пена", Color) = (0.82, 0.82, 0.76, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-50" }

        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            Blend One Zero
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "RealPeaceCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_WaveNormal); SAMPLER(sampler_WaveNormal);

            CBUFFER_START(UnityPerMaterial)
                float4 _WaveNormal_ST;
                half4 _ShallowColor;
                half4 _DeepColor;
                float _Density;
                float _Distortion;
                float _WaveScale;
                float _FlowSpeed;
                half _Smoothness;
                half4 _FoamColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                // Лёгкая рябь геометрии
                o.positionWS.y += sin(_Time.y * 1.3 + o.positionWS.z * 0.4 + o.positionWS.x * 0.3) * 0.02;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 wp = i.positionWS.xz;
                float t = _Time.y * _FlowSpeed;
                // Две бегущие сетки волн вдоль течения (река течёт на юг, к −Z) + мелкая рябь
                half3 n1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_WaveNormal, sampler_WaveNormal, wp * _WaveScale + float2(0.02, t)), 0.8);
                half3 n2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_WaveNormal, sampler_WaveNormal, wp * _WaveScale * 2.3 + float2(-t * 0.3, t * 1.4)), 0.5);
                half3 nt = normalize(half3(n1.xy + n2.xy, n1.z * n2.z));
                half3 n = normalize(half3(nt.x, nt.z, nt.y));

                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEye = LinearEyeDepth(rawDepth, _ZBufferParams);
                float waterEye = i.screenPos.w;
                float depth = max(sceneEye - waterEye, 0.0);

                // Преломление: сдвигаем выборку дна по нормали, но не туда, где «дно» оказывается перед водой
                float2 offset = nt.xy * _Distortion * saturate(depth * 0.5);
                float2 refrUV = screenUV + offset;
                float refrDepth = LinearEyeDepth(SampleSceneDepth(refrUV), _ZBufferParams) - waterEye;
                if (refrDepth < 0.0) { refrUV = screenUV; refrDepth = depth; }
                half3 bottom = SampleSceneColor(refrUV);

                half absorb = 1.0 - exp(-max(refrDepth, 0.0) * _Density);
                half3 water = lerp(bottom * _ShallowColor.rgb * 1.6, _DeepColor.rgb, absorb);

                // Отражение неба и солнца
                half3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half fresnel = pow(1.0 - saturate(dot(n, V)), 5.0) * 0.95 + 0.04;
                half3 R = reflect(-V, n);
                half3 sky = GlossyEnvironmentReflection(R, 1.0 - _Smoothness, 1.0);
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light sun = GetMainLight(shadowCoord);
                half spec = pow(saturate(dot(R, sun.direction)), 400.0) * 6.0 * sun.shadowAttenuation;
                half3 color = lerp(water, sky, fresnel) + sun.color * spec;
                // Вода в тени темнее
                color *= lerp(0.75, 1.0, sun.shadowAttenuation);

                // Пена у берега и у свай
                float foamNoise = RP_ValueNoise(wp * 1.8 + float2(0, t * 2.0));
                half foam = saturate(1.0 - depth / 0.45) * step(0.45, foamNoise + saturate(1.0 - depth / 0.25) * 0.6);
                color = lerp(color, _FoamColor.rgb * (sun.color * 0.6 + 0.4), foam * 0.8);

                color = MixFog(color, InitializeInputDataFog(float4(i.positionWS, 1.0), i.fogFactor));
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
