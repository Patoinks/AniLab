// Números de dano elementais: letras nítidas com degradê à frente, o efeito do elemento atrás delas e um halo
// escuro entre os dois para ler sobre qualquer fundo. _Style escolhe o elemento:
//   0 fogo        — vermelho -> dourado -> amarelo; chamas a subir; no fim consome-se em brasas
//   1 água        — azul fundo -> ciano -> quase branco, com brilho molhado; gotas a escorrer; no fim escorre
//   2 eletricidade — violeta -> ciano elétrico -> branco; arcos a estalar à volta; no fim apaga-se a piscar
// Uma quad por número; os algarismos chegam em _GlyphBox/_GlyphUV (espaço em "em", ver ElementDamageNumber.cs).
Shader "KonoSuba/ElementDamageNumber"
{
    Properties
    {
        _MainTex ("Atlas SDF", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "FireCommon.cginc"

            #define MAX_GLYPHS 8

            sampler2D _MainTex;
            float4 _GlyphBox[MAX_GLYPHS]; // xy origem, zw tamanho (em)
            float4 _GlyphUV[MAX_GLYPHS];  // xy origem, zw tamanho (UV do atlas)
            float _GlyphCount;
            float _Seed, _Age, _Burn, _Crit, _Style;
            float _SdfPerEm; // quanto o SDF muda por em (para o anti-aliasing)
            float _CapHalf;  // metade da altura das maiúsculas (em), para o degradê

            struct Attributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 pos : SV_POSITION; float2 p : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.p = v.uv;
                return o;
            }

            // SDF do número inteiro (0.5 = borda, 1 = bem dentro, 0 = longe).
            float Sdf(float2 p)
            {
                float s = 0.0;
                for (int i = 0; i < MAX_GLYPHS; i++)
                {
                    if (i >= (int)_GlyphCount) break;
                    float2 l = (p - _GlyphBox[i].xy) / _GlyphBox[i].zw;
                    if (all(l >= 0.0) && all(l <= 1.0))
                        s = max(s, tex2Dlod(_MainTex, float4(_GlyphUV[i].xy + l * _GlyphUV[i].zw, 0, 0)).a);
                }
                return s;
            }

            float Hash1(float x) { return frac(sin(x * 127.1 + 311.7) * 43758.5453); }

            float3 Gradient(float gy, float3 lo, float3 mid, float3 hi)
            {
                return gy < 0.5 ? lerp(lo, mid, smoothstep(0.0, 0.5, gy)) : lerp(mid, hi, smoothstep(0.5, 1.0, gy));
            }

            // Línguas: o contorno dilatado "esticado" na direção dir (+1 sobe, -1 escorre). Cada coluna tem a sua
            // altura (ruído em x) e a língua afina ao chegar ao fim da coluna — pontas em vez de muro.
            // Passos curtos: com traços finos, passos largos deixavam riscas horizontais.
            float Tongues(float2 p, float2 warp, float t, float dir, float reach, float speed, float sharp)
            {
                float colH = saturate(FireFbm2(float2(p.x * 7.5 + t * 0.6 * speed, t * 1.7 * speed)) * 1.9 - 0.35);
                colH = 0.25 + 0.95 * colH * colH;
                float tongue = 0.0;
                [unroll] for (int k = 0; k <= 14; k++)
                {
                    float fk = k / 14.0;
                    float sway = FireNoise2(float2(p.x * 8.0, p.y * 4.0 - dir * t * 4.5 * speed + fk)) - 0.5;
                    float g = smoothstep(0.22, 0.45,
                        Sdf(p + warp * 0.06 - float2(sway * 0.12 * fk * sharp, dir * fk * 0.42 * reach)));
                    tongue = max(tongue, g * saturate((colH - fk) * 2.5));
                }
                return tongue;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float2 p = i.p;
                float t = _Time.y + _Seed;
                float aa = max(fwidth(p.y) * _SdfPerEm, 1e-4);
                int style = (int)round(_Style);

                // Letras: nítidas, só um tremor leve (ar quente / água); a eletricidade treme no C# (glitch).
                float2 warp = float2(FireFbm2(float2(p.x * 7.0, p.y * 5.0 - t * 3.2)),
                                     FireFbm2(float2(p.x * 7.0 + 5.2, p.y * 5.0 - t * 3.2 + 3.1))) - 0.5;
                float body = Sdf(p + warp * (style == 2 ? 0.0 : 0.012));
                float letterA = smoothstep(0.52 - aa, 0.52 + aa, body); // ligeiramente mais fino que a fonte
                float gy = saturate((p.y + _CapHalf) / (2.0 * _CapHalf));
                float depth = smoothstep(0.52, 0.8, body);
                float ignite = saturate(1.0 - _Age / 0.18); // clarão ao aparecer
                float reach = lerp(1.0, 1.5, _Crit);

                float3 letterCol, backCol;
                float backA;
                float3 rimCol;
                float burnDir; // de onde o número desaparece: +1 por baixo (fogo), -1 por cima (água)

                if (style == 1)
                {
                    // Água: gotas a escorrer por baixo das letras e uma aura translúcida.
                    float drip = Tongues(p, warp, t, -1.0, reach * 0.8, 0.35, 0.5);
                    float dripA = smoothstep(0.18, 0.3, drip);
                    float aura = smoothstep(0.2, 0.5, body) * 0.35;
                    backA = max(dripA * 0.9, aura);
                    backCol = lerp(float3(0.05, 0.3, 0.85), float3(0.35, 0.85, 1.0), saturate(drip * 1.5)) * 1.2;

                    letterCol = Gradient(gy, lerp(float3(0.02, 0.2, 0.6), float3(0.0, 0.12, 0.5), _Crit),
                                             lerp(float3(0.1, 0.58, 1.0), float3(0.15, 0.7, 1.0), _Crit),
                                             lerp(float3(0.78, 0.96, 1.0), float3(0.92, 1.0, 1.0), _Crit));
                    // Brilho molhado: uma faixa clara ondulada a atravessar as letras.
                    float band = gy - 0.68 - 0.05 * sin(p.x * 7.0 + t * 2.0);
                    letterCol += smoothstep(0.06, 0.0, abs(band)) * 0.6;
                    letterCol *= (0.9 + 0.2 * depth) * (1.1 + 0.2 * _Crit);
                    rimCol = float3(0.85, 0.97, 1.0);
                    burnDir = -1.0;
                }
                else if (style == 2)
                {
                    // Eletricidade: arcos finos e quebrados numa faixa à volta das letras, a mudar ~16x/s, e um halo.
                    // O SDF da fonte só chega ~0.1 em para fora: a faixa vem das letras deslocadas em 8 direções.
                    float tq = floor(t * 16.0);
                    float r = 0.15 * lerp(1.0, 1.4, _Crit);
                    float dil = body;
                    [unroll] for (int j = 0; j < 8; j++)
                    {
                        float ang = j * 0.7854 + tq * 0.37;
                        dil = max(dil, Sdf(p + float2(cos(ang), sin(ang)) * r));
                    }
                    float band = smoothstep(0.3, 0.5, dil) * (1.0 - letterA);
                    // Linhas de nível de ruído suave (curvas compridas), com um desvio de alta frequência que as
                    // parte em zigue-zague: raios finos, não pontos nem estática.
                    float2 jag = (float2(FireNoise2(p * 24.0 + tq), FireNoise2(p * 24.0 - tq + 5.0)) - 0.5) * 0.06;
                    float n = FireNoise2((p + jag) * 5.0 + tq * 3.71);
                    float arcLine = saturate(1.0 - abs(n - 0.5) * 24.0);
                    float n2 = FireNoise2((p - jag) * 7.0 - tq * 2.93 + 9.0);
                    arcLine = max(arcLine, saturate(1.0 - abs(n2 - 0.5) * 30.0));
                    float patches = smoothstep(0.38, 0.55, FireNoise2(p * 2.0 + tq * 2.13));
                    float arcs = saturate(arcLine * 1.5) * band * patches * lerp(1.0, 1.3, _Crit);
                    // Brilho só colado à borda das letras (na faixa toda parecia uma placa roxa).
                    float glow = smoothstep(0.32, 0.5, body) * (1.0 - letterA) * (0.5 + 0.3 * Hash1(tq));
                    backA = saturate(max(arcs, glow * 0.6));
                    backCol = lerp(float3(0.45, 0.55, 1.0) * 1.4, float3(0.85, 0.95, 1.0) * 2.4, saturate(arcs * 1.5));

                    letterCol = Gradient(gy, lerp(float3(0.38, 0.1, 0.9), float3(0.55, 0.05, 0.95), _Crit),
                                             lerp(float3(0.3, 0.72, 1.0), float3(0.45, 0.82, 1.0), _Crit),
                                             lerp(float3(0.9, 0.97, 1.0), float3(1.0, 1.0, 1.0), _Crit));
                    // Pisca como uma lâmpada com mau contacto.
                    letterCol *= (0.85 + 0.35 * step(0.5, Hash1(tq + 0.5))) * (0.9 + 0.25 * depth) * (1.15 + 0.2 * _Crit);
                    rimCol = float3(0.8, 0.9, 1.0);
                    burnDir = 0.0;
                }
                else
                {
                    // Fogo: chamas a subir, mais vermelhas e escuras que as letras para elas sobressaírem.
                    float flame = Tongues(p, warp, t, 1.0, reach, 1.0, 1.0);
                    float cols = FireFbm2(float2(p.x * 13.0, p.y * 3.0 - t * 6.0));
                    flame *= smoothstep(0.25, 0.7, cols + flame * 0.5);
                    backA = smoothstep(0.1, 0.22, flame);
                    backCol = FireRamp(0.14 + 0.36 * saturate(flame * 1.6)) * 1.5;

                    letterCol = Gradient(gy, lerp(float3(0.85, 0.16, 0.02), float3(0.75, 0.04, 0.03), _Crit),
                                             lerp(float3(1.0, 0.52, 0.06), float3(1.0, 0.42, 0.05), _Crit),
                                             lerp(float3(1.0, 0.84, 0.3), float3(1.0, 0.94, 0.62), _Crit));
                    // Brilho a correr pelas letras e núcleo mais claro longe da borda.
                    float flow = FireFbm2(float2(p.x * 9.0, p.y * 6.0 - t * 3.0));
                    letterCol *= (0.85 + 0.3 * flow) * (0.9 + 0.25 * depth) * (1.15 + 0.2 * _Crit);
                    rimCol = FireRamp(1.0);
                    burnDir = 1.0;
                }
                letterCol += ignite * 0.8;

                // Fim: fogo arde de baixo para cima, água escorre (some por cima), eletricidade apaga-se a piscar.
                float keep, rim;
                if (style == 2)
                {
                    float flick = Hash1(floor(t * 25.0) + 3.0);
                    keep = step(_Burn * 1.15, flick) * (1.0 - smoothstep(0.85, 1.0, _Burn));
                    rim = 0.0;
                }
                else
                {
                    float burnNoise = FireFbm2(p * 9.0 + _Seed) * 0.8
                        + (burnDir > 0.0 ? saturate(p.y / 0.9) : 1.0 - gy) * 0.35;
                    float edge = _Burn * 1.25 - 0.1;
                    keep = smoothstep(edge, edge + 0.08, burnNoise);
                    rim = (1.0 - smoothstep(edge + 0.05, edge + 0.16, burnNoise)) * keep * step(0.001, _Burn);
                }
                letterCol = lerp(letterCol, rimCol, rim * 0.8);
                letterA *= keep;
                backA *= keep;

                // Composição (pré-multiplicada), de trás para a frente: efeito, halo escuro, letras.
                float halo = smoothstep(0.22, 0.5, body) * (1.0 - letterA) * keep * 0.8;
                float3 col = backCol * backA;
                float a = backA;
                col *= 1.0 - halo;
                a = halo + a * (1.0 - halo);
                col = letterCol * letterA + col * (1.0 - letterA);
                a = letterA + a * (1.0 - letterA);
                return float4(col, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
