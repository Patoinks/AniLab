// Bola de Fogo em voo: um único volume integrado por raymarching que contém a bola e o rasto.
// A forma vem de um campo de distância (esfera + cauda afilada) deslocado por ruído 3D advectado
// para trás e para cima, por isso as chamas escorrem da bola e partem-se em pedaços sozinhas.
// A malha (esfera de raio 0.5 esticada) é só o limite do volume.
Shader "KonoSuba/FireBallFlight"
{
    Properties
    {
        _FireTime ("Tempo do efeito", Float) = 0
        _Radius ("Raio da bola (m)", Float) = 0.55
        _Tail ("Comprimento do rasto (m)", Float) = 3
        _Lift ("Subida da ponta do rasto (m)", Float) = 0.3
        _Flow ("Escoamento para trás", Float) = 6
        _Seed ("Variação", Float) = 0
        _Intensity ("Intensidade", Float) = 1.6
        _Fade ("Formação", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+6" "RenderType"="Transparent" "IgnoreProjector"="True" }
        // Faces de saída e sem teste de profundidade: o recorte contra a cena é feito no fragmento,
        // o que também funciona com a câmara dentro do volume.
        Cull Front ZWrite Off ZTest Always
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float _FireTime, _Radius, _Tail, _Lift, _Flow, _Seed, _Intensity, _Fade;
            float4x4 _WorldToBall;   // só rotação e translação: espaço da bola em metros, +z = sentido do voo
            float4 _UpBall;          // vertical do mundo no espaço da bola

            struct Varyings { float4 pos : SV_POSITION; float3 local : TEXCOORD0; float4 screen : TEXCOORD1; };

            Varyings Vert(appdata_base v)
            {
                Varyings o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.local = v.vertex.xyz;
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

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
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1,0,0)), f.x),
                                 lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x),
                                 lerp(Hash(i + float3(0,1,1)), Hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            // Rodar entre oitavas tira o alinhamento com a grelha do ruído.
            static const float3x3 OctaveTurn = float3x3(0.00, 0.80, 0.60, -0.80, 0.36, -0.48, -0.60, -0.48, 0.64);

            float Fbm(float3 p)
            {
                float sum = 0.0, amp = 0.5;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    sum += amp * Noise(p);
                    p = mul(OctaveTurn, p) * 2.03;
                    amp *= 0.5;
                }
                return sum / 0.9375;
            }

            float SMin(float a, float b, float k)
            {
                float h = saturate(0.5 + 0.5 * (b - a) / k);
                return lerp(b, a, h) - k * h * (1.0 - h);
            }

            // Corpo negro aproximado: o vermelho acende primeiro, depois o verde e por fim o azul.
            float3 Blackbody(float t)
            {
                float3 c = float3(pow(t, 1.1), pow(t, 2.6), pow(t, 7.0));
                return c * lerp(0.8, 7.5, t * t);
            }

            float Density(float3 q, out float heat)
            {
                heat = 0.0;
                float R = max(_Radius * _Fade, 0.01);
                float L = max(_Tail * _Fade, 0.01);
                float3 up = _UpBall.xyz;
                float back = saturate(-q.z / L);
                // O gás quente sobe: a ponta do rasto curva para cima.
                float3 b = q - up * (_Lift * back * back);

                float ball = length(b * float3(1.0, 1.0, 0.84)) - R;
                float zc = clamp(b.z, -L, 0.0);
                float tailRadius = R * 0.78 * pow(saturate(1.0 + zc / L), 0.85);
                float tail = length(float3(b.xy, b.z - zc)) - tailRadius;
                float shape = SMin(ball, tail, R * 0.55);
                if (shape > R * 0.95) return 0.0;

                // Ruído no espaço da bola: escoa para trás (n.z cresce com o tempo) e ferve para cima.
                float3 n = b * (2.3 / max(_Radius, 0.1));
                n.z += _FireTime * _Flow;
                n -= up * (_FireTime * 1.7);
                n += _Seed * float3(1.71, 9.23, 3.17);
                float3 warp = float3(Noise(n * 0.5 + 11.3), Noise(n * 0.5 + 23.9), Noise(n * 0.5 + 37.1)) - 0.5;
                float f = Fbm(n + warp * 1.9);

                // Frente mais firme, cauda mais rasgada.
                float amplitude = R * lerp(0.5, 1.15, back);
                float d = shape - (f - 0.45) * amplitude;
                float density = saturate(-d / (R * 0.16));

                // Ao longo do rasto abrem-se falhas cada vez maiores: a cauda parte-se em pedaços soltos.
                float chunk = Noise(float3(n.xy * 0.55, n.z * 0.8) + 51.7);
                density *= smoothstep(back * 0.78 - 0.08, back * 0.78 + 0.16, chunk);

                float inner = saturate(-d / (R * 0.85));
                heat = saturate(0.22 + inner * 0.9 + (f - 0.5) * 0.55 - back * 0.42);
                return density;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float3 ro = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz;
                float3 rd = normalize(i.local - ro);
                float b = dot(ro, rd), h = b * b - dot(ro, ro) + 0.25;
                if (h <= 0.0 || _Fade < 0.001) discard;
                h = sqrt(h);
                float start = max(0.0, -b - h), end = -b + h;

                // Recorte contra a profundidade opaca com a mesma projeção que desenha a malha
                // (funciona também na câmara do reflexo, que usa uma projeção oblíqua).
                float2 uv = i.screen.xy / i.screen.w;
                float sceneDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
                #if !defined(UNITY_REVERSED_Z)
                    sceneDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, sceneDepth);
                #endif
                float4 clipOrigin = UnityObjectToClipPos(float4(ro, 1));
                float4 clipStep = UnityObjectToClipPos(float4(ro + rd, 1)) - clipOrigin;
                float denominator = clipStep.z - sceneDepth * clipStep.w;
                if (abs(denominator) > 1e-6)
                {
                    float sceneDistance = (sceneDepth * clipOrigin.w - clipOrigin.z) / denominator;
                    if (sceneDistance >= 0.0) end = min(end, sceneDistance);
                }
                if (end <= start) discard;

                // O mesmo raio no espaço da bola: o parâmetro t é partilhado, só muda a métrica.
                float3 roB = mul(_WorldToBall, mul(unity_ObjectToWorld, float4(ro, 1))).xyz;
                float3 rdB = mul((float3x3)_WorldToBall, mul((float3x3)unity_ObjectToWorld, rd));
                float metres = length(rdB);

                const int steps = 56;
                float dt = (end - start) / steps;
                float ds = dt * metres;
                // Desvio fixo por pixel (não temporal) para desfazer as bandas entre passos.
                float jitter = frac(52.9829189 * frac(dot(i.pos.xy, float2(0.06711056, 0.00583715))));
                float3 color = 0.0;
                float transmission = 1.0;
                [loop] for (int k = 0; k < steps; k++)
                {
                    float3 q = roB + rdB * (start + (k + jitter) * dt);
                    float heat;
                    float density = Density(q, heat);
                    if (density < 0.002) continue;
                    // Emissão proporcional à absorção: onde o fogo é espesso vê-se a sua "pele"
                    // com a temperatura local; nas franjas finas fica translúcido.
                    float a = 1.0 - exp(-density * 10.0 * ds);
                    color += transmission * Blackbody(heat) * _Intensity * a;
                    transmission *= 1.0 - a;
                    if (transmission < 0.01) break;
                }
                return float4(color, (1.0 - transmission) * 0.9);
            }
            ENDCG
        }
    }
    Fallback Off
}
