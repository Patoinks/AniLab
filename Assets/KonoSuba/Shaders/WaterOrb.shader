// Água em volume (semi-realista), igual no jato e na bola:
//   refração do que está atrás (_CameraOpaqueTexture do URP), cor que aumenta com a espessura atravessada,
//   reflexo do ambiente com fresnel de Schlick (água: F0 ≈ 0.02) e brilhos especulares nítidos.
// O movimento da água vê-se pelas normais (ondulação fina animada), não por deformar a forma:
//   _Swirl = 1 (bola): a ondulação gira à volta do centro, como água a rodar por dentro;
//   _Swirl = 0 (jato): a ondulação corre ao longo do jato (UV: u à volta, v ao comprido).
// _Jiggle (0..1, posto pelo script): a bola abana quando a água entra e depois acalma.
// _Impacts[i] = (direção local do ponto de entrada, idade em s), posto pelo script: cada bolinha que se funde faz
//   uma pequena bossa e um anel de onda que corre pela superfície da bola e se apaga.
// _Glow (posto pelo script): brilho interno da água comprimida, mais forte no centro.
Shader "KonoSuba/WaterOrb"
{
    Properties
    {
        _Tint ("Cor da água (absorção)", Color) = (0.55, 0.66, 0.74, 1)
        _Absorb ("Absorção na zona mais espessa", Range(0, 1)) = 0.45
        _Refraction ("Refração", Range(0, 0.2)) = 0.09
        _SkyColor ("Reflexo de cima", Color) = (0.42, 0.46, 0.55, 1)
        _GroundColor ("Reflexo de baixo (chão molhado)", Color) = (0.30, 0.33, 0.39, 1)
        _FresnelScale ("Força do reflexo (fresnel)", Range(0, 1)) = 1
        _SpecIntensity ("Brilho especular", Float) = 3
        _SpecPower ("Nitidez do brilho", Float) = 420
        _Ripple ("Ondulação fina (normais)", Range(0, 1)) = 0.35
        _RippleScale ("Escala da ondulação", Float) = 7
        _RippleSpeed ("Velocidade da ondulação", Float) = 2.2
        _Swirl ("Rodar por dentro (bola)", Float) = 0
        _SwirlSpeed ("Velocidade da rotação", Float) = 2.5
        _Jiggle ("Balanço da forma", Range(0, 1)) = 0
        _Glow ("Brilho interno (comprimida)", Range(0, 4)) = 0
        _GlowColor ("Cor do brilho", Color) = (0.62, 0.86, 1, 1)
        _Aura ("Aura de mana (contorno)", Range(0, 2)) = 0
        _AuraColor ("Cor da aura", Color) = (0.35, 0.6, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _CameraOpaqueTexture; // URP: cena opaca por trás (substitui o GrabPass)
            fixed4 _Tint, _SkyColor, _GroundColor, _GlowColor, _AuraColor;
            float _Absorb, _Refraction, _SpecIntensity, _SpecPower, _FresnelScale;
            #define ORB_MAX_IMPACTS 16
            float4 _Impacts[ORB_MAX_IMPACTS];
            static const float ImpactLife = 0.9;
            static const float ImpactFreq = 9.0;   // ondas por radiano
            static const float ImpactSpeed = 2.6;  // radianos por segundo

            // Altura da onda num ponto da esfera (direção pd) e a sua inclinação, para as normais.
            void ImpactWaves(float3 pd, out float height, out float3 slope)
            {
                height = 0;
                slope = 0;
                [loop] for (int k = 0; k < ORB_MAX_IMPACTS; k++)
                {
                    float4 im = _Impacts[k];
                    float age = im.w;
                    if (age <= 0.0 || age > ImpactLife) continue;
                    float c = clamp(dot(pd, im.xyz), -1.0, 1.0);
                    float ang = acos(c);
                    float x = ang - age * ImpactSpeed;
                    float fade = exp(-age * 4.5);
                    float env = exp(-x * x * 10.0) * fade;
                    // Bossa onde a bolinha se funde (só no início) + anel que se afasta.
                    float dent = exp(-ang * ang * 60.0) * exp(-age * 12.0) * 1.4;
                    height += cos(x * ImpactFreq) * env + dent;
                    float dh = -ImpactFreq * sin(x * ImpactFreq) * env;
                    float3 away = pd * c - im.xyz; // tangente que se afasta do ponto de entrada
                    float al = length(away);
                    if (al > 1e-4) slope += away / al * dh;
                }
            }

            float _Ripple, _RippleScale, _RippleSpeed, _Swirl, _SwirlSpeed, _Jiggle, _Glow, _Aura;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 objPos : TEXCOORD3;
                float4 grabPos : TEXCOORD4;
                UNITY_FOG_COORDS(5)
            };

            v2f vert(appdata_base v)
            {
                // Balanço de baixa frequência (só quando _Jiggle > 0): a gota estica e encolhe em vários eixos.
                float t = _Time.y;
                float3 p = v.vertex.xyz;
                float3 d = normalize(p + 1e-5);
                float wob = sin(t * 13.0) * (d.y * d.y - 0.33) * 0.6
                          + sin(t * 9.7 + 1.3) * (d.x * d.z) * 0.8
                          + sin(t * 11.3 + 2.1) * (d.x * d.x - d.z * d.z) * 0.5;
                v.vertex.xyz += v.normal * wob * 0.08 * _Jiggle;
                float ih; float3 islope;
                ImpactWaves(d, ih, islope);
                v.vertex.xyz += v.normal * ih * 0.012;

                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = v.texcoord.xy;
                o.objPos = p;
                o.grabPos = ComputeScreenPos(o.pos);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            // Campo de ondulação fina (soma de ondas em 3D); devolve uma perturbação para a normal.
            float3 RippleField(float3 q, float t)
            {
                float3 g = 0;
                g += cos(dot(q, float3(1.0, 0.6, 0.3)) * 1.0 + t * 1.1) * float3(1.0, 0.6, 0.3);
                g += cos(dot(q, float3(-0.4, 1.0, 0.7)) * 1.7 + t * 1.6) * float3(-0.4, 1.0, 0.7) * 0.6;
                g += cos(dot(q, float3(0.8, -0.3, 1.0)) * 2.9 + t * 2.3) * float3(0.8, -0.3, 1.0) * 0.35;
                g += cos(dot(q, float3(-0.9, -0.7, 0.2)) * 4.6 + t * 3.1) * float3(-0.9, -0.7, 0.2) * 0.2;
                return g;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y * _RippleSpeed;
                float3 q;
                if (_Swirl > 0.5)
                {
                    // Bola: o campo roda à volta do eixo vertical e torce com a altura.
                    float3 p = normalize(i.objPos);
                    float a = _Time.y * _SwirlSpeed + p.y * 1.5;
                    float s = sin(a), c = cos(a);
                    q = float3(c * p.x - s * p.z, p.y, s * p.x + c * p.z) * _RippleScale;
                }
                else
                {
                    // Jato: corre ao longo do comprimento.
                    q = float3(sin(i.uv.x * 6.2831), cos(i.uv.x * 6.2831), i.uv.y * 3.0 - _Time.y * 4.0) * _RippleScale * 0.5;
                }

                float3 n = normalize(i.normal);
                float ih; float3 islope;
                ImpactWaves(normalize(i.objPos), ih, islope);
                float3 ws = mul((float3x3)unity_ObjectToWorld, islope) / max(length(unity_ObjectToWorld._m00_m10_m20), 1e-4);
                n = normalize(n - ws * 0.12);
                float3 r = RippleField(q, t);
                n = normalize(n + (r - n * dot(r, n)) * _Ripple * 0.25);

                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float ndv = saturate(dot(n, v));

                // Refração: o fundo deslocado pela normal (mais no centro, onde a água é mais espessa).
                float3 nView = mul((float3x3)UNITY_MATRIX_V, n);
                float2 guv = i.grabPos.xy / i.grabPos.w - nView.xy * _Refraction * ndv;
                float3 behind = tex2D(_CameraOpaqueTexture, guv).rgb;
                float thickness = ndv; // aproximação: no centro atravessa mais água
                float3 transmitted = behind * lerp(float3(1, 1, 1), _Tint.rgb, _Absorb * thickness);

                // Reflexo: ambiente aproximado (céu escuro em cima, chão em baixo) com fresnel de Schlick.
                float3 refl = reflect(-v, n);
                float3 env = lerp(_GroundColor.rgb, _SkyColor.rgb, smoothstep(-0.2, 0.4, refl.y));
                float fresnel = (0.02 + 0.98 * pow(1.0 - ndv, 5.0)) * _FresnelScale;

                // Brilhos: o foco por cima do centro da cena e uma luz de recorte mais fraca.
                float3 l1 = normalize(float3(-0.15, 1.0, 0.1));
                float3 l2 = normalize(float3(0.7, 0.35, -0.6));
                float spec = pow(saturate(dot(refl, l1)), _SpecPower) * _SpecIntensity
                           + pow(saturate(dot(refl, l2)), _SpecPower * 0.5) * _SpecIntensity * 0.35;

                fixed4 surface = fixed4(env * fresnel + spec, 1);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, surface, fixed4(0, 0, 0, 0));
                // Água comprimida: brilha por dentro, mais no centro (onde é mais espessa), com a ondulação a mexer no brilho.
                float3 glow = _GlowColor.rgb * _Glow * (0.3 + 0.7 * ndv * ndv) * (0.85 + 0.15 * r.x);
                // Aura de mana: contorno azul que tremeluz e corre com a ondulação (a água está carregada de mana).
                float rim = pow(1.0 - ndv, 2.5);
                float auraFlicker = 0.75 + 0.25 * sin(_Time.y * 6.0 + r.y * 2.0);
                float3 aura = _AuraColor.rgb * _Aura * (rim * 0.9 + 0.08) * auraFlicker;
                return fixed4(transmitted * (1.0 - fresnel) + surface.rgb + glow + aura, 1);
            }
            ENDCG
        }
    }
}
