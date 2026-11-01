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
            // Desenhado pelo AnimLabRenderFeature depois do tonemap e do grading (como o HUD): as cores saem
            // exatamente como estão aqui, em linear 0..1.
            Tags { "LightMode"="AnimLabOverlay" }
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
            float _Fx; // variante (0 completo; 1–6 minimalistas; 7 Metin; 8 só a fonte; 9 creme + brilho laranja)
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

            // Cor de destaque de cada elemento (variantes minimalistas).
            float3 Accent(int style)
            {
                return style == 7 ? float3(1.0, 1.0, 1.0) : style == 1 ? float3(0.25, 0.65, 1.0) : style == 2 ? float3(0.6, 0.6, 1.0)
                     : style == 3 ? float3(0.35, 1.0, 0.75) : style == 4 ? float3(0.85, 0.55, 0.28)
                     : style == 5 ? float3(0.6, 0.88, 1.0) : style == 6 ? float3(0.6, 0.28, 0.95)
                     : float3(1.0, 0.42, 0.08);
            }

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

            // Marca a pincel: folha com pontas nos dois lados, centrada em c ao longo de dir. Distância aproximada
            // (em): negativa dentro.
            float Lens(float2 p, float2 c, float2 dir, float halfLen, float halfW)
            {
                float2 q = p - c;
                float u = dot(q, dir), v = dot(q, float2(-dir.y, dir.x));
                float t = saturate(abs(u) / halfLen);
                float dEnd = abs(u) - halfLen;
                return dEnd > 0.0 ? length(float2(dEnd, v)) : abs(v) - halfW * (1.0 - t * t);
            }

            float SegDist(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                return length(pa - ba * saturate(dot(pa, ba) / dot(ba, ba)));
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
                // Ligeiramente mais grosso que a fonte: os traços do pincel são finos.
                float letterA = smoothstep(0.46 - aa, 0.46 + aa, body);
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
                // Variantes minimalistas: trocam o efeito de trás (e na 6 as próprias letras).
                //   1 brilho suave · 2 contorno fino + sombra · 3 efeito pequeno · 4 faíscas · 5 risco de luz
                //   6 letras quase brancas tingidas + sombra
                //   7 Metin: núcleo creme, borda na cor do elemento, brilho suave à volta, riscos compridos atrás
                int fx = (int)round(_Fx);
                float haloK = 0.8;
                float shadow = 0.0;
                float3 accent = Accent(style);
                if (fx == 1)
                {
                    float g = smoothstep(0.0, 0.5, body);
                    backA = g * g * 0.75;
                    backCol = accent * 1.6;
                    haloK = 0.0;
                }
                else if (fx == 2 || fx == 6)
                {
                    float stroke = smoothstep(0.4, 0.46, body) * (1.0 - letterA);
                    backA = fx == 2 ? stroke : 0.0;
                    backCol = accent * 1.3;
                    shadow = smoothstep(0.3, 0.5, Sdf(p + float2(-0.025, 0.04))) * 0.55;
                    haloK = 0.0;
                    if (fx == 6)
                        letterCol = lerp(float3(1.0, 1.0, 1.0), accent, lerp(0.55, 0.15, gy)) * (1.05 + 0.15 * _Crit);
                }
                else if (fx == 3)
                {
                    backA *= 0.55;
                    haloK = 0.45;
                }
                else if (fx == 4)
                {
                    // Faíscas: pontos pequenos em células, a subir, só perto das letras.
                    float2 q = p * 16.0 + float2(0.0, -t * 2.5);
                    float2 cell = floor(q);
                    float h = Hash2(cell);
                    float2 dotPos = float2(Hash2(cell + 7.0), Hash2(cell + 13.0)) * 0.6 + 0.2;
                    float dotD = length(frac(q) - dotPos);
                    float near = smoothstep(0.15, 0.45, Dilate(p, body, 0.12, 0.0)) * (1.0 - letterA);
                    float spark = step(0.8, h) * smoothstep(0.16, 0.04, dotD) * near;
                    backA = saturate(spark + smoothstep(0.25, 0.5, body) * 0.25);
                    backCol = accent * (1.2 + spark * 1.2);
                    haloK = 0.5;
                }
                else if (fx == 5)
                {
                    // Risco de luz horizontal por trás, a toda a largura do número.
                    float x0 = 1e3, x1 = -1e3;
                    for (int g = 0; g < MAX_GLYPHS; g++)
                    {
                        if (g >= (int)_GlyphCount) break;
                        x0 = min(x0, _GlyphBox[g].x);
                        x1 = max(x1, _GlyphBox[g].x + _GlyphBox[g].z);
                    }
                    float cx = (x0 + x1) * 0.5, hw = (x1 - x0) * 0.5 + 0.25;
                    float across = saturate(1.0 - abs(p.x - cx) / hw);
                    float beam = exp(-pow((p.y + 0.02) / 0.05, 2.0)) * across * across;
                    float wide = exp(-pow((p.y + 0.02) / 0.18, 2.0)) * across * 0.35;
                    backA = saturate(beam + wide);
                    backCol = lerp(accent, 1.0, beam * 0.6) * 1.5;
                    haloK = 0.45;
                }
                else if (fx == 7)
                {
                    // Letras: creme no meio do traço, cor do elemento na borda (pela profundidade, não pela altura).
                    float3 core = lerp(float3(1.0, 0.97, 0.82), 1.0, style == 0 ? 0.0 : 0.5);
                    core = lerp(core, accent, style == 0 ? 0.0 : 0.12);
                    float3 edge = style == 0 ? float3(1.0, 0.5, 0.1) : accent;
                    float3 deep = style == 0 ? float3(0.85, 0.12, 0.03) : accent * float3(0.55, 0.55, 0.75);
                    float inner = smoothstep(0.56, 0.78, body); // núcleo creme no meio do traço, borda laranja
                    letterCol = lerp(edge, core, inner) * lerp(0.92, 1.12, gy) * (1.1 + 0.15 * _Crit);

                    // Riscos: linhas finas e inclinadas por trás, mais compridas que o número, com pontas;
                    // crescem do centro para fora no início.
                    float x0 = 1e3, x1 = -1e3;
                    for (int g = 0; g < MAX_GLYPHS; g++)
                    {
                        if (g >= (int)_GlyphCount) break;
                        x0 = min(x0, _GlyphBox[g].x);
                        x1 = max(x1, _GlyphBox[g].x + _GlyphBox[g].z);
                    }
                    float cx = (x0 + x1) * 0.5, hw = (x1 - x0) * 0.5;
                    float grow = smoothstep(0.0, 0.16, _Age);
                    float streaks = 0.0;
                    [unroll] for (int k = 0; k < 3; k++)
                    {
                        float off = (k - 1) * 0.16 + (Hash1(_Seed + k) - 0.5) * 0.06;
                        float len = (hw + 0.35 + 0.25 * Hash1(_Seed + k + 5.0)) * lerp(0.3, 1.0, grow);
                        float u = (p.x - cx - (k - 1) * 0.12) / len;
                        float lineY = off - (p.x - cx) * 0.14;
                        // Largura mínima de ~1.5 px e anti-aliasing pelo fwidth: finos, mas contínuos.
                        float px1 = fwidth(p.y);
                        float w = max((k == 1 ? 0.045 : 0.03) * saturate(1.0 - u * u), px1 * 0.75);
                        float d = abs(p.y - lineY);
                        float fade = saturate(1.0 - u * u) * (k == 1 ? 1.0 : 0.7);
                        streaks = max(streaks, (1.0 - smoothstep(w - px1, w + px1, d)) * step(abs(u), 1.0) * fade);
                    }
                    // Os riscos saem dos lados do número; junto às letras apagam-se (pareciam texto riscado).
                    float d06 = Dilate(p, body, 0.06, 0.0);
                    streaks *= (1.0 - letterA) * (1.0 - smoothstep(0.2, 0.5, d06) * 0.9);
                    // Brilho que se apaga com a distância (o SDF da fonte só chega ~0.1 em para fora): três anéis
                    // de letras dilatadas, cada vez mais fracos. Emissivo: cor forte com pouca opacidade, para
                    // somar luz em vez de tapar o fundo como um autocolante.
                    float g0 = smoothstep(0.0, 0.5, body);
                    float glow = g0 * g0 * 0.6
                        + smoothstep(0.2, 0.5, d06) * 0.16
                        + smoothstep(0.2, 0.5, Dilate(p, body, 0.13, 0.39)) * 0.08;
                    float streakA = streaks * 0.9;
                    backA = saturate(max(glow * 0.35, streakA));
                    float3 light = deep * glow * 2.6 + edge * streakA * 1.6;
                    backCol = light / max(backA, 1e-3);
                    shadow = smoothstep(0.3, 0.5, Sdf(p + float2(-0.02, 0.035))) * 0.45;
                    haloK = 0.0;
                }
                else if (fx == 9)
                {
                    // Estilo das referências do Manuel: algarismos a pincel claros, borda e brilho na cor do elemento,
                    // e um efeito por elemento. Fogo: creme e brilho laranja. Eletricidade: branco,
                    // borda e brilho violeta escuros, raio fino em zigue-zague. Ar: branco-menta, brilho verde,
                    // rajadas de penas atrás. Neutro (7): branco liso. Os outros: brilho na cor do elemento.
                    // Usa a fonte com SDF largo (ElementDamageNumber.cs), por isso o brilho vem direto da distância.
                    float padEm = 0.5 / _SdfPerEm;
                    float h = 2.0 * _CapHalf;
                    float top = _CapHalf, bottom = -_CapHalf;
                    float ix0 = 1e3, ix1 = -1e3;
                    for (int g = 0; g < MAX_GLYPHS; g++)
                    {
                        if (g >= (int)_GlyphCount) break;
                        ix0 = min(ix0, _GlyphBox[g].x + padEm);
                        ix1 = max(ix1, _GlyphBox[g].x + _GlyphBox[g].z - padEm);
                    }
                    // em; mais grosso que a fonte e com as bordas alisadas (média de 5 amostras do SDF): as referências
                    // têm traços mais cheios e sem a rugosidade do pincel. O neutro fica com a fonte tal como é.
                    float r = 0.022 * h;
                    float smoothBody = (body * 2.0 + Sdf(p + float2(r, r)) + Sdf(p + float2(-r, r))
                                      + Sdf(p + float2(r, -r)) + Sdf(p + float2(-r, -r))) / 6.0;
                    float dNum = style == 7 ? (0.5 - body) / _SdfPerEm
                                            : (0.5 - smoothBody) / _SdfPerEm - 0.035 * h;

                    // Sem marcas extra (o fogo tinha duas folhas a pincel nas diagonais; o Manuel tirou-as). dMark fica
                    // para as contas de baixo, sempre "longe".
                    float dMark = 1e3;
                    float d = min(dNum, dMark);
                    float aaEm = max(fwidth(p.y), 1e-4);
                    letterA = smoothstep(aaEm, -aaEm, d);

                    // Cores lineares 0..1 (desenhado depois do tonemap). Brilho: duas exponenciais (perto, cauda),
                    // em unidades de h; emissivo (soma luz) ou escuro (escurece o fundo, a eletricidade).
                    float3 fill, rimIn, glowCol;
                    float nearA = 0.38, nearR = 0.08, farA = 0.12, farR = 0.22, rimW = 0.07;
                    if (style == 0)
                    {
                        // sRGB da referência: creme 255,248,215; borda de dentro 255,205,152; brilho ~255,124,48.
                        fill = dMark < dNum ? float3(1.0, 0.7, 0.4) : float3(1.0, 0.94, 0.68);
                        rimIn = float3(1.0, 0.45, 0.2);
                        glowCol = float3(1.0, 0.2, 0.03);
                    }
                    else if (style == 2)
                    {
                        // Branco com contorno violeta e uma sombra violeta escura à volta (referência: 35,20,73 junto
                        // às letras, a desaparecer em ~0.8 h). É sombra, não luz: escurece o fundo.
                        fill = float3(0.9, 0.88, 1.0);
                        rimIn = float3(0.16, 0.04, 0.62);
                        rimW = 0.05;
                        glowCol = float3(0.016, 0.005, 0.066);
                        nearA = 0.8; nearR = 0.09; farA = 0.5; farR = 0.32;
                    }
                    else if (style == 3)
                    {
                        // Quase branco com um toque de menta; brilho verde fraco e difuso (referência: só +40 no verde
                        // junto às letras).
                        fill = float3(0.9, 1.0, 0.93);
                        rimIn = float3(0.55, 1.0, 0.7);
                        rimW = 0.03;
                        glowCol = float3(0.045, 0.4, 0.155);
                        nearA = 0.26; nearR = 0.06; farA = 0.18; farR = 0.36;
                    }
                    else if (style == 7)
                    {
                        // Neutro: branco liso, sem brilho.
                        fill = 1.0; rimIn = 1.0; glowCol = 0.0;
                        nearA = 0.0; farA = 0.0;
                    }
                    else
                    {
                        // Sem referência ainda: branco tingido, borda e brilho na cor do elemento.
                        fill = lerp(1.0, accent, 0.12);
                        rimIn = accent;
                        rimW = 0.045;
                        glowCol = accent * accent;
                        nearA = 0.32; nearR = 0.08; farA = 0.12; farR = 0.24;
                    }
                    letterCol = lerp(rimIn, fill, smoothstep(0.0, 1.0, saturate(-d / (rimW * h)))) * (1.0 + 0.1 * _Crit);

                    // Fora da caixa de cada algarismo o SDF é 0 ("longe"): o brilho dos algarismos apaga-se antes do fim
                    // do SDF; o das marcas é analítico, não tem limite. A referência deixa de ter brilho por ~0.9 h.
                    float dN = max(dNum, 0.0) / h, dM = max(dMark, 0.0) / h;
                    float glowN = (nearA * exp(-dN / nearR) + farA * exp(-dN / farR)) * smoothstep(0.0, 0.14, body);
                    float glowM = nearA * exp(-dM / nearR) + farA * exp(-dM / farR);
                    float glow = (1.0 - (1.0 - glowN) * (1.0 - glowM)) * (1.0 - smoothstep(0.4, 0.75, min(dN, dM)));
                    glow *= 1.0 + 0.2 * _Crit;
                    float3 backPm = glowCol * glow; // pré-multiplicado
                    backA = glow;

                    if (style == 2)
                    {
                        // Raio: sai de baixo do fim do número, sobe para a direita com um zigue-zague perto do início.
                        // Núcleo lavanda fino, brilho violeta à volta e um clarão no vértice; um fio fraco em cima à
                        // esquerda e uma faísca.
                        // Curva em 8 segmentos: começa por baixo do número, sobe para a direita e encurva para cima
                        // no fim; perto do início dá um passo para trás (o "Z" do raio), com um clarão no vértice.
                        float j = (Hash1(_Seed) - 0.5) * 0.25;
                        float x0b = ix1 - 0.55 * h, x1b = ix1 + 1.3 * h;
                        float db = 1e3, core = 0.0;
                        float2 prev = float2(x0b, bottom - 0.2 * h);
                        float2 kink = prev;
                        [unroll] for (int k = 1; k <= 8; k++)
                        {
                            float uk = k / 8.0;
                            float2 pt = float2(lerp(x0b, x1b, uk), bottom - 0.2 * h + (0.65 + j) * h * uk * uk);
                            if (k == 3) { kink = pt + float2(0.12 * h, 0.08 * h); pt = kink; }
                            if (k == 4) pt.x -= 0.16 * h;
                            float dk = SegDist(p, prev, pt);
                            float wk = 0.02 * h * (1.0 - 0.75 * uk) * saturate(uk * 6.0);
                            core = max(core, 1.0 - smoothstep(wk - aaEm, wk + aaEm, dk));
                            db = min(db, dk);
                            prev = pt;
                        }
                        float along = saturate((p.x - x0b) / (x1b - x0b));
                        float bolt = core + 0.6 * exp(-db / (0.04 * h)) * (1.0 - along * 0.5)
                                   + 1.2 * exp(-length(p - kink) / (0.07 * h));
                        float faint = SegDist(p, float2(ix0 - 1.0 * h, top + 0.1 * h), float2(ix0 - 0.2 * h, bottom + 0.4 * h));
                        bolt += 0.18 * (1.0 - smoothstep(0.006 * h, 0.016 * h, faint)) + 0.08 * exp(-faint / (0.03 * h));
                        bolt += 0.8 * exp(-length(p - float2(ix0 + 0.1 * h, top + 0.55 * h)) / (0.016 * h));
                        bolt = saturate(bolt) * (1.0 - letterA);
                        // Referência: núcleo 160,128,255 (linear ~0.35,0.22,1), brilho violeta.
                        backPm = backPm * (1.0 - bolt) + lerp(float3(0.22, 0.1, 0.9), float3(0.42, 0.3, 1.0), core) * bolt;
                        backA = backA + bolt * (1.0 - backA);
                    }
                    else if (style == 3)
                    {
                        // Rajadas: faixa curva de penas finas por trás, de cima à esquerda a baixo à direita, com as
                        // fibras a correr; uma segunda mais pequena em baixo à direita; partículas verdes.
                        float xa = ix0 - 0.8 * h, xb = ix1 + 0.9 * h;
                        float u = (p.x - xa) / (xb - xa);
                        float wisp = 0.0;
                        [unroll] for (int k = 0; k < 2; k++)
                        {
                            // 0: de cima à esquerda, por trás do meio do número, até baixo à direita (em S).
                            // 1: mais pequena, só em baixo à direita.
                            float u0 = k == 0 ? 0.0 : 0.5;
                            float uu = saturate((u - u0) / (1.0 - u0));
                            float yc = k == 0 ? lerp(top + 0.15 * h, bottom - 0.3 * h, smoothstep(0.0, 1.0, uu))
                                              : lerp(bottom + 0.05 * h, bottom - 0.45 * h, uu * uu);
                            float hw = (k == 0 ? 0.42 : 0.2) * h * pow(sin(3.14159 * uu), 0.5) + 1e-4;
                            float v = (p.y - yc) / hw;
                            float band = exp(-v * v * 2.2) * step(u0, u) * step(u, 1.0) * smoothstep(0.0, 0.15, uu);
                            float fibre = sin(v * 5.0 + FireNoise2(float2(uu * 4.0 - t * 1.2, v * 1.5) + _Seed + k * 3.0) * 4.0);
                            wisp = max(wisp, band * (0.4 + 0.6 * smoothstep(-0.2, 1.0, fibre)) * (k == 0 ? 0.55 : 0.4));
                        }
                        float2 q = p / (0.09 * h) + float2(_Seed, -t * 0.6);
                        float2 cell = floor(q);
                        float dotD = length(frac(q) - (float2(Hash2(cell + 7.0), Hash2(cell + 13.0)) * 0.6 + 0.2));
                        float nearDots = 1.0 - smoothstep(0.2, 0.55, min(dN, 1.0));
                        float spark = step(0.93, Hash2(cell)) * (1.0 - smoothstep(0.05, 0.14, dotD)) * nearDots;
                        float add = saturate(wisp + spark) * (1.0 - letterA);
                        // Referência: 86,157,127 sobre o fundo, translúcido (linear ~0.16,0.58,0.3).
                        backPm = backPm * (1.0 - add) + float3(0.16, 0.58, 0.3) * add;
                        backA = backA + add * (1.0 - backA);
                    }
                    backCol = backPm / max(backA, 1e-4);
                    haloK = 0.0;
                }
                else if (fx == 8)
                {
                    // Só a fonte: branco liso, sem efeito, brilho nem sombra (para ver a forma dos algarismos).
                    letterCol = 1.0;
                    letterA = smoothstep(0.5 - aa, 0.5 + aa, body); // a espessura exata da fonte
                    backA = 0.0;
                    haloK = 0.0;
                    ignite = 0.0;
                }
                letterCol += ignite * 0.8;

                float keep, rim = 0.0;
                if (fx >= 7) keep = 1.0 - smoothstep(0.0, 1.0, _Burn);
                else if (ending == 2)
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
                shadow *= keep;

                // Composição (pré-multiplicada), de trás para a frente: efeito, halo escuro, letras.
                float halo = max(smoothstep(0.22, 0.5, body) * (1.0 - letterA) * keep * haloK, shadow);
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
