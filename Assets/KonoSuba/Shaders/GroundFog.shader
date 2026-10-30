// Nevoeiro rasteiro em ecrã (GroundFog.cs, desenhado pela AnimLabRenderFeature do URP). Em vez de raymarch com jitter (grão) ou de quadrados com textura
// (aspeto de PNG), divide a camada de nevoeiro em fatias horizontais e, para cada fatia, calcula analiticamente
// o troço do raio que lá passa e amostra o ruído no meio desse troço. É determinístico por pixel: sem grão,
// sem bordas, e com paralaxe real entre camadas.
Shader "Hidden/KonoSuba/GroundFog"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "FogField.cginc"

    #define LAYERS 8

    TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

    float4 _CornerBL, _CornerBR, _CornerTL, _CornerTR, _CamPos;
    float4 _FogShape;        // altura, densidade, queda com a altura, distância máxima
    float4 _FogNoiseParams;  // escala, cobertura, suavidade, esticar
    float4 _FogWindParams2;  // dir.x, dir.z, velocidade, enrolar
    float4 _FogTint, _FogGlow;
    float4 _FogMisc;         // raio do brilho, névoa de base, tempo
    float4 _LightningFlash;  // clarão do Raio: xyz posição, w intensidade (global)
    float4 _LightningColor;
    TEXTURE2D(_LightningFogGlow); SAMPLER(sampler_LightningFogGlow); // brilho dos raios por baixo (LightningFogGlow)
    float4 _LightningFogRect;  // x mín., z mín., tamanho, tom geral (0..1)
    float4 _FogDetail;       // distância onde o detalhe começa / acaba de se diluir

    float N(float2 uv) { return SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, uv, 0).r; }

    // Fluxo sem divergência (curl do ruído): o fumo roda em vez de esticar.
    float2 CurlFlow(float2 p)
    {
        const float e = 1.0 / 128.0;
        float n0 = N(p);
        float2 curl = float2(N(p + float2(0, e)) - n0, n0 - N(p + float2(e, 0))) / e;
        return curl / (1.0 + length(curl));
    }

    // detail: 1 = fiapos visíveis, 0 = névoa uniforme. Ao longe o raio atravessa tanto nevoeiro que o
    // detalhe se média; amostrar o ruído lá só produz cintilação ("sujidade de pixels").
    float Density(float3 p, float hN, float time, float detail)
    {
        float profile = exp(-hN * _FogShape.z);
        float clear = FogClearing(p.xz);
        if (detail <= 0.001)
            return _FogShape.y * profile * lerp(_FogMisc.y, 1.0, 0.5) * clear;

        float2 wind = _FogWindParams2.xy;
        float2 perp = float2(-wind.y, wind.x);
        float scale = _FogNoiseParams.x;

        // Coordenadas alinhadas com o vento (esticadas no comprimento) que derivam com ele.
        float2 q = float2(dot(p.xz, wind) * _FogNoiseParams.w, dot(p.xz, perp)) * scale;
        q.x -= time * _FogWindParams2.z * scale;
        q += hN * float2(0.17, 0.43); // cada fatia é uma secção diferente do campo

        // Flow map em duas fases: a estrutura enrola-se continuamente, sem saltos nem esticões.
        float2 flow = CurlFlow(q * 0.35 + time * 0.01) * _FogWindParams2.w;
        float ft = time * 0.07 + hN * 0.3;
        float p0 = frac(ft), p1 = frac(ft + 0.5), w = abs(1.0 - 2.0 * p0);
        float n = lerp(N(q - flow * p0), N(q - flow * p1 + float2(0.37, 0.21)), w) * 0.65
                + lerp(N(q * 2.7 - flow * p0 * 1.6 + 0.3), N(q * 2.7 - flow * p1 * 1.6 + 0.71), w) * 0.35;

        float wisps = smoothstep(_FogNoiseParams.y, _FogNoiseParams.y + _FogNoiseParams.z, n);
        wisps = lerp(0.5, wisps, detail);
        return _FogShape.y * profile * lerp(_FogMisc.y, 1.0, wisps) * clear;
    }

    float3 Lighting(float3 p, float hN)
    {
        float2 c = p.xz;
        float glow = exp(-dot(c, c) / (_FogMisc.x * _FogMisc.x));
        // O clarão do Raio ilumina o nevoeiro por instantes (mais em baixo, junto à descarga).
        float2 fd = p.xz - _LightningFlash.xz;
        float flash = _LightningFlash.w * exp(-dot(fd, fd) / 45.0) * (1.2 - 0.6 * hN);
        // Os raios no chão tingem o nevoeiro por cima deles (mais em baixo), e toda a névoa puxa um pouco para roxo
        // enquanto a descarga dura.
        float2 guv = (p.xz - _LightningFogRect.xy) / max(_LightningFogRect.z, 1e-3);
        float lit = all(guv > 0.0) && all(guv < 1.0)
                  ? SAMPLE_TEXTURE2D_LOD(_LightningFogGlow, sampler_LightningFogGlow, guv, 0).r : 0.0;
        float3 baseCol = (_FogTint.rgb + _FogGlow.rgb * glow) * (0.85 + 0.3 * hN);
        float3 purple = _LightningColor.rgb * dot(baseCol, float3(0.3, 0.5, 0.2)) * 1.6;
        baseCol = lerp(baseCol, purple, saturate(_LightningFogRect.w * 0.45 + lit * 0.8 * (1.1 - 0.6 * hN)));
        return baseCol + _LightningColor.rgb * (flash * 0.18 + lit * 0.35 * (1.2 - 0.7 * hN));
    }

    float4 Frag(Varyings i) : SV_Target
    {
        float4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.texcoord);
        float rawDepth = SampleSceneDepth(i.texcoord);
        float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

        float3 dir = lerp(lerp(_CornerBL.xyz, _CornerBR.xyz, i.texcoord.x),
                          lerp(_CornerTL.xyz, _CornerTR.xyz, i.texcoord.x), i.texcoord.y);
        float3 ro = _CamPos.xyz;
        float3 rd = normalize(dir);
        float maxT = min(eyeDepth * length(dir), _FogShape.w);

        float H = _FogShape.x;
        float time = _FogMisc.z;
        float tau = 0, sumW = 0;
        float3 sumL = 0;

        [unroll]
        for (int k = 0; k < LAYERS; k++)
        {
            float y0 = H * k / LAYERS;
            float y1 = H * (k + 1) / LAYERS;
            float t0, t1;
            if (abs(rd.y) < 1e-4)
            {
                if (ro.y < y0 || ro.y > y1) continue;
                t0 = 0; t1 = maxT;
            }
            else
            {
                float ta = (y0 - ro.y) / rd.y;
                float tb = (y1 - ro.y) / rd.y;
                t0 = max(min(ta, tb), 0);
                t1 = min(max(ta, tb), maxT);
            }
            if (t1 <= t0) continue;

            float hN = (k + 0.5) / LAYERS;
            float3 p = ro + rd * (0.5 * (t0 + t1));
            float tm = 0.5 * (t0 + t1);
            float detail = (1.0 - smoothstep(_FogDetail.x, _FogDetail.y, tm))
                         * (1.0 - smoothstep(_FogDetail.x * 0.5, _FogDetail.y * 0.5, t1 - t0));
            float sigma = Density(p, hN, time, detail);
            float od = sigma * (t1 - t0);
            tau += od;
            sumL += Lighting(p, hN) * od;
            sumW += od;
        }

        float T = exp(-tau);
        float3 fogCol = sumW > 1e-5 ? sumL / sumW : 0;
        return float4(lerp(fogCol, src.rgb, T), src.a);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
