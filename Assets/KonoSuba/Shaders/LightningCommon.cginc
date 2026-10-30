// Partilhado pelo raio (LightningBolt) e pela água (CheckerWaterFloor): o brilho de um ponto de um ramo num instante.
// Estado por renderer nas fitas (MaterialPropertyBlock); os globais de LightningDischarge servem de fallback:
//   _LightningTime      segundos desde o lançamento
//   _LightningFade      atenuação final (1 → 0)
//   _LightningBranches  tabela 3 × 64 (RGBAFloat), uma linha por ramo:
//     texel 0: gate (0 = apagão), semente, escala da largura, manter até (0 = envelope normal)
//     texel 1/2: pulso (início, s de origem, velocidade com sinal, ganho) — reacendimento que corre pelo ramo
#ifndef KONOSUBA_LIGHTNING_COMMON
#define KONOSUBA_LIGHTNING_COMMON

sampler2D _LightningBranches;
float _LightningTime, _LightningFade;

// Envelope local: sobe em 5 ms, pico até 35 ms, desce a ~45% aos 150 ms, fica aceso até aos 500 ms e apaga-se
// até aos 650 ms. Como cada ponto conta desde que a corrente lá chegou, o raio apaga-se a começar pela cauda
// (o que acendeu primeiro) e o apagar corre atrás da ponta.
float LightningEnvelope(float age)
{
    if (age < 0.0) return 0.0;
    if (age < 0.005) return age / 0.005;
    if (age < 0.035) return 1.0;
    if (age < 0.15) return lerp(1.0, 0.45, (age - 0.035) / 0.115);
    if (age < 0.5) return 0.45;
    return 0.45 * saturate(1.0 - (age - 0.5) / 0.15);
}

// Pulso (reacendimento): curto, sem rasto.
float LightningPulseEnvelope(float age)
{
    if (age < 0.0) return 0.0;
    if (age < 0.005) return age / 0.005;
    if (age < 0.035) return 1.0;
    return saturate(1.0 - (age - 0.035) / 0.08);
}

float4 LightningBranchRow(float id, float k)
{
    return tex2Dlod(_LightningBranches, float4((k + 0.5) / 3.0, (id + 0.5) / 64.0, 0, 0));
}

float LightningPulse(float4 p, float s)
{
    if (p.w <= 0.0 || abs(p.z) < 1e-3) return 0.0;
    float dist = (s - p.y) * sign(p.z);
    if (dist < 0.0) return 0.0;
    return p.w * LightningPulseEnvelope(_LightningTime - p.x - dist / abs(p.z));
}

// Brilho (0..1) de um ponto: chegada da corrente ou um dos dois reacendimentos, vezes o gate do ramo.
float LightningSignal(float id, float arrival, float s)
{
    float4 b = LightningBranchRow(id, 0);
    float e = LightningEnvelope(_LightningTime - arrival);
    if (b.w > 0.0 && _LightningTime >= arrival)
        e = _LightningTime < b.w ? 0.85 : LightningEnvelope(_LightningTime - b.w);
    e = max(e, LightningPulse(LightningBranchRow(id, 1), s));
    e = max(e, LightningPulse(LightningBranchRow(id, 2), s));
    return b.x * e * _LightningFade;
}

// Variação de brilho ao longo do canal (pontos quentes de 8–20 cm), fixa no canal.
float LightningHotSpots(float s, float seed)
{
    float x = s / 0.14 + seed * 37.0;
    float i = floor(x), f = frac(x);
    float a = frac(sin(i * 127.1 + seed) * 43758.5453);
    float b = frac(sin((i + 1.0) * 127.1 + seed) * 43758.5453);
    float n = lerp(a, b, f * f * (3.0 - 2.0 * f));
    return lerp(0.25, 1.0, smoothstep(0.3, 0.85, n));
}

#endif
