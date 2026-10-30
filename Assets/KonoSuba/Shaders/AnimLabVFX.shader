// Shader-mestre dos VFX do AnimLab (URP, sem iluminação). Cada técnica de estúdio liga-se por material:
//   mistura (aditivo / pré-multiplicado / multiplicar), textura com pan, distorção de UV por ruído, flowmap,
//   flipbook com interpolação por motion vectors, gradient map, erosão com borda queimada, soft particles,
//   fresnel (brilho na borda ou só borda) e deformação de vértices por ruído.
// Partículas (Shuriken): Custom Vertex Streams → UV (TEXCOORD0.xy) + Custom1.xy (TEXCOORD0.zw):
//   Custom1.x soma à erosão (erosão ao longo da vida), Custom1.y é uma semente aleatória (frame/ruído).
// A cor final vem pré-multiplicada; a mistura é escolhida com VFXMaterial.SetBlend (o inspetor faz isso).
Shader "AnimLab/VFX"
{
    Properties
    {
        [Enum(Aditivo,0,PreMultiplicado,1,Multiplicar,2)] _Blend ("Mistura", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Faces", Float) = 0
        [HideInInspector] _SrcBlend ("", Float) = 1
        [HideInInspector] _DstBlend ("", Float) = 1

        [HDR] _Color ("Cor", Color) = (1, 1, 1, 1)
        _Intensity ("Intensidade", Float) = 1
        _MainTex ("Textura (forma)", 2D) = "white" {}
        _MainPan ("Pan da textura (u, v por s)", Vector) = (0, 0, 0, 0)
        _MaskChannel ("Canal da forma (r, g, b, a)", Vector) = (1, 0, 0, 0)
        _UVEdgeFade ("Apagar junto às bordas da UV", Range(0, 0.5)) = 0

        [Toggle(_UV_DISTORT)] _UseDistort ("Distorção de UV por ruído", Float) = 0
        _NoiseScale ("Escala do ruído (u, v)", Vector) = (4, 4, 0, 0)
        _NoisePan ("Pan do ruído (u, v por s)", Vector) = (0, 0.5, 0, 0)
        _DistortStrength ("Força da distorção", Float) = 0.1

        [Toggle(_FLOWMAP)] _UseFlow ("Flowmap", Float) = 0
        _FlowTex ("Flowmap (rg)", 2D) = "gray" {}
        _FlowStrength ("Força do flow", Float) = 0.2
        _FlowSpeed ("Velocidade do flow", Float) = 0.5

        [Toggle(_FLIPBOOK)] _UseFlipbook ("Flipbook", Float) = 0
        _FlipbookGrid ("Grelha (colunas, linhas, fps)", Vector) = (8, 8, 30, 0)
        [Toggle(_FLIPBOOK_MV)] _UseMotion ("Interpolar com motion vectors", Float) = 0
        _MotionTex ("Motion vectors (rg)", 2D) = "gray" {}
        _MotionStrength ("Força dos motion vectors", Float) = 0.02

        [Toggle(_GRADIENT_MAP)] _UseGradient ("Gradient map", Float) = 0
        _GradientTex ("Gradiente", 2D) = "white" {}
        _GradientScale ("Escala do gradiente", Float) = 1
        _GradientShift ("Deslocar o gradiente", Float) = 0

        [Toggle(_EROSION)] _UseErosion ("Erosão", Float) = 0
        _Erosion ("Erosão (0 inteiro, 1 desaparecido)", Range(0, 1)) = 0
        _ErosionSoftness ("Suavidade da erosão", Range(0.001, 1)) = 0.1
        _ErosionNoiseMix ("Ruído na erosão", Range(0, 1)) = 0.5
        _EdgeWidth ("Largura da borda queimada", Range(0, 0.5)) = 0.05
        [HDR] _EdgeColor ("Cor da borda", Color) = (2, 1, 0.3, 1)

        [Toggle(_SOFT_PARTICLES)] _UseSoft ("Soft particles", Float) = 0
        _SoftDistance ("Distância de suavização (m)", Float) = 0.3

        [Toggle(_FRESNEL)] _UseFresnel ("Fresnel", Float) = 0
        [Enum(BrilhoNaBorda,0,SoBorda,1)] _FresnelMode ("Modo do fresnel", Float) = 0
        _FresnelPower ("Potência do fresnel", Float) = 3
        [HDR] _FresnelColor ("Cor do fresnel", Color) = (1, 1, 1, 1)

        [Toggle(_VERTEX_OFFSET)] _UseVertex ("Deformar vértices", Float) = 0
        _VertexOffset ("Deslocamento (m)", Float) = 0.1
        _VertexNoiseScale ("Escala do ruído dos vértices", Float) = 2
        _VertexSpeed ("Velocidade do ruído dos vértices", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "VFX"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _UV_DISTORT
            #pragma shader_feature_local _FLOWMAP
            #pragma shader_feature_local _FLIPBOOK
            #pragma shader_feature_local _FLIPBOOK_MV
            #pragma shader_feature_local _GRADIENT_MAP
            #pragma shader_feature_local _EROSION
            #pragma shader_feature_local _SOFT_PARTICLES
            #pragma shader_feature_local _FRESNEL
            #pragma shader_feature_local _VERTEX_OFFSET
            #pragma shader_feature_local _BLEND_MULTIPLY
            #pragma multi_compile_fog

            #include "VFXCore.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_FlowTex); SAMPLER(sampler_FlowTex);
            TEXTURE2D(_MotionTex); SAMPLER(sampler_MotionTex);
            TEXTURE2D(_GradientTex); SAMPLER(sampler_GradientTex);

            CBUFFER_START(UnityPerMaterial)
            float4 _Color, _MainTex_ST, _MainPan, _MaskChannel, _NoiseScale, _NoisePan, _FlipbookGrid, _EdgeColor, _FresnelColor;
            float _UVEdgeFade;
            float _Intensity, _DistortStrength, _FlowStrength, _FlowSpeed, _MotionStrength, _GradientScale, _GradientShift;
            float _Erosion, _ErosionSoftness, _ErosionNoiseMix, _EdgeWidth, _SoftDistance, _FresnelMode, _FresnelPower;
            float _VertexOffset, _VertexNoiseScale, _VertexSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float4 uv : TEXCOORD0; // xy UV, zw = Custom1.xy das partículas
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;
                float4 color : COLOR;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float fogCoord : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                #if _VERTEX_OFFSET
                    float k = VFXFbm(v.positionOS.xz * _VertexNoiseScale + v.positionOS.y * _VertexNoiseScale * 0.7
                        + _Time.y * _VertexSpeed + v.uv.w * 17.0);
                    wp += n * (k - 0.5) * 2.0 * _VertexOffset;
                #endif
                o.positionWS = wp;
                o.positionCS = TransformWorldToHClip(wp);
                o.normalWS = n;
                o.uv = v.uv;
                o.color = v.color;
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float4 SampleShape(float2 uv, float2 baseUV, float random, float t)
            {
                #if _FLOWMAP
                    float2 flow = (SAMPLE_TEXTURE2D(_FlowTex, sampler_FlowTex, baseUV).rg * 2.0 - 1.0) * _FlowStrength;
                    float3 a = VFXFlowUV(uv, flow, t * _FlowSpeed, 0.0);
                    float3 b = VFXFlowUV(uv, flow, t * _FlowSpeed, 0.5);
                    return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, a.xy) * a.z + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, b.xy) * b.z;
                #elif _FLIPBOOK
                    float2 uvA, uvB;
                    float blend;
                    float count = _FlipbookGrid.x * _FlipbookGrid.y;
                    VFXFlipbookUV(frac(uv), _FlipbookGrid.xy, t * _FlipbookGrid.z + random * count, uvA, uvB, blend);
                    #if _FLIPBOOK_MV
                        // Cada frame anda metade do caminho em direção ao outro, pelos motion vectors.
                        float2 mvA = (SAMPLE_TEXTURE2D(_MotionTex, sampler_MotionTex, uvA).rg * 2.0 - 1.0) * _MotionStrength;
                        float2 mvB = (SAMPLE_TEXTURE2D(_MotionTex, sampler_MotionTex, uvB).rg * 2.0 - 1.0) * _MotionStrength;
                        uvA -= mvA * blend;
                        uvB += mvB * (1.0 - blend);
                    #endif
                    return lerp(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvA), SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvB), blend);
                #else
                    return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                #endif
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float random = i.uv.w;
                float2 baseUV = i.uv.xy;
                float2 uv = baseUV * _MainTex_ST.xy + _MainTex_ST.zw + _MainPan.xy * t;
                #if _UV_DISTORT
                    float dn = VFXFbm(baseUV * _NoiseScale.xy + _NoisePan.xy * t + random * 13.0);
                    uv += (dn - 0.5) * _DistortStrength;
                #endif

                float4 tex = SampleShape(uv, baseUV, random, t);
                float mask = saturate(dot(tex, _MaskChannel));
                // Cartas e malhas com UV aberta: sem corte visível nas bordas.
                float2 edgeUV = min(baseUV, 1.0 - baseUV);
                mask *= _UVEdgeFade > 0.0 ? smoothstep(0.0, _UVEdgeFade, min(edgeUV.x, edgeUV.y)) : 1.0;

                #if _GRADIENT_MAP
                    float3 col = SAMPLE_TEXTURE2D(_GradientTex, sampler_GradientTex, float2(saturate(mask * _GradientScale + _GradientShift), 0.5)).rgb;
                #else
                    float3 col = tex.rgb;
                #endif
                col *= _Color.rgb * i.color.rgb * _Intensity;
                float alpha = mask * _Color.a * i.color.a;

                #if _EROSION
                    float erosion = saturate(_Erosion + i.uv.z);
                    float noise = VFXFbm(baseUV * _NoiseScale.xy * 1.7 + random * 7.0);
                    float2 er = VFXErode(lerp(mask, noise, _ErosionNoiseMix), erosion, _ErosionSoftness, _EdgeWidth);
                    alpha *= er.x;
                    col += _EdgeColor.rgb * er.y;
                #endif

                #if _FRESNEL
                    float fr = VFXFresnel(i.normalWS, GetWorldSpaceNormalizeViewDir(i.positionWS), _FresnelPower);
                    if (_FresnelMode > 0.5) alpha *= fr;
                    else col += _FresnelColor.rgb * fr;
                #endif

                #if _SOFT_PARTICLES
                    float eye = -TransformWorldToView(i.positionWS).z;
                    alpha *= VFXSoftFade(i.positionCS, eye, _SoftDistance);
                #endif

                #if _BLEND_MULTIPLY
                    return half4(lerp(1.0.xxx, col, saturate(alpha)), 1.0);
                #else
                    col = MixFogColor(col * alpha, 0.0.xxx, i.fogCoord);
                    return half4(col, saturate(alpha));
                #endif
            }
            ENDHLSL
        }
    }
    CustomEditor "AnimLab.KonoSuba.EditorTools.AnimLabVFXGUI"
}
