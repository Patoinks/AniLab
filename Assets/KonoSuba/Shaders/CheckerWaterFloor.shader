// Chão axadrezado do "pós-vida" coberto por uma película de água espelhada (URP).
// Todo o movimento da água vem da simulação de ondas (WaterSim.cs): a malha sobe e desce, as normais
// saem do declive simulado, o xadrez por baixo refrata através da onda e as cristas apanham luz.
// Reflexo planar (PlanarReflection.cs) com fresnel. Onde o vento varreu a água, o chão fica a seco.
// Iluminação PBR do URP (UniversalFragmentPBR); sombras e profundidade pelos passes do Lit.
Shader "KonoSuba/CheckerWaterFloor"
{
    Properties
    {
        [Header(Chao)]
        _ColorA ("Tile claro", Color) = (0.27, 0.28, 0.30, 1)
        _ColorB ("Tile escuro", Color) = (0.11, 0.115, 0.13, 1)
        _TileSize ("Tamanho do tile (m)", Float) = 4
        _Variation ("Variação por tile", Range(0, 0.2)) = 0.04
        _GlowColor ("Brilho central", Color) = (0.22, 0.23, 0.26, 1)
        _GlowRadius ("Raio do brilho (m)", Float) = 16

        [Header(Agua)]
        _ReflectionMin ("Reflexo a olhar de cima", Range(0, 1)) = 0.35
        _ReflectionMax ("Reflexo em ângulo rasante", Range(0, 1)) = 0.85
        _ReflectionTint ("Cor do reflexo", Color) = (0.85, 0.9, 1, 1)
        _Distortion ("Distorção do reflexo", Range(0, 0.3)) = 0.12
        _Refraction ("Refração do xadrez (profundidade, m)", Range(0, 0.6)) = 0.25
        _Smoothness ("Smoothness (brilho especular)", Range(0, 1)) = 0.85
        _IdleWaves ("Ondulação parada", Range(0, 1)) = 0.04
        _WaveScale ("Escala da ondulação", Float) = 0.9
        _WaveSpeed ("Velocidade da ondulação", Float) = 0.4

        [Header(Agua rasa)]
        _SimNormalScale ("Relevo da água (exagero das normais)", Float) = 2.5
        _DryTint ("Chão seco (multiplicador de cor)", Float) = 1.12
        _DrySmoothness ("Smoothness do chão seco", Range(0, 1)) = 0.35

        [Header(Cristas)]
        _CrestColor ("Luz nas cristas", Color) = (0.15, 0.17, 0.21, 1)
        _CrestSharpness ("Sensibilidade das cristas", Float) = 0.8

        [HideInInspector] _SimMesh ("Malha simulada (1) ou chao plano (0)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "WaterSim.cginc"
            #include "LightningCommon.cginc"

            sampler2D _ReflectionTex;
            float4 _ColorA, _ColorB, _GlowColor, _ReflectionTint, _CrestColor;
            float4 _LightningFlash;   // xyz posição (y ignorado), w intensidade (global, LightningDischarge)
            float4 _LightningColor;
            sampler2D _LightningGround;   // canais do Raio na água: chegada, ID do ramo, s, cobertura (LightningGroundField)
            float4 _LightningGroundRect;  // x mín., z mín., tamanho, ativo
            float _TileSize, _Variation, _GlowRadius;
            float _ReflectionMin, _ReflectionMax, _Distortion, _Refraction, _Smoothness;
            float _IdleWaves, _WaveScale, _WaveSpeed, _CrestSharpness, _SimMesh;
            float _SimNormalScale, _DryTint, _DrySmoothness;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float fogCoord : TEXCOORD2;
            };

            // A malha simulada sobe e desce com as ondas (o chão plano grande fica onde está).
            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                if (_SimMesh > 0.5) wp.y += WaterSimHeight(wp.xz);
                o.positionWS = wp;
                o.positionCS = TransformWorldToHClip(wp);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Onda direcional: acumula o gradiente (dh/dx, dh/dz) de amp*sin(k*dot(d,p) + w*t).
            void Wave(inout float2 g, float2 p, float t, float2 dir, float k, float amp)
            {
                dir = normalize(dir);
                float phase = k * dot(dir, p) + t * sqrt(9.8 * k);
                g += dir * (k * amp * cos(phase));
            }

            float3 Checker(float2 xz)
            {
                // Xadrez com filtro de caixa (evita aliasing ao longe).
                float2 uv = xz / _TileSize;
                float2 fw = max(fwidth(uv), 1e-4);
                float2 i = 2.0 * (abs(frac((uv - 0.5 * fw) * 0.5) - 0.5) - abs(frac((uv + 0.5 * fw) * 0.5) - 0.5)) / fw;
                float checker = 0.5 - 0.5 * i.x * i.y;
                float3 col = lerp(_ColorB.rgb, _ColorA.rgb, checker);
                return col * (1.0 + (Hash21(floor(uv)) - 0.5) * 2.0 * _Variation);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 xz = i.positionWS.xz;
                // Dentro da área simulada quem desenha é a malha deslocada; o chão plano recorta-se.
                if (_SimMesh < 0.5 && InsideWaterSim(xz, 0.05)) clip(-1);

                // Declive da água: simulação + uma ondulação de fundo quase impercetível.
                float2 g = 0;
                float2 p = xz * _WaveScale;
                float t = _Time.y * _WaveSpeed;
                Wave(g, p, t, float2( 1.0,  0.3), 1.0, 0.045);
                Wave(g, p, t, float2(-0.4,  1.0), 1.7, 0.028);
                Wave(g, p, t, float2( 0.8, -0.9), 2.9, 0.016);
                g *= _IdleWaves;
                // Película rasa: os desníveis são de centímetros, por isso o relevo das normais é exagerado
                // para se ler. Onde a água foi varrida, o chão fica a seco: sem reflexo, mate e sem ondulação.
                float wet = WaterSimWetness(xz);
                float2 simGrad = WaterSimGradient(xz) * _SimNormalScale;
                g = (g + simGrad) * wet;
                float3 normalWS = normalize(float3(-g.x, 1.0, -g.y));

                // Refração: o xadrez debaixo da película é visto deslocado pela inclinação da onda.
                float3 col = Checker(xz - simGrad * _Refraction * wet);
                col *= lerp(_DryTint, 1.0, wet);

                // Reflexo planar distorcido pela normal; fresnel decide quanto é espelho vs. chão.
                float3 viewWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float2 suv = i.screenPos.xy / max(i.screenPos.w, 1e-4) + normalWS.xz * _Distortion;
                float3 refl = tex2D(_ReflectionTex, suv).rgb * _ReflectionTint.rgb;
                float ndv = saturate(dot(viewWS, normalWS));
                float fresnel = lerp(_ReflectionMin, _ReflectionMax, pow(1.0 - ndv, 4.0)) * wet;

                // Cristas: onde a superfície faz "bico" (curvatura negativa) e inclina, apanha luz.
                float crest = saturate(-WaterSimCurvature(xz) * _CrestSharpness) * saturate(length(simGrad) * 6.0);
                float glow = exp(-dot(xz, xz) / (_GlowRadius * _GlowRadius));

                float3 emission = refl * fresnel + col * _GlowColor.rgb * glow + _CrestColor.rgb * crest;

                // Clarão do Raio: luz roxa ampla e contida à volta da descarga...
                float2 fd = xz - _LightningFlash.xz;
                float flash = _LightningFlash.w * exp(-dot(fd, fd) / 30.0) * 0.25;
                emission += _LightningColor.rgb * flash * (0.18 + 0.6 * fresnel + 1.5 * crest) * (0.4 + 0.6 * wet);
                // ...e a água acende-se por baixo dos canais com o mesmo sinal do raio (chegada, reacendimentos,
                // apagões). IDs e chegadas não se interpolam: avaliam-se os 4 texels e interpola-se a luz.
                if (_LightningGroundRect.w > 0.0)
                {
                    float2 guv = (xz - _LightningGroundRect.xy) / _LightningGroundRect.z;
                    if (all(guv > 0.0) && all(guv < 1.0))
                    {
                        float2 tp = guv * 512.0 - 0.5;
                        float2 lbase = floor(tp), lf = tp - lbase;
                        float4 sig;
                        [unroll] for (int k = 0; k < 4; k++)
                        {
                            float2 o2 = float2(k & 1, k >> 1);
                            float4 lg = tex2Dlod(_LightningGround, float4((lbase + o2 + 0.5) / 512.0, 0, 0));
                            sig[k] = lg.y >= 0.0 ? LightningSignal(lg.y, lg.x, lg.z) * lg.w : 0.0;
                        }
                        float m = lerp(lerp(sig.x, sig.y, lf.x), lerp(sig.z, sig.w, lf.x), lf.y);
                        emission += _LightningColor.rgb * 1.1 * m * wet;
                    }
                }

                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.positionCS = i.positionCS;
                input.normalWS = normalWS;
                input.viewDirectionWS = viewWS;
                input.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                input.fogCoord = i.fogCoord;
                input.bakedGI = SampleSH(normalWS);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                input.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = col * (1.0 - fresnel);
                surface.metallic = 0;
                surface.smoothness = lerp(_DrySmoothness, _Smoothness, wet);
                surface.normalTS = half3(0, 0, 1);
                surface.emission = emission;
                surface.occlusion = 1;
                surface.alpha = 1;

                half4 c = UniversalFragmentPBR(input, surface);
                c.rgb = MixFog(c.rgb, i.fogCoord);
                return c;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
