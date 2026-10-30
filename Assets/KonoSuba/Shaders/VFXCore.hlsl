// Biblioteca partilhada dos VFX do AnimLab (URP): ruído, flipbook com motion vectors, flowmap, erosão,
// gradient map, soft particles e fresnel. Usada pelo shader-mestre AnimLab/VFX.
#ifndef ANIMLAB_VFX_CORE
#define ANIMLAB_VFX_CORE

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

// ---------------------------------------------------------------- Ruído
float VFXHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

float VFXNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(VFXHash(i), VFXHash(i + float2(1, 0)), f.x), lerp(VFXHash(i + float2(0, 1)), VFXHash(i + float2(1, 1)), f.x), f.y);
}

float VFXFbm(float2 p)
{
    return VFXNoise(p) * 0.5 + VFXNoise(p * 2.03 + 1.7) * 0.3 + VFXNoise(p * 4.1 + 3.1) * 0.2;
}

// ---------------------------------------------------------------- Flipbook
// Índice do frame (contínuo) → UVs dos dois frames vizinhos e o peso entre eles.
// grid = (colunas, linhas); frames contam da esquerda para a direita, de cima para baixo.
void VFXFlipbookUV(float2 uv, float2 grid, float frame, out float2 uvA, out float2 uvB, out float blend)
{
    float count = grid.x * grid.y;
    frame = fmod(max(frame, 0.0), count);
    float a = floor(frame), b = fmod(a + 1.0, count);
    blend = frame - a;
    float2 size = 1.0 / grid;
    float2 cellA = float2(fmod(a, grid.x), grid.y - 1.0 - floor(a / grid.x));
    float2 cellB = float2(fmod(b, grid.x), grid.y - 1.0 - floor(b / grid.x));
    uvA = (cellA + uv) * size;
    uvB = (cellB + uv) * size;
}

// ---------------------------------------------------------------- Flowmap (duas fases, sem saltos)
float3 VFXFlowUV(float2 uv, float2 flow, float time, float phaseOffset)
{
    float progress = frac(time + phaseOffset);
    return float3(uv - flow * progress, 1.0 - abs(1.0 - 2.0 * progress));
}

// ---------------------------------------------------------------- Erosão
// mask 0..1, erosion 0..1 (0 = inteiro, 1 = desaparecido). Devolve (alpha, borda).
float2 VFXErode(float mask, float erosion, float softness, float edgeWidth)
{
    float e = erosion * (1.0 + softness + edgeWidth);
    float alpha = smoothstep(e - softness, e, mask);
    // Sem largura de borda não há borda (evita somar a cor da borda a toda a zona suave).
    float edge = edgeWidth > 1e-4 ? alpha * (1.0 - smoothstep(e, e + edgeWidth, mask)) : 0.0;
    return float2(alpha, edge);
}

// ---------------------------------------------------------------- Profundidade e fresnel
// Desvanece junto à geometria da cena (soft particles). eyeDepth = profundidade do fragmento.
float VFXSoftFade(float4 positionCS, float eyeDepth, float distance)
{
    float2 uv = GetNormalizedScreenSpaceUV(positionCS);
    float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
    return saturate((scene - eyeDepth) / max(distance, 1e-3));
}

float VFXFresnel(float3 normalWS, float3 viewWS, float power)
{
    return pow(1.0 - saturate(abs(dot(normalize(normalWS), viewWS))), power);
}

#endif
