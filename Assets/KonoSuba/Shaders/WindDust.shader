// Poeira e brilhos do vento (WindTornado, bola de vento): cada partícula são dois planos 3D cruzados ao longo do
// movimento, com um traço suave (cabeça brilhante, cauda de cometa, bordas a desvanecer) — sem arestas
// geométricas. Animada toda no vértice; ondula ao acaso e cintila.
// _Shape 0 (tornado): orbita a ampulheta (mais depressa na cintura), sobe e dá a volta.
// _Shape 1 (esfera): cada partícula orbita num plano inclinado próprio à volta do centro, a _Radius;
//   _Gather (0..1) puxa-as de longe para a esfera (formação da bola).
// Malha: vertex = canto unitário (x ao longo, y em pé, z radial); uv = (altura inicial 0..1, ângulo inicial 0..1);
// uv2 = (escala do raio, velocidade); color = (fase, tamanho, tipo 0 ar / 1 chão, brilho).
Shader "KonoSuba/WindDust"
{
    Properties
    {
        _Intensity ("Intensidade", Float) = 6
        _Size ("Tamanho (m)", Float) = 0.03
        _Trail ("Esticão do rasto", Float) = 6
        _Rise ("Subida (voltas da altura/s)", Float) = 0.08
        _Spin ("Rotação (rad·m/s)", Float) = 3.5
        _Shape ("Forma (0 tornado, 1 esfera)", Float) = 0
        _Radius ("Raio da esfera (m)", Float) = 0.55
        _Gather ("Formação (0 longe, 1 esfera)", Range(0, 1)) = 1
        _Sway ("Chicote da coluna", Float) = 1
        _Fade ("Desvanecer", Range(0, 1)) = 1
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

            float _Intensity, _Size, _Trail, _Rise, _Spin, _Shape, _Radius, _Gather, _Sway, _Fade;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float4 col : COLOR; float2 q : TEXCOORD0; };

            // Roda p à volta de X (tilt) e depois de Y (yaw).
            float3 Orient(float3 p, float tilt, float yaw)
            {
                float st = sin(tilt), ct = cos(tilt), sy = sin(yaw), cy = cos(yaw);
                p = float3(p.x, p.y * ct - p.z * st, p.y * st + p.z * ct);
                return float3(p.x * cy + p.z * sy, p.y, -p.x * sy + p.z * cy);
            }

            v2f vert(appdata v)
            {
                v2f o;
                float t = _Time.y, ph = v.color.r * 6.2832;
                bool ground = v.color.b > 0.5;
                float speed = v.uv2.y;
                float3 centre, tan3, up, rad3;
                float h, life;
                if (_Shape > 0.5)
                {
                    // Esfera: órbita num plano inclinado próprio; de longe para perto durante a formação.
                    float r = _Radius * (0.75 + 0.6 * frac(v.uv2.x * 3.7)) * lerp(3.5, 1.0, _Gather);
                    r += sin(t * 2.1 + ph * 3.0) * 0.05 * _Radius;
                    float ang = v.uv.y * 6.2832 + t * _Spin * 1.6 * speed / max(r, 0.2);
                    float tilt = (v.uv.x - 0.5) * 3.1416, yaw = ph;
                    centre = Orient(float3(cos(ang), 0, sin(ang)), tilt, yaw) * r;
                    tan3 = Orient(float3(sin(ang), 0, -cos(ang)), tilt, yaw);
                    rad3 = normalize(centre);
                    up = normalize(cross(tan3, rad3));
                    h = centre.y;
                    life = 1.0;
                }
                else
                {
                    // Tornado: sobe e dá a volta (no chão fica rasteira e sobe pouco).
                    float cycle = frac(v.uv.x + t * _Rise * speed);
                    h = ground ? 0.05 + cycle * 0.5 : cycle * 5.4;
                    float r = ground ? lerp(0.6, 3.2, v.uv2.x) : WindRadius(h) * v.uv2.x;
                    // Erraticidade: o raio e a altura tremem com ruído próprio de cada partícula.
                    r += (sin(t * 2.1 + ph * 3.0) * 0.5 + sin(t * 5.3 + ph * 7.0) * 0.5) * 0.12 * (ground ? 1.5 : 1.0);
                    h += sin(t * 3.7 + ph * 5.0) * 0.08;
                    float ang = v.uv.y * 6.2832 + t * _Spin * speed / max(r, 0.3) + sin(t * 1.3 + ph) * 0.3;
                    float2 radial = float2(cos(ang), sin(ang));
                    float2 tangent = float2(-radial.y, radial.x);
                    centre = float3(radial.x * r, h, radial.y * r);
                    tan3 = normalize(float3(-tangent.x, 0.25 + 0.2 * sin(ph), -tangent.y));
                    up = float3(0, 1, 0);
                    rad3 = float3(radial.x, 0, radial.y);
                    life = sin(3.1416 * cycle);
                }

                // Dois planos cruzados esticados ao longo da tangente (rasto), tamanho e comprimento próprios.
                float size = _Size * (0.5 + v.color.g) * (ground ? 1.4 : 1.0);
                float trailLen = _Trail * (0.5 + frac(v.color.r * 13.7));
                float3 local = tan3 * v.vertex.x * trailLen + up * v.vertex.y + rad3 * v.vertex.z;
                o.q = float2(v.vertex.x, v.vertex.y + v.vertex.z);
                float3 obj = centre + local * size;
                // Chicote da coluna em espaço de mundo (igual aos traços, seja qual for a rotação do objeto).
                float3 wp = mul(unity_ObjectToWorld, float4(obj, 1)).xyz + WindSway(h, t) * _Sway;
                o.pos = mul(UNITY_MATRIX_VP, float4(wp, 1));

                // Cintilar + entrar/sair suave no ciclo da subida.
                float tw = pow(0.5 + 0.5 * sin(t * (3.0 + 7.0 * v.color.r) + ph * 13.0), 5.0);
                float bright = (0.25 + 1.6 * tw) * life * v.color.a * (0.75 + 0.5 * _WindGust) * (ground ? 0.45 : 1.0);
                float heat = 0.45 + tw * 0.6 + (ground ? -0.15 : 0.0);
                o.col = float4(WindRamp(heat) * bright * _Intensity * _Fade, 0);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // Traço suave: gaussiana através, cauda que cresce para a cabeça (x = 1) e cabeça arredondada.
                float across = exp(-i.q.y * i.q.y * 5.0);
                float tail = pow(saturate((i.q.x + 1.0) * 0.5), 2.2);
                float head = smoothstep(1.0, 0.7, i.q.x);
                return i.col * (across * tail * head);
            }
            ENDCG
        }
    }
}
