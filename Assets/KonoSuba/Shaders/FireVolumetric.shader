// Fogo volumétrico: o volume dentro de uma esfera (raio 0,5 em espaço de objeto) é percorrido por raymarching.
// Em cada amostra calcula-se uma densidade (uma bola "pirotécnica": esfera deformada por ruído 3D que corre com
// as chamas) e uma temperatura (mais quente no núcleo, a arrefecer para fora). A cor vem de uma rampa de corpo
// negro (vermelho-escuro → laranja → amarelo → quase branco), emitida e absorvida da frente para trás, por isso
// as chamas têm profundidade real, recortes e núcleo sem ser um disco liso a brilhar.
// Quando arrefece (_Heat → 0) deixa de emitir e passa a fumo que absorve (ou vapor claro, com _Steam = 1).
// Parâmetros por instância (MaterialPropertyBlock):
//   _Heat      0..1   temperatura geral (1 = fogo vivo, 0 = só fumo/vapor)
//   _Fade      0..1   opacidade geral
//   _Seed             desfasa o ruído entre instâncias
//   _Flow      xyz    direção (espaço de objeto) para onde as chamas correm; w = velocidade
//   _Core      xyz    centro do núcleo quente (espaço de objeto); w = quanto a cauda se estica para trás (0..1)
//   _Turb             força da turbulência (recortes das chamas)
// A cor é pré-compensada para o Color Grading da cena (que tira saturação e arrefece): sem isto sai salmão.
Shader "KonoSuba/FireVolumetric"
{
    Properties
    {
        _Intensity ("Intensidade (HDR)", Float) = 2.8
        _Steam ("Vapor claro em vez de fumo escuro", Range(0, 1)) = 0
        _Heat ("Temperatura", Range(0, 1)) = 1
        _Fade ("Opacidade", Range(0, 1)) = 1
        _Seed ("Semente", Float) = 0
        _Flow ("Fluxo (xyz dir, w velocidade)", Vector) = (0, 1, 0, 1)
        _Core ("Núcleo (xyz, w cauda)", Vector) = (0, 0, 0, 0)
        _Turb ("Turbulência", Float) = 1
        _Density ("Densidade", Float) = 14
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One OneMinusSrcAlpha   // pré-multiplicado: emite e tapa (fumo)
            ZWrite Off
            Cull Front                   // desenha as faces de trás: funciona também com a câmara lá dentro

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            float _Intensity, _Steam, _Heat, _Fade, _Seed, _Turb, _Density;
            float4 _Flow, _Core;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 objPos : TEXCOORD0;
                float3 objCam : TEXCOORD1;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.objPos = v.vertex.xyz;
                o.objCam = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz;
                return o;
            }

            // Ruído de valor 3D e fbm (4 oitavas).
            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }
            float Noise(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i + float3(0, 0, 0)), Hash(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }
            float Fbm(float3 p)
            {
                float s = 0.0, a = 0.5;
                [unroll] for (int k = 0; k < 4; k++) { s += a * Noise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return s;
            }

            // Rampa de corpo negro, com a saturação puxada para cima (o grading da cena tira-a).
            float3 FireColor(float t)
            {
                // Pouco vermelho-escuro (só nas bordas mais frias); o corpo do fogo é laranja e o núcleo amarelo.
                float3 c = lerp(float3(0.45, 0.05, 0.0), float3(1.0, 0.30, 0.0), smoothstep(0.0, 0.18, t));
                c = lerp(c, float3(1.0, 0.52, 0.04), smoothstep(0.15, 0.42, t));
                c = lerp(c, float3(1.0, 0.76, 0.22), smoothstep(0.42, 0.72, t));
                c = lerp(c, float3(1.0, 0.93, 0.66), smoothstep(0.75, 1.0, t));
                float l = dot(c, float3(0.299, 0.587, 0.114));
                c = l + (c - l) * 1.3;
                return max(c, 0.0) * (0.45 + 2.4 * t * t);
            }

            // Densidade e temperatura num ponto (espaço de objeto).
            void Sample(float3 p, out float density, out float temp)
            {
                float t = _Time.y * _Flow.w;
                float3 flow = normalize(_Flow.xyz + 1e-5);
                // A cauda: atrás do núcleo (contra o fluxo... a favor dele) a distância conta menos e a forma estica.
                float3 v = p - _Core.xyz;
                float along = dot(v, flow);
                v -= flow * along * (along > 0.0 ? _Core.w * 0.6 : 0.0);
                float r = length(v);

                // Turbulência que corre com as chamas: a forma está sempre a mudar, sem saltos.
                float3 q = p * 3.2 - flow * t * 1.6 + _Seed;
                float n = Fbm(q);
                float n2 = Fbm(q * 2.3 + float3(4.1, 1.7, 9.3) - flow * t * 1.2);
                float edge = 0.30 + (n - 0.5) * 0.30 * _Turb + (n2 - 0.5) * 0.12 * _Turb;
                // Línguas de fogo: mais recortadas para o lado da cauda.
                edge += max(along, 0.0) * 0.35 * _Core.w * (n2 - 0.35);
                float d = edge - r;
                density = saturate(d * _Density);
                // Mais quente no centro e onde a turbulência "acende"; a temperatura geral escala tudo.
                temp = saturate(d / 0.16 + (n - 0.5) * 0.5) * _Heat; // aquece depressa para dentro: corpo laranja, núcleo amarelo
            }

            float4 frag(v2f i) : SV_Target
            {
                // Raio da câmara em espaço de objeto, recortado à esfera de raio 0,5.
                float3 ro = i.objCam;
                float3 rd = normalize(i.objPos - ro);
                float b = dot(ro, rd), c = dot(ro, ro) - 0.25;
                float h = b * b - c;
                if (h <= 0.0) return 0;
                h = sqrt(h);
                float t0 = max(-b - h, 0.0), t1 = -b + h;
                if (t1 <= t0) return 0;

                const int Steps = 40;
                float dt = (t1 - t0) / Steps;
                float jitter = frac(sin(dot(i.pos.xy, float2(12.9898, 78.233))) * 43758.5453);
                float trans = 1.0;
                float3 col = 0;
                // Fumo cinzento-acastanhado (não preto-avermelhado); vapor claro.
                float3 smokeCol = lerp(float3(0.16, 0.14, 0.13), float3(0.72, 0.74, 0.78), _Steam);
                [loop] for (int s = 0; s < Steps; s++)
                {
                    float3 p = ro + rd * (t0 + (s + jitter) * dt);
                    // Nada abaixo do chão (y = 0 em mundo): o fogo esmaga-se contra a água.
                    float wy = mul(unity_ObjectToWorld, float4(p, 1)).y;
                    if (wy < 0.0) continue;
                    float dens, temp;
                    Sample(p, dens, temp);
                    if (dens <= 0.001) continue;
                    float smoke = 1.0 - smoothstep(0.04, 0.22, temp);
                    float sigma = dens * lerp(2.5, 5.0, smoke) * _Fade;
                    float a = 1.0 - exp(-sigma * dt * 6.0);
                    float3 emit = FireColor(temp) * _Intensity * (1.0 - _Steam);
                    float3 c2 = lerp(emit, smokeCol, smoke);
                    col += trans * a * c2;
                    trans *= 1.0 - a;
                    if (trans < 0.01) break;
                }
                float alpha = (1.0 - trans);
                col *= _Fade;
                return float4(any(isnan(col)) ? 0 : max(col, 0), saturate(alpha * _Fade));
            }
            ENDCG
        }
    }
}
