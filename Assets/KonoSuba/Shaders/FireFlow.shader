// Fogo a fluir numa malha 3D (material reutilizável para ataques de fogo).
// A malha traz uv.x à volta e uv.y ao longo; o fogo são listras compridas no sentido do fluxo (ruído
// esticado), que sobem (_ScrollV) e rodam (_ScrollU), deformadas por uma segunda camada mais lenta.
// A intensidade de cada listra escolhe a cor (vermelho-escuro → laranja → amarelo → branco quente) e só
// as listras mais quentes passam de 1 (o bloom apanha-as). Onde a intensidade é baixa o fogo corrói-se
// com borda viva, por isso a malha nunca se lê como uma superfície lisa.
// Aditivo, dos dois lados. O ruído usa (cos, sin) do ângulo: sem costura em uv.x = 0/1.
Shader "KonoSuba/FireFlow"
{
    Properties
    {
        _Intensity ("Intensidade", Float) = 1.0
        _Hot ("Brilho das listras quentes (HDR)", Float) = 1.6
        _ScrollV ("Subida", Float) = 1.4
        _ScrollU ("Rotação", Float) = 0.35
        _Streaks ("Listras à volta", Float) = 5
        _Length ("Comprimento das listras", Float) = 1.2
        _Erode ("Corrosão", Range(0, 1)) = 0.35
        _Seed ("Semente", Float) = 0
        _Fade ("Opacidade", Range(0, 1)) = 1
        _Open ("Fita aberta (apaga as bordas em uv.x)", Range(0, 1)) = 0
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
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "FireCommon.cginc"

            float _Intensity, _Hot, _ScrollV, _ScrollU, _Streaks, _Length, _Erode, _Seed, _Fade, _Open;

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

            // Ruído sem costura à volta: o ângulo vira um círculo em 3D.
            float Flow(float u, float v, float around, float along, float t, float seed)
            {
                float a = (u + t * _ScrollU) * 6.2831853;
                float3 p = float3(cos(a) * around, sin(a) * around, v * along - t * _ScrollV + seed);
                return FireFbm3(p);
            }

            float3 Gradient(float h)
            {
                float3 c = lerp(float3(0.35, 0.02, 0.0), float3(1.0, 0.22, 0.0), smoothstep(0.0, 0.3, h));
                c = lerp(c, float3(1.0, 0.5, 0.04), smoothstep(0.3, 0.6, h));
                c = lerp(c, float3(1.0, 0.82, 0.35), smoothstep(0.6, 0.85, h));
                c = lerp(c, float3(1.0, 0.96, 0.85), smoothstep(0.85, 1.0, h));
                return c;
            }

            float4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                float u = i.uv.x, v = i.uv.y;

                // Deformação lenta (as listras ondulam e enrolam) e listras compridas por cima.
                float warp = Flow(u, v, 1.2, 1.5, t * 0.5, _Seed + 11.0) - 0.5;
                float streak = Flow(u + warp * 0.12, v, _Streaks, _Length, t, _Seed);
                float detail = Flow(u + warp * 0.2, v, _Streaks * 2.3, _Length * 2.5, t * 1.3, _Seed + 5.0);
                float s = saturate(streak * 1.25 + (detail - 0.5) * 0.5);

                // Mais forte a meio do comprimento; apaga-se nas pontas da malha.
                float along = smoothstep(0.0, 0.18, v) * (1.0 - smoothstep(0.6, 1.0, v));
                // Fitas abertas: as bordas laterais desfazem-se (sem aresta reta).
                along *= lerp(1.0, smoothstep(0.0, 0.35, u) * smoothstep(1.0, 0.65, u), _Open);
                float heat = saturate((s - 0.25) * 1.8) * along;

                // Corrosão: onde arrefece, abre buracos com borda viva.
                float keep = smoothstep(_Erode, _Erode + 0.06, s * along + 0.1);
                // As listras de lado (onde se vê a malha de raspão) lêem-se mais densas: um pouco mais de brilho.
                float3 view = normalize(_WorldSpaceCameraPos - i.worldPos);
                float grazing = 1.0 - abs(dot(normalize(i.normal), view));
                heat *= 0.8 + 0.4 * grazing;

                float3 col = Gradient(heat) * heat * _Intensity;
                col += float3(1.0, 0.85, 0.55) * pow(saturate(heat - 0.7) / 0.3, 2.0) * _Hot; // listras quentes
                return float4(max(col, 0) * keep * _Fade, 0);
            }
            ENDCG
        }
    }
}
