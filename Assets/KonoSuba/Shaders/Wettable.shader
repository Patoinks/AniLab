// Superfície PBR (URP) que pode ficar molhada só onde a água tocou (WetSurface passa as manchas):
// a zona molhada fica mais escura e muito mais brilhante, com escorrências que descem a partir de cada mancha.
// Opcional (_VertexColor = 1): a cor vem das cores dos vértices (sRGB), como no boneco de treino do Blender;
// onde o alfa do vértice é 1 (tecido) pinta-se o alvo do peito em espaço do objeto (_Ring*).
Shader "KonoSuba/Wettable"
{
    Properties
    {
        _Color ("Cor", Color) = (1, 1, 1, 1)
        _Glossiness ("Suavidade", Range(0, 1)) = 0.3
        _Metallic ("Metálico", Range(0, 1)) = 0
        _WetRunoff ("Comprimento das escorrências (m)", Float) = 0.7
        [Toggle] _VertexColor ("Cor dos vértices (sRGB)", Float) = 0
        _RingColor ("Cor do alvo", Color) = (0.545, 0.263, 0.192, 1)
        _RingCenter ("Alvo: centro (x, altura), frente (sinal de z, limite)", Vector) = (0, 1.19, 1, 0.1)
        _RingRadii ("Alvo: raios (anel fora, anel dentro, ponto)", Vector) = (0.169, 0.122, 0.059, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

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

            #define WET_SPOTS 16
            float4 _WetSpots[WET_SPOTS];   // xyz mundo, w raio
            float _WetAmounts[WET_SPOTS];  // 0..1 (seca com o tempo)
            float4 _Color, _RingColor, _RingCenter, _RingRadii;
            float _Glossiness, _Metallic, _WetRunoff, _VertexColor;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogCoord : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                float4 color : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                o.positionOS = v.positionOS.xyz;
                o.color = v.color;
                return o;
            }

            // Ruído barato para as escorrências não serem riscas perfeitas.
            float Streak(float x) { return 0.5 + 0.5 * sin(x * 37.0) * sin(x * 13.0 + 1.7); }

            half4 frag(Varyings i) : SV_Target
            {
                float wet = 0;
                for (int k = 0; k < WET_SPOTS; k++)
                {
                    float4 s = _WetSpots[k];
                    float amount = _WetAmounts[k];
                    if (amount <= 0.001 || s.w <= 0.0) continue;
                    float3 d = i.positionWS - s.xyz;
                    // Abaixo da mancha a água escorre: a zona estica para baixo, em fios irregulares.
                    float below = saturate(-d.y / max(_WetRunoff, 1e-3));
                    float lateral = length(d.xz);
                    float streak = Streak(atan2(d.z, d.x) * 2.0 + s.x * 3.1);
                    float runWidth = s.w * lerp(1.0, 0.35 + 0.4 * streak, below);
                    float vertical = d.y > 0 ? d.y : d.y * (1.0 - below) * 0.15;
                    float dist = length(float2(lateral / max(runWidth / s.w, 0.05), vertical));
                    float m = 1.0 - smoothstep(s.w * 0.55, s.w, dist);
                    m *= 1.0 - smoothstep(_WetRunoff * 0.7, _WetRunoff, -d.y);
                    wet = max(wet, m * amount);
                }

                float3 normalWS = normalize(i.normalWS);
                float3 viewWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                // Película de água: um brilho de reflexo nas bordas (fresnel), porque este cenário escuro quase
                // não tem o que refletir e a zona molhada passaria despercebida.
                float fres = pow(1.0 - saturate(dot(viewWS, normalWS)), 3.0);

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

                float3 baseColor = _Color.rgb;
                if (_VertexColor > 0.5)
                {
                    baseColor *= SRGBToLinear(i.color.rgb);
                    // Alvo pintado no tecido: anel e ponto, só na frente do peito.
                    float r = length(float2(i.positionOS.x - _RingCenter.x, i.positionOS.y - _RingCenter.y));
                    float aa = fwidth(r);
                    float ring = smoothstep(_RingRadii.y - aa, _RingRadii.y + aa, r) * (1.0 - smoothstep(_RingRadii.x - aa, _RingRadii.x + aa, r));
                    float dot0 = 1.0 - smoothstep(_RingRadii.z - aa, _RingRadii.z + aa, r);
                    float front = step(_RingCenter.w, i.positionOS.z * _RingCenter.z);
                    baseColor = lerp(baseColor, _RingColor.rgb, saturate(ring + dot0) * front * step(0.5, i.color.a));
                }

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = baseColor * lerp(1.0, 0.4, wet);
                surface.metallic = _Metallic;
                surface.smoothness = lerp(_Glossiness, 0.95, wet);
                surface.normalTS = half3(0, 0, 1);
                surface.emission = float3(0.42, 0.47, 0.55) * wet * (0.12 + 0.6 * fres);
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
