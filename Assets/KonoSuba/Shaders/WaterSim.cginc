// Leitura da simulação de ondas (WaterSim.cs) a partir dos shaders da água.
#ifndef KONOSUBA_WATER_SIM_INCLUDED
#define KONOSUBA_WATER_SIM_INCLUDED

sampler2D _WaterSimTex;
float4 _WaterSimArea; // centro x, centro z, tamanho (m), tamanho de um texel (m)
float _WaterSimDepth;  // profundidade da película de água (m)

float2 WaterSimUV(float2 xz)
{
    return (xz - _WaterSimArea.xy) / max(_WaterSimArea.z, 1e-3) + 0.5;
}

bool InsideWaterSim(float2 xz, float inset)
{
    if (_WaterSimArea.z <= 0.0) return false;
    float2 d = abs(xz - _WaterSimArea.xy);
    return max(d.x, d.y) < _WaterSimArea.z * 0.5 - inset;
}

// Altura da água (m). Vai a zero perto das bordas da área, para coser com o chão parado à volta.
float WaterSimHeight(float2 xz)
{
    if (_WaterSimArea.z <= 0.0) return 0.0;
    float2 uv = WaterSimUV(xz);
    if (any(uv < 0.0) || any(uv > 1.0)) return 0.0;
    float2 e = min(uv, 1.0 - uv);
    float fade = saturate(min(e.x, e.y) / 0.08);
    return tex2Dlod(_WaterSimTex, float4(uv, 0, 0)).r * fade;
}

// Declive (dh/dx, dh/dz) da superfície simulada.
float2 WaterSimGradient(float2 xz)
{
    float e = max(_WaterSimArea.w, 1e-3);
    return float2(WaterSimHeight(xz + float2(e, 0)) - WaterSimHeight(xz - float2(e, 0)),
                  WaterSimHeight(xz + float2(0, e)) - WaterSimHeight(xz - float2(0, e))) / (2.0 * e);
}

// 1 = coberto de água, 0 = chão a seco (a água foi toda varrida dali).
float WaterSimWetness(float2 xz)
{
    if (_WaterSimDepth <= 0.0) return 1.0;
    return saturate((WaterSimHeight(xz) + _WaterSimDepth) / (_WaterSimDepth * 0.9));
}

// Curvatura (laplaciano) da superfície: negativa no topo das cristas.
float WaterSimCurvature(float2 xz)
{
    float e = max(_WaterSimArea.w, 1e-3) * 1.5;
    float h = WaterSimHeight(xz);
    return (WaterSimHeight(xz + float2(e, 0)) + WaterSimHeight(xz - float2(e, 0))
          + WaterSimHeight(xz + float2(0, e)) + WaterSimHeight(xz - float2(0, e)) - 4.0 * h) / (e * e);
}

#endif
