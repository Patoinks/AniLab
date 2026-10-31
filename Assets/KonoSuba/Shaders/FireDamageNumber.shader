// Números de dano de fogo: letras nítidas com degradê (vermelho -> dourado -> amarelo) à frente, fogo a subir
// atrás delas e um halo escuro entre os dois para ler sobre qualquer fundo. No fim o número consome-se em brasas.
// Uma quad por número; os algarismos chegam em _GlyphBox/_GlyphUV (espaço em "em", ver FireDamageNumber.cs).
Shader "KonoSuba/FireDamageNumber"
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
            float _Seed, _Age, _Burn, _Crit;
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

            float4 Frag(Varyings i) : SV_Target
            {
                float2 p = i.p;
                float t = _Time.y + _Seed;
                float aa = max(fwidth(p.y) * _SdfPerEm, 1e-4);

                // Letras: nítidas, só um tremor leve de ar quente.
                float2 warp = float2(FireFbm2(float2(p.x * 7.0, p.y * 5.0 - t * 3.2)),
                                     FireFbm2(float2(p.x * 7.0 + 5.2, p.y * 5.0 - t * 3.2 + 3.1))) - 0.5;
                float body = Sdf(p + warp * 0.012);
                float letterA = smoothstep(0.52 - aa, 0.52 + aa, body); // ligeiramente mais fino que a fonte

                // Fogo atrás das letras: o contorno dilatado "esticado" para cima. Cada coluna tem a sua altura
                // (ruído em x que tremeluz) e a língua afina ao chegar ao topo da coluna — pontas em vez de muro.
                float reach = lerp(1.0, 1.5, _Crit);
                float colH = saturate(FireFbm2(float2(p.x * 7.5 + t * 0.6, t * 1.7)) * 1.9 - 0.35);
                colH = 0.25 + 0.95 * colH * colH;
                float tongue = 0.0;
                // Passos curtos: com traços finos, passos largos deixavam riscas horizontais nas línguas.
                [unroll] for (int k = 0; k <= 14; k++)
                {
                    float fk = k / 14.0;
                    float2 sway = float2(FireNoise2(float2(p.x * 8.0, p.y * 4.0 - t * 4.5 + fk)) - 0.5, 0.0);
                    float g = smoothstep(0.22, 0.45, Sdf(p + warp * 0.06 - float2(sway.x * 0.12 * fk, fk * 0.42 * reach)));
                    tongue = max(tongue, g * saturate((colH - fk) * 2.5));
                }
                float cols = FireFbm2(float2(p.x * 13.0, p.y * 3.0 - t * 6.0));
                float flame = tongue * smoothstep(0.25, 0.7, cols + tongue * 0.5);
                float flameA = smoothstep(0.1, 0.22, flame);
                // Mais vermelho e escuro que as letras, para elas sobressaírem à frente.
                float3 flameCol = FireRamp(0.14 + 0.36 * saturate(flame * 1.6)) * 1.5;

                // Degradê das letras, de baixo para cima: vermelho-laranja -> dourado -> amarelo claro.
                // Críticos: carmesim -> laranja -> quase branco, mais intenso.
                float gy = saturate((p.y + _CapHalf) / (2.0 * _CapHalf));
                float3 lo = lerp(float3(0.85, 0.16, 0.02), float3(0.75, 0.04, 0.03), _Crit);
                float3 mid = lerp(float3(1.0, 0.52, 0.06), float3(1.0, 0.42, 0.05), _Crit);
                float3 hi = lerp(float3(1.0, 0.84, 0.3), float3(1.0, 0.94, 0.62), _Crit);
                float3 letterCol = gy < 0.5 ? lerp(lo, mid, smoothstep(0.0, 0.5, gy)) : lerp(mid, hi, smoothstep(0.5, 1.0, gy));
                // Brilho a correr pelas letras e núcleo mais claro longe da borda.
                float flow = FireFbm2(float2(p.x * 9.0, p.y * 6.0 - t * 3.0));
                float depth = smoothstep(0.52, 0.8, body);
                float ignite = saturate(1.0 - _Age / 0.18); // clarão ao aparecer
                letterCol *= (0.85 + 0.3 * flow) * (0.9 + 0.25 * depth) * (1.15 + 0.2 * _Crit);
                letterCol += ignite * 0.8;

                // A arder no fim: o ruído come o número de cima para baixo, com uma franja incandescente.
                float burnNoise = FireFbm2(p * 9.0 + _Seed) * 0.8 + saturate(p.y / 0.9) * 0.35;
                float edge = _Burn * 1.25 - 0.1;
                float keep = smoothstep(edge, edge + 0.08, burnNoise);
                float rim = (1.0 - smoothstep(edge + 0.05, edge + 0.16, burnNoise)) * keep * step(0.001, _Burn);
                letterCol = lerp(letterCol, FireRamp(1.0), rim * 0.8);
                letterA *= keep;
                flameA *= keep;

                // Composição (pré-multiplicada), de trás para a frente: fogo, halo escuro, letras.
                float halo = smoothstep(0.22, 0.5, body) * (1.0 - letterA) * keep * 0.8;
                float3 col = flameCol * flameA;
                float a = flameA;
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
