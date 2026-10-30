#ifndef KONOSUBA_TORNADO_FLOW
#define KONOSUBA_TORNADO_FLOW
// These constants and maps match FireTornadoFlow.cs (metres, seconds).
float FireTravel(float h) { return log(1 + max(0,h) * 0.30 / 0.9) / 0.30; }
float FireHeight(float travel) { return 3 * (exp(0.30 * max(0,travel)) - 1); }
float FireTurn(float travel, float lane)
{
    if (travel < 0)
    {
        float b = 0.72 * lane * lane;
        return -5.2 / 0.55 * (travel + log((0.55 + b * exp(-1.04 * travel)) / (0.55 + b)) / 1.04);
    }
    return ((-5.2 + 0.58 * 3) * travel - 0.58 * FireHeight(travel) / 0.30) / (0.55 + 0.72 * lane * lane);
}
float3 FireAxis(float h, float t)
{
    float fade = saturate(h * 1.3);
    return float3(sin(h * 1.55 - t * 1.05) * 0.085 * fade, h, cos(h * 1.35 - t * 0.9) * 0.065 * fade);
}
float FireRadialBend(float a, float h, float t)
{
    float turbulence = 0.085 * sin(a * 3 - h * 3.4 + t * 3.0)
        + 0.025 * sin(a * 7 + h * 4.7 - t * 5.3) + 0.012 * sin(a * 11 - h * 7 - t * 8);
    return 1 + turbulence * lerp(.22,1,smoothstep(0,.6,h));
}
float FireHash(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p,p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}
float FireNoise(float3 p)
{
    float3 cell = floor(p), f = frac(p);
    f = f*f*f*(f*(f*6-15)+10);
    return lerp(lerp(lerp(FireHash(cell),FireHash(cell+float3(1,0,0)),f.x),
                     lerp(FireHash(cell+float3(0,1,0)),FireHash(cell+float3(1,1,0)),f.x),f.y),
                lerp(lerp(FireHash(cell+float3(0,0,1)),FireHash(cell+float3(1,0,1)),f.x),
                     lerp(FireHash(cell+float3(0,1,1)),FireHash(cell+1),f.x),f.y),f.z);
}
float FireFbm(float3 p)
{
    return FireNoise(p)*0.58 + FireNoise(p*2.03+float3(17.1,4.7,9.2))*0.28
        + FireNoise(p*4.17+float3(2.3,31.1,7.4))*0.14;
}
// Inverse characteristics: constant label and birth time follow the rotating, rising flow.
// Embedding the angle on a circle avoids both the UV seam and a repeating image tile.
float3 FireDomain(float angle, float h, float lane, float time, float scale)
{
    float travel = FireTravel(h);
    float label = angle - FireTurn(travel,lane);
    return float3(cos(label)*scale,sin(label)*scale,(travel-time)*6.0);
}
#endif
