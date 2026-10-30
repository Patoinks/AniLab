// Gelo pintado (URP): lâminas facetadas que deixam ver o chão por trás, refratado e tingido de azul (_CameraOpaqueTexture),
// com um corpo luminoso pintado em três tons por faceta, fraturas brancas por dentro (em camadas, com paralaxe, esticadas
// ao longo da lâmina), arestas claras, rim de fresnel, brilhos de faceta e geada branca na base.
// Atributos da malha (IceShard): uv0 = (tom da faceta, altura 0..1), uv1 = (aleatório da lâmina, comprimento),
// uv2 = distâncias às arestas da faceta (m), tangente = eixo da lâmina.
Shader "KonoSuba/Ice"
{
    Properties
    {
        [Header(Corpo)]
        _Deep ("Tom fundo", Color) = (0.04, 0.16, 0.32, 1)
        _Mid ("Tom médio", Color) = (0.22, 0.55, 0.85, 1)
        _Pale ("Tom claro", Color) = (0.75, 0.93, 1.0, 1)
        _Brightness ("Brilho do corpo", Float) = 1.0
        _Opacity ("Opacidade do corpo", Range(0, 1)) = 0.55
        _ExtraLight ("Luzes pontuais", Float) = 0.6

        [Header(Transparencia)]
        _Tint ("Cor através do gelo", Color) = (0.45, 0.8, 1.0, 1)
        _Refraction ("Refração", Float) = 0.04

        [Header(Fraturas)]
        _CrackColor ("Cor das fraturas", Color) = (0.8, 0.95, 1.0, 1)
        _CrackScale ("Escala", Float) = 1.6
        _CrackStrength ("Força", Float) = 0.6

        [Header(Arestas e brilhos)]
        [HDR] _EdgeColor ("Arestas", Color) = (1.2, 1.36, 1.4, 1)
        _EdgeWidth ("Largura das arestas (m)", Float) = 0.025
        [HDR] _RimColor ("Rim", Color) = (0.7, 1.05, 1.2, 1)
        [HDR] _GlintColor ("Brilho das facetas", Color) = (2.2, 2.5, 2.6, 1)
        _FrostColor ("Geada", Color) = (0.85, 0.95, 1.0, 1)
        _FrostHeight ("Altura da geada (0..1)", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "Ice"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Deep, _Mid, _Pale, _Tint, _CrackColor, _EdgeColor, _RimColor, _GlintColor, _FrostColor;
            float _Brightness, _Opacity, _ExtraLight, _Refraction, _CrackScale, _CrackStrength, _EdgeWidth, _FrostHeight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float3 axisOS : TEXCOORD3;
                float4 facet : TEXCOORD4;   // tom, altura, aleatório da lâmina, comprimento
                float4 edges : TEXCOORD5;
                float fogCoord : TEXCOORD6;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS.xyz;
                o.axisOS = v.tangentOS.xyz;
                o.facet = float4(v.uv0, v.uv1);
                o.edges = v.uv2;
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float3 Hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            // Distância à parede mais próxima entre células de Voronoi (F2 - F1): linhas finas onde as células se tocam.
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

            float Noise3(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash33(i).x, n100 = Hash33(i + float3(1, 0, 0)).x;
                float n010 = Hash33(i + float3(0, 1, 0)).x, n110 = Hash33(i + float3(1, 1, 0)).x;
                float n001 = Hash33(i + float3(0, 0, 1)).x, n101 = Hash33(i + float3(1, 0, 1)).x;
                float n011 = Hash33(i + float3(0, 1, 1)).x, n111 = Hash33(i + float3(1, 1, 1)).x;
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            // Uma camada de fraturas a uma profundidade dentro do gelo: células esticadas ao longo da lâmina, como placas.
            float Cracks(float3 p, float3 axis, float seed)
            {
                float3 q = (p - axis * dot(p, axis) * 0.7) * _CrackScale + seed * 7.31;
                return 1.0 - smoothstep(0.0, 0.07, VoronoiEdge(q));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float ndv = saturate(dot(N, V));
                float fres = pow(1.0 - ndv, 3.0);
                float tone = i.facet.x, height = i.facet.y, shard = i.facet.z, len = i.facet.w;

                // Corpo pintado: a luz principal só dá a direção (a lua é fraca); cada faceta tem o seu tom.
                Light main = GetMainLight();
                float lit = saturate(0.5 + 0.5 * dot(N, main.direction));
                lit = saturate(lit * 0.65 + (tone - 0.5) * 0.45 + height * 0.25 + 0.1);
                float3 body = lerp(_Deep.rgb, _Mid.rgb, smoothstep(0.0, 0.55, lit));
                body = lerp(body, _Pale.rgb, smoothstep(0.55, 1.0, lit));
                body *= _Brightness;

                // Luzes pontuais (o fogo, o clarão do trovão) tingem o gelo.
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float3 extra = 0;
                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS, half4(1, 1, 1, 1));
                    extra += l.color * l.distanceAttenuation * saturate(0.35 + 0.65 * dot(N, l.direction));
                LIGHT_LOOP_END
                #endif
                body += extra * _ExtraLight * _Pale.rgb;

                // O que está por trás, refratado pela faceta e tingido conforme a espessura atravessada.
                float3 nVS = TransformWorldToViewDir(N);
                float2 suv = inputData.normalizedScreenSpaceUV + nVS.xy * _Refraction * (0.4 + ndv);
                float thick = lerp(0.35, 1.0, ndv);
                float3 behind = SampleSceneColor(suv) * lerp(1.0, _Tint.rgb, thick);

                // Fraturas por dentro: duas camadas a profundidades diferentes (mexem-se com a vista = volume).
                float3 viewOS = TransformWorldToObjectDir(V);
                float3 axis = normalize(i.axisOS);
                float c1 = Cracks(i.positionOS - viewOS * 0.12, axis, shard);
                float c2 = Cracks(i.positionOS * 1.7 - viewOS * 0.35, axis, shard + 0.5);
                float cracks = (c1 * 0.65 + c2 * 0.35) * _CrackStrength * lerp(0.5, 1.0, ndv);
                // Véus leitosos ao longo da lâmina (as placas de gelo da referência), também por dentro.
                float3 pv = i.positionOS - viewOS * 0.2;
                float along = dot(pv, axis);
                float veil = Noise3((pv - axis * along) * 5.0 + axis * along * 0.6 + shard * 13.0);
                veil = smoothstep(0.45, 0.85, veil) * 0.35;

                float3 col = lerp(behind, body, saturate(_Opacity + veil)) + _CrackColor.rgb * cracks;
                // As pontas brilham mais (a luz acumula na ponta e faz bloom).
                col *= 1.0 + smoothstep(0.6, 1.0, height) * 0.45;

                // Arestas das facetas (mais largas nas lâminas grandes), rim e o brilho de uma faceta virada para a luz.
                float e = min(min(i.edges.x, i.edges.y), min(i.edges.z, i.edges.w));
                float ew = _EdgeWidth * sqrt(max(len, 0.2));
                float edge = 1.0 - smoothstep(ew * 0.3, ew, e);
                // Arestas pintadas: fortes só nas facetas claras e mais acima; somem nas sombras e na base.
                col += _EdgeColor.rgb * edge * smoothstep(0.15, 0.8, lit) * (0.25 + 0.75 * height);
                col += _RimColor.rgb * fres;
                float3 H = normalize(main.direction + V);
                float3 H2 = normalize(V + float3(0, 0.6, 0));
                float glint = max(smoothstep(0.93, 0.985, dot(N, H)), smoothstep(0.96, 0.995, dot(N, H2)) * 0.6);
                col += _GlintColor.rgb * glint * (0.4 + 0.6 * tone);

                // Geada branca na base, com o limite recortado por ruído.
                float frost = 1.0 - smoothstep(_FrostHeight * 0.4, _FrostHeight, height + (Noise3(i.positionOS * 6.0) - 0.5) * 0.25);
                col = lerp(col, _FrostColor.rgb * _Brightness, frost * 0.85);

                col = MixFog(col, i.fogCoord);
                return half4(col, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
