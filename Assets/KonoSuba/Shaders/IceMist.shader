// Névoa de gelo volumétrica (URP): raymarching dentro da caixa do objeto (sem billboards), densidade de um ruído 3D
// cozido (Perlin-Worley + Worley a desfiar as bordas), com a forma de um monte denso no centro e um anel a espalhar-se,
// colado ao chão. Luz falsa: sombra própria em 3 amostras (azul frio por dentro, branco no topo) e um núcleo luminoso.
// Para na profundidade da cena, por isso envolve quem está lá dentro.
Shader "KonoSuba/IceMist"
{
    Properties
    {
        _Noise ("Ruído 3D", 3D) = "white" {}
        _Radius ("Raio (m)", Float) = 4.8
        _Height ("Altura (m)", Float) = 2.4
        _Ring ("Anel (0..1 do raio)", Range(0, 1)) = 0.72
        _Density ("Densidade", Float) = 2.2
        _Absorb ("Sombra própria", Float) = 1.6
        _NoiseScale ("Escala do ruído (1/m)", Float) = 0.11
        _Erode ("Desfiar bordas", Range(0, 1)) = 0.45
        _Fade ("Fade", Range(0, 1)) = 1
        _LitColor ("Cor iluminada", Color) = (0.93, 0.97, 1.0, 1)
        _ShadowColor ("Cor na sombra", Color) = (0.42, 0.58, 0.74, 1)
        _CoreColor ("Núcleo", Color) = (0.35, 0.55, 0.75, 1)
        // Câmara fora do volume: Back + LEqual (o gelo à frente tapa a névoa); dentro: Front + Always.
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" }

        Pass
        {
            Name "IceMist"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE3D(_Noise); SAMPLER(sampler_Noise);
            CBUFFER_START(UnityPerMaterial)
            float4 _LitColor, _ShadowColor, _CoreColor;
            float _Radius, _Height, _Ring, _Density, _Absorb, _NoiseScale, _Erode, _Fade;
            CBUFFER_END

            #define STEPS 56

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            // ground: centro da face de baixo da caixa (o chão do volume).
            float Density(float3 p, float3 ground, float t)
            {
                float3 q = p - ground;
                float h = q.y;
                // O contorno não é um círculo: um ruído grande empurra o raio para dentro/fora.
                float warp = SAMPLE_TEXTURE3D_LOD(_Noise, sampler_Noise, float3(q.x, 0.0, q.z) * 0.07 + float3(0, t * 0.01, 0), 0).r;
                float r = length(q.xz) / _Radius + (warp - 0.5) * 0.35;
                // Forma: monte denso no centro (envolve o alvo) + anel a espalhar-se + camada rasteira até à borda.
                float mound = 1.0 - smoothstep(0.0, 0.55, r);
                float wave = exp(-pow((r - _Ring) / 0.2, 2.0)) * (0.55 + 0.6 * warp);
                float top = _Height * (0.75 * mound + 0.45 * wave + 0.1);
                float shape = (1.0 - smoothstep(top * 0.2, top, h)) * saturate(mound * 1.1 + wave * 0.9);
                shape = max(shape, (1.0 - smoothstep(0.05, 0.4, h)) * 0.55);
                shape *= 1.0 - smoothstep(0.75, 1.0, r);
                if (shape <= 0.001) return 0;

                // Ruído: a base sobe e roda devagar; o detalhe corre mais depressa e desfia as bordas.
                float3 uvw = q * _NoiseScale;
                float base = SAMPLE_TEXTURE3D_LOD(_Noise, sampler_Noise, uvw + float3(0.01, -0.035, 0.02) * t, 0).r;
                float detail = SAMPLE_TEXTURE3D_LOD(_Noise, sampler_Noise, uvw * 3.3 + float3(0.03, -0.09, -0.02) * t, 0).g;
                float d = saturate(shape * 1.7 - (1.0 - base) * 1.0);
                d = saturate(d - (1.0 - detail) * _Erode * (1.0 - saturate(d * 2.0)));
                return d * _Density;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 halfSize = 0.5 * float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), length(UNITY_MATRIX_M._m02_m12_m22));
                float3 center = UNITY_MATRIX_M._m03_m13_m23;
                float3 ground = center - float3(0, halfSize.y, 0);

                float3 ro = GetCameraPositionWS();
                float3 rd = normalize(i.positionWS - ro);
                float3 inv = 1.0 / rd;
                float3 a = (center - halfSize - ro) * inv, b = (center + halfSize - ro) * inv;
                float3 tmin = min(a, b), tmax = max(a, b);
                float t0 = max(max(max(tmin.x, tmin.y), tmin.z), 0.0);
                float t1 = min(min(tmax.x, tmax.y), tmax.z);

                // Parar no que é opaco (chão, personagens).
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float eye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float3 camFwd = -UNITY_MATRIX_V[2].xyz;
                t1 = min(t1, eye / max(dot(rd, camFwd), 1e-3));
                if (t1 <= t0) return 0;

                float stepLen = (t1 - t0) / STEPS;
                float jitter = InterleavedGradientNoise(i.positionCS.xy, 0);
                float time = _Time.y;
                Light main = GetMainLight();
                float3 L = normalize(main.direction + float3(0, 1.6, 0));

                float T = 1.0;
                float3 col = 0;
                [loop] for (int s = 0; s < STEPS; s++)
                {
                    float3 p = ro + rd * (t0 + (s + jitter) * stepLen);
                    float d = Density(p, ground, time);
                    if (d <= 0.002) continue;
                    // Sombra própria: quanto nevoeiro há entre este ponto e a luz.
                    float od = Density(p + L * 0.3, ground, time) * 0.3 + Density(p + L * 0.8, ground, time) * 0.5 + Density(p + L * 1.6, ground, time) * 0.8;
                    float lit = exp(-od * _Absorb);
                    float3 q = p - ground;
                    float h01 = saturate(q.y / _Height);
                    float3 c = lerp(_ShadowColor.rgb, _LitColor.rgb, saturate(lit * 0.8 + h01 * 0.3));
                    float r = length(q.xz) / _Radius;
                    // Energia mágica: brilho ciano junto ao chão e no centro, mais forte onde a névoa é fina.
                    c += _CoreColor.rgb * (exp(-r * 3.0) * 1.2 + 0.35) * exp(-q.y * 1.3);
                    float alpha = 1.0 - exp(-d * stepLen);
                    col += T * alpha * c;
                    T *= 1.0 - alpha;
                    if (T < 0.02) break;
                }
                float A = (1.0 - T) * _Fade;
                return half4(col * _Fade, A);
            }
            ENDHLSL
        }
    }
}
