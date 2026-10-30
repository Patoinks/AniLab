// Fissuras do impacto do trovão (LightningImpact): disco plano no chão (uv 0..1 → p −1..1) com uma rede de
// rachas a brilhar — Voronoi em coordenadas log-polares (rachas a sair do centro e a ramificar), núcleo branco
// e brilho ciano. _Reach (0..1) abre as rachas a partir do centro; _Fade apaga-as a partir das pontas.
Shader "KonoSuba/LightningCracks"
{
    Properties
    {
        [HDR] _Core ("Núcleo", Color) = (1, 1, 1, 1)
        [HDR] _Glow ("Brilho", Color) = (0.08, 0.6, 1, 1)
        _Intensity ("Intensidade", Float) = 4
        _Seed ("Semente", Float) = 0
        _Reach ("Alcance (0..1)", Range(0, 1)) = 1
        _Fade ("Apagar (0..1)", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Cracks"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Core, _Glow;
            float _Intensity, _Seed, _Reach, _Fade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 p : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.p = v.uv * 2.0 - 1.0;
                return o;
            }

            float2 Hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p + _Seed * 17.13) * 43758.5453);
            }

            // Distância à fronteira entre células (F2 − F1 aproximado pela distância à aresta).
            float VoronoiEdge(float2 x)
            {
                float2 n = floor(x), f = frac(x);
                float2 mr = 0, mg = 0;
                float md = 8.0;
                for (int j = -1; j <= 1; j++)
                    for (int i = -1; i <= 1; i++)
                    {
                        float2 g = float2(i, j);
                        float2 r = g + Hash2(n + g) - f;
                        float d = dot(r, r);
                        if (d < md) { md = d; mr = r; mg = g; }
                    }
                md = 8.0;
                for (int j2 = -2; j2 <= 2; j2++)
                    for (int i2 = -2; i2 <= 2; i2++)
                    {
                        float2 g = mg + float2(i2, j2);
                        float2 r = g + Hash2(n + g) - f;
                        if (dot(mr - r, mr - r) > 1e-5) md = min(md, dot(0.5 * (mr + r), normalize(r - mr)));
                    }
                return md;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float r = length(i.p);
                if (r > 1.0) discard;
                float a = atan2(i.p.y, i.p.x) / 6.2832 + 0.5;
                // Log-polar: as células esticam-se para fora (rachas radiais que ramificam).
                float2 q = float2(a * 11.0, log(max(r, 0.02)) * 2.6);
                float edge = VoronoiEdge(q + float2(0, _Seed));
                float fine = VoronoiEdge(q * 2.3 + 5.0);
                float lineCore = 1.0 - smoothstep(0.0, 0.03 * (1.25 - r), edge);
                float lineGlow = 1.0 - smoothstep(0.0, 0.11 * (1.25 - r), edge);
                lineCore = max(lineCore, (1.0 - smoothstep(0.0, 0.02, fine)) * 0.5 * (1.0 - r));
                // Abre do centro (alcance) e apaga das pontas para o centro.
                float open = 1.0 - smoothstep(_Reach - 0.08, _Reach, r);
                float alive = 1.0 - smoothstep(_Fade * 1.05 - 0.15, _Fade * 1.05, r);
                float centre = exp(-r * r * 90.0) * 0.8;
                float k = open * alive * (1.0 - r * 0.45);
                float3 col = _Core.rgb * (lineCore + centre) * k * _Intensity + _Glow.rgb * (lineGlow * 0.6 + centre * 0.6) * k * _Intensity * 0.6;
                return float4(col, 0);
            }
            ENDHLSL
        }
    }
}
