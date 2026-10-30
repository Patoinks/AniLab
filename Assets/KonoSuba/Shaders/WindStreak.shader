// Traços do tornado de vento (WindTornado): aditivo, estrias finas a correr ao longo da fita.
// uv.x = através da fita, uv.y = ao longo; vertex color r = fase aleatória, g = brilho do traço.
// _Mode: 0 fita (traço/faísca), 1 casca (uv.x à volta, estrias em espiral com _Shear, ténue de lado),
// 2 disco no chão (uv.x à volta, uv.y radial).
// Varrimento (_Window > 0): só um rasto de comprimento _Window está aceso e percorre a fita _Rate vezes/s
// (cada fita com o seu ritmo pela fase); _Base é o que fica aceso fora do rasto.
// Vida: no vértice a coluna chicoteia (WindSway) e cada fita ondula com ruído; a cor vem de WindRamp pela
// intensidade, pela altura e por fita; o lado de trás do tornado fica mais escuro e azul (volume).
Shader "KonoSuba/WindStreak"
{
    Properties
    {
        _Intensity ("Intensidade", Float) = 2.5
        _Lines ("Estrias através", Float) = 6
        _Stretch ("Comprimento das estrias", Float) = 2.5
        _Speed ("Velocidade ao longo", Float) = 1.6
        _Mode ("Modo (0 fita, 1 casca, 2 disco)", Float) = 0
        _Shear ("Espiral (casca)", Float) = 2.5
        _Window ("Comprimento do rasto (0 = sem varrimento)", Float) = 0
        _Rate ("Varrimentos por segundo", Float) = 0.3
        _Base ("Brilho fora do rasto", Range(0, 1)) = 0.25
        _Solid ("Sem estrias (0/1)", Range(0, 1)) = 0
        _Wobble ("Ondulação (m)", Float) = 0.12
        _Heat ("Desvio da rampa", Float) = 0
        _Sway ("Chicote da coluna", Float) = 1
        _Fade ("Desvanecer", Range(0, 1)) = 1
        _Head ("Cabeça do rasto fixa (-1 = automática)", Float) = -1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "WindCommon.cginc"

            float _Intensity, _Lines, _Stretch, _Speed, _Mode, _Shear, _Window, _Rate, _Base, _Solid, _Wobble, _Heat, _Sway, _Fade, _Head;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float3 normal : NORMAL; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float facing : TEXCOORD1;
                float2 shape : TEXCOORD2; // x = altura normalizada, y = 0 trás … 1 frente
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 origin = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float h = wp.y - origin.y, t = _Time.y, ph = v.color.r * 37.0;
                float2 radial = wp.xz - origin.xz;
                float rl = max(length(radial), 1e-3);
                radial /= rl;
                // Ondulação própria da fita (radial e vertical), mais forte no ar do que no chão.
                float lift = saturate(h / 0.6);
                float n = sin(h * 3.1 + t * 2.7 + ph) * 0.6 + sin(h * 7.3 - t * 4.1 + ph * 1.7) * 0.4;
                wp.xz += radial * n * _Wobble * lift;
                wp.y += sin(t * 3.3 + ph + rl * 2.0) * _Wobble * 0.5 * lift;
                wp += WindSway(h, t) * _Sway;

                o.pos = mul(UNITY_MATRIX_VP, float4(wp, 1));
                o.uv = v.uv;
                o.color = v.color;
                float3 n3 = UnityObjectToWorldNormal(v.normal);
                float3 toCam = normalize(_WorldSpaceCameraPos - wp);
                o.facing = abs(dot(n3, toCam));
                float3 axisToCam = _WorldSpaceCameraPos - (origin + float3(0, h, 0));
                axisToCam.y = 0;
                o.shape = float2(saturate(h / 5.5), dot(radial, normalize(axisToCam.xz)) * 0.5 + 0.5);
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            float4 frag(v2f i) : SV_Target
            {
                float phase = i.color.r * 37.0;
                float t = _Time.y * _Speed;
                float across = i.uv.x, along = i.uv.y;
                float edge, ends;
                float2 p;
                if (_Mode > 1.5)
                {
                    // Disco: estrias em espiral à volta, mais fortes a meio do raio.
                    p = float2((across + along * _Shear * 0.3) * _Lines * 6.0, along * _Stretch * 2.0 - t * 0.5);
                    edge = 1.0;
                    ends = smoothstep(0.0, 0.15, along) * smoothstep(1.0, 0.45, along);
                }
                else if (_Mode > 0.5)
                {
                    // Casca: estrias em espiral à volta; ténue e mais fraca de lado (silhueta suave).
                    p = float2((across + along * _Shear) * _Lines * 4.0, along * _Stretch - t);
                    edge = i.facing * i.facing;
                    ends = smoothstep(0.0, 0.08, along) * smoothstep(1.0, 0.75, along);
                }
                else
                {
                    p = float2(across * _Lines + phase, along * _Stretch * 4.0 - t + phase);
                    // Centro da fita mais quente que as bordas (degradê através).
                    edge = 1.0 - pow(abs(across * 2.0 - 1.0), 1.6);
                    ends = smoothstep(0.0, 0.12, along) * smoothstep(1.0, 0.7, along);
                }
                // Estrias: ruído muito esticado ao longo, apertado através; duas escalas.
                float s = Noise(p) * 0.65 + Noise(p * float2(2.3, 1.7) + 4.1) * 0.35;
                s = lerp(smoothstep(0.3, 0.95, s), 1.0, _Solid);
                float bright = s * edge * ends * i.color.g;
                float trail = 0.0;
                if (_Window > 0.0 && _Mode < 0.5)
                {
                    // Rasto: cabeça nítida em c, cauda a apagar para trás; sai inteiro pelas pontas.
                    float rate = _Rate * (0.7 + 0.6 * frac(i.color.r * 7.13));
                    // _Head >= 0: progresso imposto pelo script (rajadas da bola de vento a entrar).
                    float progress = _Head >= 0.0 ? _Head : frac(_Time.y * rate + i.color.r);
                    float head = progress * (1.0 + _Window * 2.0) - _Window * 0.5;
                    float x = (head - along) / _Window;
                    trail = smoothstep(-0.08, 0.0, x) * pow(saturate(1.0 - x), 2.0);
                    bright *= lerp(_Base, 1.0, trail) * (1.0 + trail * 0.8);
                }
                // Volume: o lado de trás escurece e puxa para azul; rajada global faz respirar o brilho.
                float front = i.shape.y;
                bright *= lerp(0.3, 1.15, front) * (0.8 + 0.4 * _WindGust);
                float heat = bright * 0.9 + trail * 0.35 + _Heat
                           + (i.shape.x - 0.5) * 0.2 + (frac(i.color.r * 3.7) - 0.5) * 0.25 - (1.0 - front) * 0.25;
                float3 c = WindRamp(heat) * bright * _Intensity * _Fade;
                return float4(c, 0);
            }
            ENDCG
        }
    }
}
