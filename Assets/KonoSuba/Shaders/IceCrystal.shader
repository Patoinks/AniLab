// Cristal de gelo (URP) para o kit do Blender (VFX/Ice/IceKit.fbx), como na folha de assets:
// - o que está por trás vê-se refratado e com dispersão (vermelho, verde e azul desviados de forma diferente = franjas de prisma);
// - interior marmoreado em duas camadas com paralaxe (manchas de azul fundo e zonas claras), do ruído 3D cozido;
// - fraturas internas com iridescência (a cor muda com o ângulo, como película de sabão) e cintilas espetrais nas facetas;
// - arestas brancas finas de largura constante no ecrã, a partir das baricêntricas guardadas na cor dos vértices;
// - brilho interior ciano, mais forte para as pontas.
Shader "KonoSuba/IceCrystal"
{
    Properties
    {
        _Noise ("Ruído 3D", 3D) = "white" {}
        [Header(Corpo)]
        _Deep ("Azul fundo", Color) = (0.02, 0.07, 0.2, 1)
        _Mid ("Azul médio", Color) = (0.25, 0.5, 0.85, 1)
        _Pale ("Azul claro", Color) = (0.7, 0.88, 1.0, 1)
        _Opacity ("Opacidade do corpo", Range(0, 1)) = 0.55
        _MarbleScale ("Escala do marmoreado (1/m)", Float) = 0.55
        _MarbleDepth ("Profundidade do marmoreado (m)", Float) = 0.25
        [Header(Refracao)]
        _Refraction ("Refração", Float) = 0.035
        _Dispersion ("Dispersão (arco-íris)", Range(0, 1)) = 0.35
        [Header(Fraturas e arco iris)]
        _FractureScale ("Escala das fraturas (1/m)", Float) = 1.2
        _Fractures ("Força das fraturas", Float) = 0.55
        _Iridescence ("Iridescência", Range(0, 1)) = 0.6
        [Header(Arestas e brilhos)]
        [HDR] _EdgeColor ("Arestas", Color) = (1.1, 1.25, 1.4, 1)
        _EdgeWidth ("Largura das arestas (px)", Float) = 1.1
        [HDR] _RimColor ("Rim", Color) = (0.45, 0.7, 1.0, 1)
        _GlintStrength ("Cintilas", Float) = 1.5
        [HDR] _Glow ("Brilho interior", Color) = (0.12, 0.32, 0.75, 1)
        _GlowHeight ("Altura do brilho máximo (m)", Float) = 4
        _Fade ("Fade", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "IceCrystal"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE3D(_Noise); SAMPLER(sampler_Noise);
            CBUFFER_START(UnityPerMaterial)
            float4 _Deep, _Mid, _Pale, _EdgeColor, _RimColor, _Glow;
            float _Opacity, _MarbleScale, _MarbleDepth, _Refraction, _Dispersion, _FractureScale, _Fractures, _Iridescence;
            float _EdgeWidth, _GlintStrength, _GlowHeight, _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;   // baricêntricas do triângulo
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float3 bary : TEXCOORD3;
                float fogCoord : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS.xyz;
                o.bary = v.color.rgb;
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float3 Hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            // Paredes entre células de Voronoi (F2 - F1): os planos de fratura.
            float VoronoiEdge(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                float d1 = 8.0, d2 = 8.0;
                for (int z = -1; z <= 1; z++)
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float3 g = float3(x, y, z);
                    float3 r = g + Hash33(i + g) - f;
                    float d = dot(r, r);
                    if (d < d1) { d2 = d1; d1 = d; }
                    else if (d < d2) d2 = d;
                }
                return sqrt(d2) - sqrt(d1);
            }

            // Arco-íris (matiz 0..1 -> RGB saturado).
            float3 Spectrum(float h)
            {
                return saturate(abs(frac(h + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
            }

            float3 Noise(float3 p) { return SAMPLE_TEXTURE3D_LOD(_Noise, sampler_Noise, p, 0).rgb; }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float ndv = saturate(dot(N, V));
                float fres = pow(1.0 - ndv, 3.0);
                float3 viewOS = TransformWorldToObjectDir(V);
                float h01 = saturate(i.positionOS.y / _GlowHeight);
                Light main = GetMainLight();
                float lit = saturate(0.5 + 0.5 * dot(N, main.direction));

                // Interior marmoreado: duas camadas a profundidades diferentes (mexem-se com a vista).
                float3 n1 = Noise((i.positionOS - viewOS * _MarbleDepth * 0.5) * _MarbleScale);
                float3 n2 = Noise((i.positionOS * 1.9 - viewOS * _MarbleDepth) * _MarbleScale + 0.37);
                float marble = smoothstep(0.25, 0.8, n1.r * 0.65 + n2.b * 0.35);
                float tone = saturate((1.0 - marble) * 0.9 + lit * 0.2 + h01 * 0.1 - 0.1);
                float3 body = lerp(_Deep.rgb, _Mid.rgb, smoothstep(0.0, 0.55, tone));
                body = lerp(body, _Pale.rgb, smoothstep(0.55, 1.0, tone));

                // Refração com dispersão: cada canal desvia-se um pouco diferente (franjas de prisma).
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float2 off = TransformWorldToViewDir(N).xy * _Refraction * (0.5 + ndv);
                float3 behind;
                behind.r = SampleSceneColor(suv + off * (1.0 + _Dispersion)).r;
                behind.g = SampleSceneColor(suv + off).g;
                behind.b = SampleSceneColor(suv + off * (1.0 - _Dispersion)).b;
                behind *= lerp(_Pale.rgb, _Mid.rgb, ndv);
                float3 col = lerp(behind, body, saturate(_Opacity * lerp(0.75, 1.15, marble)));

                // Fraturas internas (esticadas ao longo do pico) com iridescência; só em algumas zonas.
                float3 q = (i.positionOS - viewOS * 0.22) * float3(1.0, 0.45, 1.0) * _FractureScale;
                float crack = (1.0 - smoothstep(0.0, 0.06, VoronoiEdge(q))) * smoothstep(0.35, 0.7, n2.r);
                float3 irid = Spectrum(frac(ndv * 1.7 + n1.g * 2.3 + dot(N, float3(0.3, 0.5, 0.2))));
                col += crack * lerp(_Pale.rgb, irid, _Iridescence) * _Fractures;

                // Arestas brancas: largura constante no ecrã.
                float e = min(i.bary.x, min(i.bary.y, i.bary.z));
                float edge = 1.0 - smoothstep(0.0, fwidth(e) * _EdgeWidth, e);
                col += _EdgeColor.rgb * edge * (0.35 + 0.65 * lit) * (0.6 + 0.4 * h01);

                // Rim com uma franja de cor, cintilas espetrais nas facetas viradas para a luz.
                col += _RimColor.rgb * fres + Spectrum(frac(ndv * 3.0 + 0.2)) * fres * _Iridescence * 0.25;
                float3 H = normalize(main.direction + V);
                float glint = smoothstep(0.95, 0.99, dot(N, H)) + smoothstep(0.97, 0.995, dot(N, normalize(V + float3(0, 0.5, 0)))) * 0.5;
                col += glint * _GlintStrength * lerp(1.0, Spectrum(frac(dot(N, float3(1.7, 2.3, 0.9)))), 0.5 * _Iridescence);

                // Brilho interior ciano, mais forte para as pontas.
                col += _Glow.rgb * (0.3 + 0.7 * h01);

                col = MixFog(col, i.fogCoord);
                return half4(col * _Fade, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
