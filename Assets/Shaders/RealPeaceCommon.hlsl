#ifndef REALPEACE_COMMON_INCLUDED
#define REALPEACE_COMMON_INCLUDED

// Общая библиотека шейдеров Real Peace: шум, трипланарные текстуры, ветер и обвязка освещения URP
// (тени с каскадами, SSAO, отражения окружения, туман) — чтобы каждый шейдер не повторял одно и то же.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// ---------- Шум ----------

float RP_Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float RP_ValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = RP_Hash21(i);
    float b = RP_Hash21(i + float2(1, 0));
    float c = RP_Hash21(i + float2(0, 1));
    float d = RP_Hash21(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float RP_Fbm(float2 p)
{
    float s = 0.0, a = 0.5;
    for (int i = 0; i < 4; i++)
    {
        s += a * RP_ValueNoise(p);
        p = p * 2.03 + 17.1;
        a *= 0.5;
    }
    return s;
}

// ---------- Трипланарные текстуры ----------
// Примитивы (кубы, цилиндры) растянуты масштабом, их UV плывут. Поэтому текстуру кладём по трём проекциям
// в координатах объекта, умноженных на его масштаб: на любой грани метр — это метр, и текстура не «едет»,
// когда юнит двигается.

struct RP_Triplanar
{
    float3 weights;
    float2 uvX, uvY, uvZ;
};

RP_Triplanar RP_MakeTriplanar(float3 position, float3 normal, float scale)
{
    RP_Triplanar t;
    float3 w = pow(abs(normal), 4.0);
    t.weights = w / max(w.x + w.y + w.z, 1e-4);
    t.uvX = position.zy * scale;
    t.uvY = position.xz * scale;
    t.uvZ = position.xy * scale;
    return t;
}

half4 RP_SampleTriplanar(TEXTURE2D_PARAM(tex, samp), RP_Triplanar t)
{
    return SAMPLE_TEXTURE2D(tex, samp, t.uvX) * t.weights.x
         + SAMPLE_TEXTURE2D(tex, samp, t.uvY) * t.weights.y
         + SAMPLE_TEXTURE2D(tex, samp, t.uvZ) * t.weights.z;
}

// Нормаль из трипланарной карты нормалей (смешивание «whiteout»), normal — нормаль поверхности в том же пространстве
float3 RP_TriplanarNormal(TEXTURE2D_PARAM(tex, samp), RP_Triplanar t, float3 normal, float strength)
{
    float3 nX = UnpackNormalScale(SAMPLE_TEXTURE2D(tex, samp, t.uvX), strength);
    float3 nY = UnpackNormalScale(SAMPLE_TEXTURE2D(tex, samp, t.uvY), strength);
    float3 nZ = UnpackNormalScale(SAMPLE_TEXTURE2D(tex, samp, t.uvZ), strength);
    nX = float3(nX.xy + normal.zy, abs(nX.z) * normal.x);
    nY = float3(nY.xy + normal.xz, abs(nY.z) * normal.y);
    nZ = float3(nZ.xy + normal.xy, abs(nZ.z) * normal.z);
    return normalize(nX.zyx * t.weights.x + nY.xzy * t.weights.y + nZ.xyz * t.weights.z);
}

// Масштаб объекта (длины столбцов матрицы) — для трипланара «в метрах»
float3 RP_ObjectScale()
{
    return float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), length(UNITY_MATRIX_M._m02_m12_m22));
}

// ---------- Ветер ----------
float _RP_WindStrength;   // задаётся из C# (Shader.SetGlobalFloat)
float4 _RP_WindDirection; // xz — направление

float3 RP_Wind(float3 positionWS, float bendAmount)
{
    float t = _Time.y;
    float2 dir = normalize(_RP_WindDirection.xz + 1e-4);
    float gust = RP_ValueNoise(positionWS.xz * 0.03 - dir * t * 0.35) * 0.8 + 0.2;
    float wave = sin(t * 1.9 + dot(positionWS.xz, float2(0.35, 0.21))) * 0.5 + 0.5;
    float flutter = sin(t * 7.0 + positionWS.x * 2.3 + positionWS.z * 1.7) * 0.15;
    float amount = (gust * (0.6 + 0.4 * wave) + flutter) * bendAmount * max(_RP_WindStrength, 0.0);
    return float3(dir.x, 0, dir.y) * amount;
}

// ---------- Освещение ----------

// Полное PBR-освещение URP: солнце с каскадными тенями, доп. источники (вспышки, пожары), SSAO, отражения неба, туман.
half4 RP_Shade(float3 positionWS, float4 positionCS, half3 normalWS, half3 albedo, half metallic, half smoothness,
               half occlusion, half3 emission, half alpha, float fogFactor)
{
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.positionCS = positionCS;
    inputData.normalWS = NormalizeNormalPerPixel(normalWS);
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
#endif
    inputData.fogCoord = InitializeInputDataFog(float4(positionWS, 1.0), fogFactor);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    inputData.bakedGI = SampleSH(inputData.normalWS);
    inputData.shadowMask = half4(1, 1, 1, 1);

    SurfaceData s = (SurfaceData)0;
    s.albedo = albedo;
    s.metallic = metallic;
    s.specular = half3(0, 0, 0);
    s.smoothness = smoothness;
    s.occlusion = occlusion;
    s.emission = emission;
    s.alpha = alpha;
    s.normalTS = half3(0, 0, 1);

    half4 color = UniversalFragmentPBR(inputData, s);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    return color;
}

// Позиция в тени с поправкой (bias) для прохода ShadowCaster
float3 _LightDirection;
float3 _LightPosition;

float4 RP_ShadowPositionCS(float3 positionWS, float3 normalWS)
{
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
    return ApplyShadowClamping(positionCS);
}

#endif
