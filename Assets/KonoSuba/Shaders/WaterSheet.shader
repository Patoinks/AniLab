// Água do impacto (WaterImpactSurface): a mesma leitura da bola (refração, fresnel de Schlick, brilhos nítidos),
// mas guiada pela espessura real de água em cada vértice (cor .r, em metros):
//   - a cor e a refração crescem com a espessura (absorção exp(-k·espessura));
//   - um lençol muito fino quase desaparece — a água some a afinar-se, não a partir-se em gotas.
// Ar na água (cor .g, 0..1): onde a água vai depressa fica branca e leitosa (espuma/bolhas), em manchas
// e estrias que correm com o escoamento; ao abrandar volta a ficar transparente.
// Vê-se dos dois lados (o lençol dobra-se à volta do corpo).
Shader "KonoSuba/WaterSheet"
{
    Properties
    {
        _Tint ("Cor da água (absorção)", Color) = (0.55, 0.66, 0.74, 1)
        _Absorb ("Absorção por metro", Float) = 1.5
        _Refraction ("Refração", Range(0, 0.2)) = 0.08
        _SkyColor ("Reflexo de cima", Color) = (0.58, 0.63, 0.71, 1)
        _GroundColor ("Reflexo de baixo (chão molhado)", Color) = (0.40, 0.44, 0.51, 1)
        _SpecIntensity ("Brilho especular", Float) = 3
        _SpecPower ("Nitidez do brilho", Float) = 380
        _Ripple ("Ondulação fina (normais)", Range(0, 1)) = 0.25
        _VisibleThickness ("Espessura a partir da qual se vê inteira (m)", Float) = 0.015
        _FoamColor ("Cor da água com ar", Color) = (0.86, 0.9, 0.95, 1)
        _Foam ("Força do ar na água", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _CameraOpaqueTexture; // URP: cena opaca por trás (substitui o GrabPass)
            fixed4 _Tint, _SkyColor, _GroundColor, _FoamColor;
            float _Absorb, _Refraction, _SpecIntensity, _SpecPower, _Ripple, _VisibleThickness, _Foam;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float4 grabPos : TEXCOORD2;
                float thickness : TEXCOORD3;
                float aeration : TEXCOORD4;
            };

            v2f vert(appdata_full v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.grabPos = ComputeScreenPos(o.pos);
                o.thickness = v.color.r;
                o.aeration = v.color.g;
                return o;
            }

            // Ruído suave 3D (valor), para as manchas de ar não ficarem às riscas regulares.
            float Hash3(float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }
            float Noise3(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(lerp(Hash3(i), Hash3(i + float3(1, 0, 0)), f.x), lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y);
                float b = lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x), lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y);
                return lerp(a, b, f.z);
            }

            fixed4 frag(v2f i, fixed facing : VFACE) : SV_Target
            {
                float3 n = normalize(i.normal) * (facing > 0 ? 1.0 : -1.0);
                // Ondulação fina a correr pela superfície (a água está a escoar).
                float3 q = i.worldPos * 6.0;
                float t = _Time.y * 3.0;
                float3 rip = float3(sin(q.y * 1.3 + t + q.z), sin(q.z * 1.7 - t * 1.2 + q.x), sin(q.x * 1.1 + t * 0.8 + q.y));
                n = normalize(n + (rip - n * dot(rip, n)) * _Ripple * 0.2);

                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float ndv = saturate(dot(n, v));
                float th = max(i.thickness, 0.0);
                float visible = smoothstep(0.0015, _VisibleThickness, th);

                float3 nView = mul((float3x3)UNITY_MATRIX_V, n);
                float2 guv = i.grabPos.xy / i.grabPos.w - nView.xy * _Refraction * visible;
                float3 behind = tex2D(_CameraOpaqueTexture, guv).rgb;
                // Atravessa mais água quando se olha de lado para o lençol.
                float path = min(th / max(ndv, 0.25), 0.25);
                float3 transmitted = behind * lerp(float3(1, 1, 1), _Tint.rgb, (1.0 - exp(-_Absorb * path)) * 0.6); // igual à bola

                float3 refl = reflect(-v, n);
                float3 env = lerp(_GroundColor.rgb, _SkyColor.rgb, smoothstep(-0.2, 0.4, refl.y));
                float fresnel = (0.02 + 0.98 * pow(1.0 - ndv, 5.0)) * visible;
                float3 l1 = normalize(float3(-0.15, 1.0, 0.1));
                float3 l2 = normalize(float3(0.7, 0.35, -0.6));
                float spec = (pow(saturate(dot(refl, l1)), _SpecPower) * _SpecIntensity
                            + pow(saturate(dot(refl, l2)), _SpecPower * 0.5) * _SpecIntensity * 0.35) * visible;

                float3 col = lerp(behind, transmitted * (1.0 - fresnel) + env * fresnel, visible) + spec;

                // Água com ar: difusa e clara, em estrias que correm com a água (não uma camada uniforme).
                float3 fq = i.worldPos * 5.0 + float3(0, t * 0.5, 0);
                float streaks = Noise3(fq) * 0.6 + Noise3(fq * 2.7 + 11.3) * 0.4;
                float foam = saturate(i.aeration) * smoothstep(0.45, 0.9, streaks * 0.7 + i.aeration * 0.4) * _Foam * visible;
                float lightUp = 0.6 + 0.4 * saturate(n.y * 0.5 + 0.5);
                col = lerp(col, _FoamColor.rgb * lightUp, foam);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
