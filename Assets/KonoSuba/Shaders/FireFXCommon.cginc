// Partilhado pelos materiais desenhados pelo caminho do fogo (FireFX): como o buffer do fogo não tem
// profundidade da cena, cada fragmento compara-se com a depth texture da câmara e some atrás dos objetos.
#ifndef KONOSUBA_FIRE_FX_COMMON
#define KONOSUBA_FIRE_FX_COMMON

#include "UnityCG.cginc"
UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

// screenPos = ComputeScreenPos(clipPos); eyeDepth = -UnityObjectToViewPos(v).z (no vertex shader).
// Devolve 0 escondido, 1 visível, com uma transição suave de <softness> metros junto às superfícies.
float FireSceneVisibility(float4 screenPos, float eyeDepth, float softness)
{
    float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(screenPos)));
    return saturate((scene - eyeDepth) / max(softness, 1e-3));
}

// Hide imperfect border pixels of the painted texture without introducing a visible
// vertical seam on the shell. The offset sample itself has zero weight at its own seam.
float FirePaintFlow(sampler2D map, float2 uv)
{
    float2 p = frac(uv);
    float2 w = smoothstep(0.0, 0.13, p) * (1 - smoothstep(0.87, 1.0, p));
    float a = tex2D(map, p).r;
    float b = tex2D(map, frac(p + float2(0.5, 0))).r;
    float c = tex2D(map, frac(p + float2(0, 0.5))).r;
    float d = tex2D(map, frac(p + 0.5)).r;
    return lerp(lerp(d, c, w.x), lerp(b, a, w.x), w.y);
}

// Rampa de calor do fogo (paleta medida na referência, sRGB; o projeto está em Gamma): 0 brasa escura → 1 branco-amarelo.
float3 FirePaletteRamp(float h)
{
    h = saturate(h);
    float3 c = lerp(float3(0.294, 0.063, 0.0), float3(0.725, 0.169, 0.035), saturate(h / 0.22));
    c = lerp(c, float3(1.0, 0.357, 0.102), saturate((h - 0.22) / 0.23));
    c = lerp(c, float3(1.0, 0.722, 0.137), saturate((h - 0.45) / 0.27));
    c = lerp(c, float3(1.0, 0.871, 0.275), saturate((h - 0.72) / 0.16));
    c = lerp(c, float3(1.0, 0.965, 0.655), saturate((h - 0.88) / 0.12));
    return c;
}

#endif
