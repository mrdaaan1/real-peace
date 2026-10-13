// Земля: пять детальных слоёв (трава, грунтовая дорога, жнивьё, речная грязь, пашня) по карте весов.
// Слои смешиваются по высоте (камешки проступают сквозь траву, а не просвечивают полупрозрачно),
// крутые склоны — в грунт. Повтор текстур маскируется вторым, крупным масштабом и шумом.
Shader "RealPeace/Terrain"
{
    Properties
    {
        _ControlTex ("Веса слоёв (R дорога, G жнивьё, B грязь, A пашня)", 2D) = "black" {}
        _GrassTex ("Трава", 2D) = "gray" {}
        _GrassN ("Трава — нормаль", 2D) = "bump" {}
        _DirtTex ("Грунт", 2D) = "gray" {}
        _DirtN ("Грунт — нормаль", 2D) = "bump" {}
        _WheatTex ("Жнивьё", 2D) = "gray" {}
        _WheatN ("Жнивьё — нормаль", 2D) = "bump" {}
        _MudTex ("Грязь", 2D) = "gray" {}
        _MudN ("Грязь — нормаль", 2D) = "bump" {}
        _PloughTex ("Пашня", 2D) = "gray" {}
        _PloughN ("Пашня — нормаль", 2D) = "bump" {}
        _Tiling ("Повторов на метр", Float) = 0.25
        _MapSize ("Размер карты, м", Float) = 400
        _NormalStrength ("Сила нормали", Range(0, 2)) = 1
        _GrassTint ("Оттенок травы", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "RealPeaceCommon.hlsl"

        TEXTURE2D(_ControlTex); SAMPLER(sampler_ControlTex);
        TEXTURE2D(_GrassTex); SAMPLER(sampler_GrassTex);
        TEXTURE2D(_GrassN);
        TEXTURE2D(_DirtTex); TEXTURE2D(_DirtN);
        TEXTURE2D(_WheatTex); TEXTURE2D(_WheatN);
        TEXTURE2D(_MudTex); TEXTURE2D(_MudN);
        TEXTURE2D(_PloughTex); TEXTURE2D(_PloughN);

        CBUFFER_START(UnityPerMaterial)
            float4 _ControlTex_ST;
            float _Tiling;
            float _MapSize;
            half _NormalStrength;
            half4 _GrassTint;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            float fogFactor : TEXCOORD3;
        };

        Varyings Vert(Attributes v)
        {
            Varyings o = (Varyings)0;
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.uv = v.uv;
            o.fogFactor = ComputeFogFactor(o.positionCS.z);
            return o;
        }

        // Слой на двух масштабах: мелкая деталь вблизи + крупная (против «плитки» вдали)
        half4 Layer(TEXTURE2D_PARAM(tex, samp), float2 uv, float2 uvFar, half farK)
        {
            half4 a = SAMPLE_TEXTURE2D(tex, samp, uv);
            half4 b = SAMPLE_TEXTURE2D(tex, samp, uvFar);
            half4 near = lerp(a, (a + b) * 0.5, 0.35);
            return lerp(near, b, farK);
        }

        half3 LayerN(TEXTURE2D_PARAM(tex, samp), float2 uv, float2 uvFar, half farK)
        {
            half3 a = UnpackNormal(SAMPLE_TEXTURE2D(tex, samp, uv));
            half3 b = UnpackNormal(SAMPLE_TEXTURE2D(tex, samp, uvFar));
            return normalize(lerp(a, b, farK * 0.7));
        }

        void TerrainSurface(Varyings i, out half3 albedo, out half3 normalWS, out half smoothness, out half occlusion)
        {
            float2 wp = i.positionWS.xz;
            // Лёгкий шум в UV ломает прямые края повторов
            float2 warp = float2(RP_ValueNoise(wp * 0.05), RP_ValueNoise(wp * 0.05 + 7.3)) * 2.0;
            float2 uv = (wp + warp) * _Tiling;
            float2 uvFar = (wp + warp * 3.0) * _Tiling * 0.21;
            float dist = distance(i.positionWS, GetCameraPositionWS());
            half farK = saturate((dist - 30.0) / 90.0);

            half4 ctrl = SAMPLE_TEXTURE2D(_ControlTex, sampler_ControlTex, i.uv);
            float3 n = normalize(i.normalWS);
            // Крутые склоны и берега — грунт
            half slope = saturate((1.0 - n.y - 0.12) * 6.0);
            ctrl.r = max(ctrl.r, slope);

            half4 g = Layer(TEXTURE2D_ARGS(_GrassTex, sampler_GrassTex), uv, uvFar, farK);
            g.rgb *= _GrassTint.rgb;
            half4 d = Layer(TEXTURE2D_ARGS(_DirtTex, sampler_GrassTex), uv, uvFar, farK);
            half4 w = Layer(TEXTURE2D_ARGS(_WheatTex, sampler_GrassTex), uv * 1.3, uvFar, farK);
            half4 m = Layer(TEXTURE2D_ARGS(_MudTex, sampler_GrassTex), uv, uvFar, farK);
            half4 p = Layer(TEXTURE2D_ARGS(_PloughTex, sampler_GrassTex), uv, uvFar, farK);

            // Смешивание по высоте: у кого «высота + вес» больше, тот и сверху
            half grassW = saturate(1.0 - ctrl.r - ctrl.g - ctrl.b - ctrl.a);
            float4 hw = float4(d.a + ctrl.r * 1.6, w.a + ctrl.g * 1.6, m.a + ctrl.b * 1.6, p.a + ctrl.a * 1.6) - 1.0 + float4(ctrl.r, ctrl.g, ctrl.b, ctrl.a) * 0.6;
            float hg = g.a + grassW * 1.6 - 1.0 + grassW * 0.6;
            float top = max(max(max(hw.x, hw.y), max(hw.z, hw.w)), hg) - 0.18;
            float4 bw = max(hw - top, 0.0) * step(0.001, float4(ctrl.r, ctrl.g, ctrl.b, ctrl.a));
            float bg = max(hg - top, 0.0) * step(0.001, grassW);
            float sum = bw.x + bw.y + bw.z + bw.w + bg + 1e-4;
            bw /= sum; bg /= sum;

            albedo = g.rgb * bg + d.rgb * bw.x + w.rgb * bw.y + m.rgb * bw.z + p.rgb * bw.w;

            // Крупные пятна: выгоревшая трава, сырые низины
            float macro = RP_Fbm(wp * 0.012);
            float macro2 = RP_Fbm(wp * 0.045 + 31.0);
            albedo *= lerp(0.85, 1.12, macro);
            albedo = lerp(albedo, albedo * half3(1.08, 1.02, 0.86), saturate(macro2 - 0.45) * 1.4 * bg);

            half3 tn = LayerN(TEXTURE2D_ARGS(_GrassN, sampler_GrassTex), uv, uvFar, farK) * bg
                     + LayerN(TEXTURE2D_ARGS(_DirtN, sampler_GrassTex), uv, uvFar, farK) * bw.x
                     + LayerN(TEXTURE2D_ARGS(_WheatN, sampler_GrassTex), uv * 1.3, uvFar, farK) * bw.y
                     + LayerN(TEXTURE2D_ARGS(_MudN, sampler_GrassTex), uv, uvFar, farK) * bw.z
                     + LayerN(TEXTURE2D_ARGS(_PloughN, sampler_GrassTex), uv, uvFar, farK) * bw.w;
            tn.xy *= _NormalStrength * (1.0 - farK * 0.5);
            // Касательные для почти горизонтальной земли: X и Z мира
            float3 t = normalize(float3(1, 0, 0) - n * n.x);
            float3 b = cross(n, t);
            normalWS = normalize(t * tn.x + b * -tn.y + n * max(tn.z, 0.2));

            // Грязь у реки блестит
            smoothness = lerp(0.08, 0.55, bw.z) * lerp(1.0, 0.6, farK);
            half h = g.a * bg + d.a * bw.x + w.a * bw.y + m.a * bw.z + p.a * bw.w;
            occlusion = lerp(0.65, 1.0, saturate(h * 1.4));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

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

            half4 Frag(Varyings i) : SV_Target
            {
                half3 albedo, normalWS; half smoothness, occlusion;
                TerrainSurface(i, albedo, normalWS, smoothness, occlusion);
                return RP_Shade(i.positionWS, i.positionCS, normalWS, albedo, 0, smoothness, occlusion, 0, 1, i.fogFactor);
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
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float4 ShadowVert(Attributes v) : SV_POSITION
            {
                return RP_ShadowPositionCS(TransformObjectToWorld(v.positionOS.xyz), TransformObjectToWorldNormal(v.normalOS));
            }
            half4 ShadowFrag() : SV_Target { return 0; }
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
            half4 DepthFrag(Varyings i) : SV_Target { return i.positionCS.z; }
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
            half4 NormalsFrag(Varyings i) : SV_Target
            {
                half3 albedo, normalWS; half smoothness, occlusion;
                TerrainSurface(i, albedo, normalWS, smoothness, occlusion);
                return half4(NormalizeNormalPerPixel(normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
