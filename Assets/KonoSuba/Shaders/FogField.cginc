// Campo de interferência do nevoeiro, partilhado pelo nevoeiro de ecrã (GroundFog) e pelos cards (FogParticle).
// Escrito em HLSL simples (sem macros da Unity) para poder ser incluído em CG e no StdLib do Post Processing.
// Os dados vêm de FogField.cs:
//   _FogBlasts[i]      = (x, z, tempoInicio, raio)
//   _FogBlastParams[i] = (força, duração, tempoExpansão, -)
//   _FogWinds[i]       = (x, z, dir.x * força, dir.z * força)
//   _FogWindParams[i]  = (comprimento, largura, tempoInicio, duração)
//                        largura < 0: rasto reto de projétil (largura constante, sem ondular, abre quase de imediato)
#ifndef KONOSUBA_FOG_FIELD_INCLUDED
#define KONOSUBA_FOG_FIELD_INCLUDED

#define FOG_MAX_BLASTS 8
#define FOG_MAX_WINDS 4

float4 _FogBlasts[FOG_MAX_BLASTS];
float4 _FogBlastParams[FOG_MAX_BLASTS];
float4 _FogWinds[FOG_MAX_WINDS];
float4 _FogWindParams[FOG_MAX_WINDS];
float _FogTime;

// Ruído suave 1D (0..1), para as bordas dos rastos não serem retas.
float FogNoise(float x)
{
    float i = floor(x), f = frac(x);
    float a = frac(sin(i * 127.1) * 43758.5453);
    float b = frac(sin((i + 1.0) * 127.1) * 43758.5453);
    return lerp(a, b, f * f * (3.0 - 2.0 * f));
}

// Devolve o multiplicador de densidade em xz: 0 = limpo, 1 = normal, >1 = nevoeiro empurrado (acumulado).
float FogClearing(float2 xz)
{
    float f = 1.0;
    float rim = 0.0;

    // Impactos: a onda de choque abre um buraco circular (com contorno irregular) que empurra o
    // nevoeiro para a borda; depois o buraco fecha das bordas para dentro.
    for (int i = 0; i < FOG_MAX_BLASTS; i++)
    {
        float4 b = _FogBlasts[i];
        float4 bp = _FogBlastParams[i];
        float age = _FogTime - b.z;
        if (bp.y <= 0.0 || age < 0.0 || age > bp.y) continue;

        float e = saturate(age / max(bp.z, 1e-3));
        float expand = 1.0 - (1.0 - e) * (1.0 - e) * (1.0 - e);
        float recover = smoothstep(bp.z, bp.y, age);

        float2 dv = xz - b.xy;
        float d = length(dv);
        float ang = atan2(dv.y, dv.x);
        float wob = 1.0 + 0.12 * sin(ang * 5.0 + b.z * 7.0) + 0.06 * sin(ang * 11.0 + b.z * 3.0);
        float r = b.w * expand * wob;
        float hole = r * (1.0 - recover);

        float inside = 1.0 - smoothstep(hole * 0.55, hole + 0.01, d);
        f *= 1.0 - bp.x * inside;

        float rw = 0.18 * b.w + 0.05;
        rim += exp(-((d - r) * (d - r)) / (rw * rw)) * bp.x * (1.0 - recover) * (1.0 - 0.5 * expand);
    }

    // Vento: abre um corredor que cresce na direção do golpe e volta a encher no fim.
    for (int j = 0; j < FOG_MAX_WINDS; j++)
    {
        float4 w = _FogWinds[j];
        float4 wp = _FogWindParams[j];
        float age = _FogTime - wp.z;
        if (wp.w <= 0.0 || age < 0.0 || age > wp.w) continue;

        float strength = length(w.zw);
        float2 dir = w.zw / max(strength, 1e-4);
        bool straight = wp.y < 0.0;
        float baseWidth = abs(wp.y);
        float grow = saturate(age / (wp.w * (straight ? 0.03 : 0.2)));
        grow = 1.0 - (1.0 - grow) * (1.0 - grow);
        float recover = smoothstep(wp.w * 0.35, wp.w, age);

        float2 dv = xz - w.xy;
        float along = dot(dv, dir);
        float side = dot(dv, float2(-dir.y, dir.x));
        float len = wp.x * grow;
        float width = baseWidth;
        float puffs = 1.0;
        if (!straight)
        {
            width *= 0.5 + 0.5 * saturate(along / max(wp.x, 1e-3)); // abre em leque
            width *= 1.0 + 0.15 * sin(along * 0.9 + wp.z * 5.0);
        }
        else
        {
            // Rasto de bala: nasce fino logo atrás dela e depois o ar dispersa-se — o rasto alarga,
            // serpenteia, as bordas ficam irregulares e desfaz-se em bolsas que voltam a encher.
            float spread = saturate(age / (wp.w * 0.6));
            spread = 1.0 - (1.0 - spread) * (1.0 - spread);
            float seed = wp.z * 13.7;
            float n1 = FogNoise(along * 0.55 + seed) * 2.0 - 1.0;
            float n2 = FogNoise(along * 1.7 - age * 1.5 + seed * 0.3) * 2.0 - 1.0;
            width *= (1.0 + 4.0 * spread) * (1.0 + 0.5 * n1 * spread + 0.2 * n2);
            side -= (FogNoise(along * 0.25 + seed + 5.0) * 2.0 - 1.0) * 1.6 * spread;
            puffs = 0.85 + 0.15 * (FogNoise(along * 0.9 + age * 2.0 + seed * 1.7));
        }
        float across = abs(side);

        float inLen = smoothstep(-1.0, 0.5, along) * (1.0 - smoothstep(len * 0.75, len, along));
        float inWid = 1.0 - smoothstep(width * (straight ? 0.2 : 0.45), width, across);
        f *= 1.0 - saturate(strength) * inLen * inWid * puffs * (1.0 - recover);

        // O nevoeiro empurrado acumula-se nas margens do corredor (mais no rasto da bala: o ar foi afastado à bruta).
        float edge = exp(-((across - width) * (across - width)) / (width * width * 0.15 + 0.01));
        rim += edge * inLen * (straight ? 0.85 : 0.5) * (1.0 - recover);
    }

    return max(f, 0.0) + rim * 0.6;
}

#endif
