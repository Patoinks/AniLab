// Números de dano elementais: letras nítidas com degradê à frente, o efeito do elemento atrás delas e um halo
// escuro entre os dois para ler sobre qualquer fundo. _Style escolhe o elemento:
//   0 fogo         — vermelho -> dourado -> amarelo; chamas a subir; no fim consome-se em brasas
//   1 água         — azul fundo -> ciano -> quase branco, brilho molhado; gotas a escorrer; no fim escorre
//   2 eletricidade — violeta -> ciano elétrico -> branco; arcos a estalar à volta; no fim apaga-se a piscar
//   3 ar           — verde-água -> menta -> branco, reflexo a correr; rajadas de lado; no fim leva-o o vento
//   4 terra        — castanho -> ocre -> areia, com fendas; crosta de rocha e pó; no fim desfaz-se em blocos
//   5 gelo         — azul -> ciano claro -> branco, facetas; espigões e pingentes, cintilações; no fim estilhaça
//   6 trevas       — violeta quase negro -> lavanda, a pulsar; fumo escuro a subir; no fim dissolve-se no fumo
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
            float Hash2(float2 c) { return Hash1(dot(c, float2(1.0, 57.0)) + _Seed); }

            float3 Gradient(float gy, float3 lo, float3 mid, float3 hi)
            {
                return gy < 0.5 ? lerp(lo, mid, smoothstep(0.0, 0.5, gy)) : lerp(mid, hi, smoothstep(0.5, 1.0, gy));
            }

            // As letras "esticadas" na direção dir (subir, escorrer, de lado). Cada coluna (ao longo da
            // perpendicular) tem o seu comprimento e a língua afina ao chegar ao fim — pontas em vez de muro.
            // Passos curtos: com traços finos, passos largos deixavam riscas.
            // taper afina a língua com o comprimento (pontas de cristal); blocky dá a cada coluna estreita um
            // comprimento ao acaso em vez de ondulado (espigões).
            float Tongues(float2 p, float2 warp, float t, float2 dir, float reach, float speed, float sharp,
                          float lo, float hi, float taper = 0.0, float blocky = 0.0)
            {
                float2 perp = float2(-dir.y, dir.x);
                float along = dot(p, perp);
                float colH = saturate(FireFbm2(float2(along * 7.5 + t * 0.6 * speed, t * 1.7 * speed)) * 1.9 - 0.35);
                colH = lerp(colH, Hash1(floor(along * 14.0) + _Seed), blocky);
                colH = 0.25 + 0.95 * colH * colH;
                float tongue = 0.0;
                [unroll] for (int k = 0; k <= 14; k++)
                {
                    float fk = k / 14.0;
                    float sway = FireNoise2(float2(along * 8.0, dot(p, dir) * 4.0 - t * 4.5 * speed + fk)) - 0.5;
                    float g = smoothstep(lo + fk * taper, hi + fk * taper,
                        Sdf(p + warp * 0.06 - perp * (sway * 0.12 * fk * sharp) - dir * (fk * 0.42 * reach)));
                    tongue = max(tongue, g * saturate((colH - fk) * 2.5));
                }
                return tongue;
            }

            // Espigões de gelo: colunas estreitas com comprimento ao acaso, cada uma um triângulo (largo junto à
            // letra, ponta no fim). dirY +1 cresce para cima, -1 pingentes para baixo.
            float Spikes(float2 p, float dirY, float len, float freq)
            {
                float x = p.x * freq + _Seed * 0.37;
                float u = frac(x);
                float colH = (0.2 + 0.8 * Hash1(floor(x) + _Seed)) * len;
                float tri = 1.0 - abs(u - 0.5) * 2.0;
                float best = 0.0;
                [unroll] for (int k = 0; k <= 14; k++)
                {
                    float d = k / 14.0 * len;
                    float g = smoothstep(0.35, 0.5, Sdf(p - float2(0.0, dirY * d)));
                    best = max(best, g * saturate((colH * tri - d) * 30.0));
                }
                return best;
            }

            // As letras deslocadas em 8 direções: estende o SDF da fonte (que só chega ~0.1 em para fora).
            float Dilate(float2 p, float body, float r, float spin)
            {
                float d = body;
                [unroll] for (int j = 0; j < 8; j++)
                {
                    float ang = j * 0.7854 + spin;
                    d = max(d, Sdf(p + float2(cos(ang), sin(ang)) * r));
                }
                return d;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float2 p = i.p;
                float t = _Time.y + _Seed;
                float aa = max(fwidth(p.y) * _SdfPerEm, 1e-4);
                int style = (int)round(_Style);

                // Letras: nítidas, só um tremor leve; eletricidade, terra e gelo ficam firmes.
                float2 warp = float2(FireFbm2(float2(p.x * 7.0, p.y * 5.0 - t * 3.2)),
                                     FireFbm2(float2(p.x * 7.0 + 5.2, p.y * 5.0 - t * 3.2 + 3.1))) - 0.5;
                bool solid = style == 2 || style == 4 || style == 5;
                float body = Sdf(p + warp * (solid ? 0.0 : 0.012));
                float letterA = smoothstep(0.52 - aa, 0.52 + aa, body); // ligeiramente mais fino que a fonte
                float gy = saturate((p.y + _CapHalf) / (2.0 * _CapHalf));
                float depth = smoothstep(0.52, 0.8, body);
                float ignite = saturate(1.0 - _Age / 0.18); // clarão ao aparecer
                float reach = lerp(1.0, 1.5, _Crit);

                float3 letterCol, backCol, rimCol = 1.0;
                float backA;
                // Fim: 0 contínuo (dissolveDir diz de onde some), 1 em pedaços, 2 a piscar.
                int ending = 0;
                float dissolveDir = 0.0; // +1 some de baixo, -1 de cima, 2 da direita
                float cells = 12.0;

                if (style == 1)
                {
                    // Água: gotas a escorrer por baixo das letras e uma aura translúcida.
                    float drip = Tongues(p, warp, t, float2(0, -1), reach * 0.8, 0.35, 0.5, 0.22, 0.45);
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
                    dissolveDir = -1.0;
                }
                else if (style == 2)
                {
                    // Eletricidade: arcos finos e quebrados numa faixa à volta das letras, a mudar ~16x/s.
                    float tq = floor(t * 16.0);
                    float dil = Dilate(p, body, 0.15 * lerp(1.0, 1.4, _Crit), tq * 0.37);
                    float band = smoothstep(0.3, 0.5, dil) * (1.0 - letterA);
                    // Linhas de nível de ruído suave (curvas compridas), partidas em zigue-zague por um desvio
                    // de alta frequência: raios finos, não pontos nem estática.
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
                    ending = 2;
                }
                else if (style == 3)
                {
                    // Ar: as letras arrastadas para trás pelo vento, em riscas finas que correm.
                    float2 dir = normalize(float2(-1.0, 0.15));
                    float gust = Tongues(p, warp, t, dir, reach * 1.4, 1.6, 0.3, 0.25, 0.45);
                    // Poucas riscas finas e com falhas (rajadas), não um bloco às riscas.
                    float stripes = sin(p.y * 38.0 + FireNoise2(p * float2(2.0, 9.0) + t * 1.5) * 7.0);
                    float lines = smoothstep(0.75, 0.97, stripes * 0.5 + 0.5)
                        * smoothstep(0.35, 0.6, FireNoise2(float2(p.x * 3.0 + t * 4.0, p.y * 12.0)));
                    float gustA = smoothstep(0.08, 0.25, gust) * lines;
                    float aura = smoothstep(0.25, 0.5, body) * 0.3;
                    backA = max(gustA * 0.85, aura);
                    backCol = lerp(float3(0.25, 0.75, 0.6), float3(0.85, 1.0, 0.95), saturate(gust * 1.4)) * 1.25;

                    letterCol = Gradient(gy, lerp(float3(0.04, 0.42, 0.38), float3(0.0, 0.5, 0.42), _Crit),
                                             lerp(float3(0.35, 0.9, 0.7), float3(0.4, 1.0, 0.75), _Crit),
                                             lerp(float3(0.9, 1.0, 0.95), float3(1.0, 1.0, 1.0), _Crit));
                    // Reflexo a correr pelas letras, como uma rajada.
                    float sheen = pow(saturate(sin(p.x * 4.0 + t * 6.0 + p.y * 2.0)), 10.0);
                    letterCol += sheen * 0.45;
                    letterCol *= (0.9 + 0.2 * depth) * (1.1 + 0.2 * _Crit);
                    rimCol = float3(0.9, 1.0, 0.96);
                    dissolveDir = 2.0;
                }
                else if (style == 4)
                {
                    // Terra: crosta de rocha facetada à volta das letras e pó a escorregar por baixo.
                    float dil = Dilate(p, body, 0.09 * lerp(1.0, 1.3, _Crit), 0.2);
                    float2 cell = floor(p * 16.0);
                    float facet = Hash2(cell);
                    float rock = smoothstep(0.4, 0.46, dil + (facet - 0.5) * 0.22);
                    float dust = Tongues(p, warp, t * 0.6, float2(0, -1), 0.45, 0.4, 2.0, 0.2, 0.45);
                    float dustA = smoothstep(0.1, 0.35, dust) * 0.5 * (0.6 + 0.4 * FireFbm2(p * 6.0 - t));
                    backA = max(rock, dustA);
                    float3 rockCol = lerp(float3(0.16, 0.09, 0.05), float3(0.36, 0.23, 0.13), facet);
                    backCol = lerp(float3(0.62, 0.52, 0.4), rockCol, rock);

                    letterCol = Gradient(gy, lerp(float3(0.34, 0.18, 0.07), float3(0.4, 0.15, 0.04), _Crit),
                                             lerp(float3(0.7, 0.46, 0.22), float3(0.82, 0.5, 0.18), _Crit),
                                             lerp(float3(0.96, 0.84, 0.6), float3(1.0, 0.9, 0.62), _Crit));
                    // Fendas escuras e faces da pedra.
                    float n = FireNoise2(p * 9.0 + _Seed);
                    float crack = saturate(1.0 - abs(n - 0.5) * 28.0) * depth;
                    letterCol *= (1.0 - crack * 0.75) * (0.85 + 0.25 * Hash2(floor(p * 10.0))) * (1.05 + 0.2 * _Crit);
                    ending = 1;
                    cells = 9.0;
                }
                else if (style == 5)
                {
                    // Gelo: espigões a crescer para cima, pingentes curtos por baixo e cintilações.
                    // Espigões: colunas estreitas de comprimento ao acaso que afinam até à ponta.
                    float up = Spikes(p, 1.0, 0.36 * reach, 13.0);
                    float down = Spikes(p + 0.031, -1.0, 0.2 * reach, 17.0);
                    float frost = max(up, down);
                    float frostA = frost;
                    float sparkle = step(0.97, Hash2(floor(p * 30.0) + floor(t * 6.0)))
                        * smoothstep(0.15, 0.45, body) * (1.0 - letterA);
                    float aura = smoothstep(0.25, 0.5, body) * 0.3;
                    backA = saturate(max(max(frostA * 0.9, aura), sparkle));
                    backCol = lerp(float3(0.3, 0.6, 0.95), float3(0.85, 0.97, 1.0), saturate(frost * 1.5)) * 1.2;
                    backCol = lerp(backCol, 2.5, sparkle);

                    letterCol = Gradient(gy, lerp(float3(0.1, 0.32, 0.75), float3(0.05, 0.25, 0.7), _Crit),
                                             lerp(float3(0.55, 0.85, 1.0), float3(0.6, 0.9, 1.0), _Crit),
                                             lerp(float3(0.95, 1.0, 1.0), float3(1.0, 1.0, 1.0), _Crit));
                    // Facetas: bandas diagonais de luz, como cristal talhado.
                    float f = frac((p.x + p.y * 0.6) * 3.5);
                    letterCol += step(0.55, f) * 0.15 + smoothstep(0.04, 0.0, abs(f - 0.55)) * 0.35;
                    letterCol *= (0.9 + 0.2 * depth) * (1.1 + 0.2 * _Crit);
                    rimCol = float3(0.9, 0.98, 1.0);
                    ending = 1;
                    cells = 11.0;
                }
                else if (style == 6)
                {
                    // Trevas: fumo escuro e mole a subir devagar, com uma aura sombria.
                    // Fumo: sem recorte de chama — a densidade vem de ruído mole que sobe e se desfaz em fios.
                    float smoke = Tongues(p, warp * 3.0, t, float2(0, 1), reach * 1.3, 0.35, 3.0, 0.1, 0.5);
                    float wisp = smoothstep(0.3, 0.75, FireFbm2(p * float2(5.0, 3.0) - float2(t * 0.2, t * 0.8)));
                    float smokeA = saturate(smoke * 1.4) * wisp;
                    float aura = smoothstep(0.1, 0.5, body) * 0.55;
                    backA = max(smokeA * 0.8, aura);
                    backCol = lerp(float3(0.05, 0.015, 0.08), float3(0.24, 0.09, 0.38), saturate(smoke * wisp * 1.5));

                    letterCol = Gradient(gy, lerp(float3(0.2, 0.03, 0.32), float3(0.25, 0.0, 0.3), _Crit),
                                             lerp(float3(0.56, 0.26, 0.86), float3(0.66, 0.2, 0.95), _Crit),
                                             lerp(float3(0.92, 0.82, 1.0), float3(1.0, 0.85, 1.0), _Crit));
                    letterCol *= (0.9 + 0.15 * sin(t * 4.0)) * (0.85 + 0.3 * depth) * (1.05 + 0.2 * _Crit);
                    rimCol = float3(0.5, 0.25, 0.8);
                    dissolveDir = -1.0;
                }
                else
                {
                    // Fogo: chamas a subir, mais vermelhas e escuras que as letras para elas sobressaírem.
                    float flame = Tongues(p, warp, t, float2(0, 1), reach, 1.0, 1.0, 0.22, 0.45);
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
                    dissolveDir = 1.0;
                }
                letterCol += ignite * 0.8;

                float keep, rim = 0.0;
                if (ending == 2)
                {
                    float flick = Hash1(floor(t * 25.0) + 3.0);
                    keep = step(_Burn * 1.15, flick) * (1.0 - smoothstep(0.85, 1.0, _Burn));
                }
                else if (ending == 1)
                {
                    // Em pedaços: cada célula some de uma vez (blocos de terra, estilhaços de gelo).
                    float h = Hash2(floor(p * cells) + 0.5);
                    keep = step(_Burn * 1.15, h);
                    rim = (1.0 - step(_Burn * 1.15 + 0.12, h)) * keep * step(0.001, _Burn);
                }
                else
                {
                    float side = dissolveDir > 1.5 ? saturate(0.5 - p.x * 0.4)
                               : dissolveDir > 0.0 ? saturate(p.y / 0.9) : 1.0 - gy;
                    float burnNoise = FireFbm2(p * 9.0 + _Seed) * 0.8 + side * 0.35;
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
