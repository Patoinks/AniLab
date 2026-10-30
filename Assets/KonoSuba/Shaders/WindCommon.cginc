// Partilhado pelos shaders do tornado de vento (WindStreak, WindDust).
#ifndef KONOSUBA_WIND_COMMON
#define KONOSUBA_WIND_COMMON

#include "UnityCG.cginc"

float _WindGust; // 0..1, rajada global (WindTornado.Update)

// Raio da ampulheta à altura h (m); igual à tabela de WindTornado.Profile.
float WindRadius(float h)
{
    float r = 1.5;
    r = lerp(r, 0.95, smoothstep(0.0, 0.3, h));
    r = lerp(r, 0.48, smoothstep(0.3, 1.2, h));
    r = lerp(r, 0.35, smoothstep(1.2, 2.4, h));
    r = lerp(r, 0.62, smoothstep(2.4, 3.6, h));
    r = lerp(r, 1.3, smoothstep(3.6, 4.6, h));
    r = lerp(r, 2.1, smoothstep(4.6, 5.5, h));
    return r;
}

// Chicote da coluna (mundo): o topo precessa e oscila, a base fica presa ao chão.
float3 WindSway(float h, float t)
{
    float k = saturate(h / 5.5);
    k *= k;
    return float3(sin(t * 0.9 + h * 0.7) * 0.4 * k + sin(t * 2.3 + h * 1.9) * 0.05 * k,
                  0.0,
                  cos(t * 0.7 + h * 0.6) * 0.35 * k + cos(t * 2.9 + h * 1.4) * 0.04 * k);
}

// Rampa de cor do vento: 0 azul-profundo → ciano → menta → 1+ branco.
float3 WindRamp(float x)
{
    float3 deep = float3(0.02, 0.16, 0.34);
    float3 cyan = float3(0.05, 0.72, 0.9);
    float3 mint = float3(0.45, 1.0, 0.82);
    float3 hot = float3(0.92, 1.0, 1.0);
    float3 c = lerp(deep, cyan, saturate(x / 0.35));
    c = lerp(c, mint, saturate((x - 0.35) / 0.35));
    return lerp(c, hot, saturate((x - 0.7) / 0.4));
}

#endif
