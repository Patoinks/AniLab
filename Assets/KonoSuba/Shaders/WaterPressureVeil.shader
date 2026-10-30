// Manga de água pulverizada a alta pressão (atrás do alvo, quando a bala comprimida sai pelas costas):
// um cone translúcido, contínuo e suave — spray denso lido como superfície, nunca como gotinhas.
// Mais visível nas bordas (vê-se de lado mais água), com ruído que corre ao longo do cone e se apaga com _Fade.
Shader "KonoSuba/WaterPressureVeil"
{
    Properties
    {
        _Color ("Cor", Color) = (0.78, 0.86, 0.95, 1)
        _Fade ("Visibilidade", Range(0, 1)) = 1
        _Speed ("Velocidade do ruído", Float) = 6
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Fade, _Speed;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                // Névoa de spray, não casca: mais densa onde se olha através do cone, a sumir no contorno
                // (assim não se vê a forma geométrica do cone).
                float facing = abs(dot(normalize(i.normal), v));
                float soft = smoothstep(0.05, 0.6, facing);
                // u à volta do cone, v ao longo (0 = saída do alvo, 1 = ponta).
                float2 q = float2(i.uv.x * 10.0, i.uv.y * 4.0 - _Time.y * _Speed);
                float n = Noise(q) * 0.6 + Noise(q * 2.3 + 3.1) * 0.4;
                float along = smoothstep(0.0, 0.12, i.uv.y) * (1.0 - smoothstep(0.55, 1.0, i.uv.y));
                float breakup = smoothstep(0.25, 0.75, n); // falhas: o spray não é uma folha uniforme
                float a = 0.32 * soft * breakup * along * _Fade;
                return fixed4(_Color.rgb * (0.9 + 0.3 * n), saturate(a));
            }
            ENDCG
        }
    }
}
