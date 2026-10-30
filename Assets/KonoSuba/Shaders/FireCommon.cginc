// Partilhado pelos shaders de fogo (FireOrb, FireCorona, FireRing, FireHitGlow, FireVolume):
// ruído de valor 2D/3D, fbm e a rampa de cor do fogo.
#ifndef KONOSUBA_FIRE_COMMON
#define KONOSUBA_FIRE_COMMON

float FireHash3(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float FireNoise3(float3 x)
{
    float3 i = floor(x), f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(FireHash3(i + float3(0, 0, 0)), FireHash3(i + float3(1, 0, 0)), f.x),
                     lerp(FireHash3(i + float3(0, 1, 0)), FireHash3(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(FireHash3(i + float3(0, 0, 1)), FireHash3(i + float3(1, 0, 1)), f.x),
                     lerp(FireHash3(i + float3(0, 1, 1)), FireHash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

float FireFbm3(float3 p)
{
    float s = 0.0, a = 0.5;
    [unroll] for (int k = 0; k < 4; k++) { s += a * FireNoise3(p); p = p * 2.03 + 17.1; a *= 0.5; }
    return s;
}

float FireNoise2(float2 x) { return FireNoise3(float3(x, 0.37)); }

float FireFbm2(float2 p)
{
    float s = 0.0, a = 0.5;
    [unroll] for (int k = 0; k < 4; k++) { s += a * FireNoise2(p); p = p * 2.07 + 11.3; a *= 0.5; }
    return s;
}

// Rampa do fogo (0 = brasa escura, 1 = núcleo quase branco). Saturação puxada para cima e brilho contido nas
// bandas laranja, porque o grading da cena tira saturação e o bloom lava para branco o que passa muito de 1.
float3 FireRamp(float h)
{
    h = saturate(h);
    float3 c = lerp(float3(0.10, 0.006, 0.0), float3(0.65, 0.06, 0.0), smoothstep(0.0, 0.25, h));
    c = lerp(c, float3(1.0, 0.30, 0.01), smoothstep(0.22, 0.50, h));
    c = lerp(c, float3(1.0, 0.58, 0.07), smoothstep(0.48, 0.76, h));
    c = lerp(c, float3(1.0, 0.86, 0.45), smoothstep(0.78, 1.0, h));
    float l = dot(c, float3(0.299, 0.587, 0.114));
    c = l + (c - l) * 1.3;
    return max(c, 0.0) * (0.3 + 1.25 * h * h); // só o núcleo passa de 1: o laranja sobrevive ao bloom
}

// Nomes usados pelo anel e pelo brilho no alvo.
float FireNoise(float3 p) { return FireFbm3(p); }
float3 FireColor(float h) { return FireRamp(h); }

#endif
