// Bloom HDR e composição do fogo depois do grading da cena (AnimLabRenderFeature, URP).
// Passe 0: composição (cena em _BlitTexture); passe 1: extração dos brilhos do fogo; passe 2: descida da pirâmide.
// O fogo é pintado em valores sRGB (vindos do projeto em Gamma); na composição passa-se a linear para sair
// no ecrã com as mesmas cores.
Shader "Hidden/KonoSuba/FireComposite"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    TEXTURE2D(_FireFXTex);
    TEXTURE2D(_FireBloomNear);
    TEXTURE2D(_FireBloomMid);
    TEXTURE2D(_FireBloomWide);
    TEXTURE2D(_FireBloomHalo);
    float _FireExposure, _FireBloom;
    float4 _FireBloomStep;

    float4 FragComposite(Varyings i) : SV_Target
    {
        float2 uv = i.texcoord;
        float4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        float4 fire = SAMPLE_TEXTURE2D(_FireFXTex, sampler_LinearClamp, uv);
        float3 nearGlow = SAMPLE_TEXTURE2D(_FireBloomNear, sampler_LinearClamp, uv).rgb;
        float3 midGlow = SAMPLE_TEXTURE2D(_FireBloomMid, sampler_LinearClamp, uv).rgb;
        float3 wideGlow = SAMPLE_TEXTURE2D(_FireBloomWide, sampler_LinearClamp, uv).rgb;
        float3 haloGlow = SAMPLE_TEXTURE2D(_FireBloomHalo, sampler_LinearClamp, uv).rgb;
        float3 c = saturate(max(fire.rgb, 0.0) * _FireExposure);
        float a = saturate(fire.a);
        // Keep dark paint legible inside the silhouette while light spills into the surroundings.
        float3 glow = (nearGlow * 0.22 + midGlow * float3(0.65, 0.4, 0.12)
            + wideGlow * float3(1.05, 0.38, 0.06) + haloGlow * float3(0.85, 0.18, 0.015)) * _FireBloom;
        glow *= lerp(1.0, 0.08, a);
        float3 fireLight = c + (1.0 - saturate(c)) * (1.0 - exp(-glow));
        // O brilho pode cair fora do fogo (alpha 0): a parte do brilho soma-se à cena.
        float3 painted = SRGBToLinear(saturate(fireLight));
        return float4(painted + scene.rgb * (1.0 - a), scene.a);
    }

    float4 FragExtract(Varyings i) : SV_Target
    {
        float3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.texcoord).rgb * _FireExposure;
        float luminance = max(c.r, max(c.g, c.b));
        return float4(c * smoothstep(0.5, 1.1, luminance), 0.0);
    }

    float4 FragDown(Varyings i) : SV_Target
    {
        float2 uv = i.texcoord, d = _FireBloomStep.xy * 1.5;
        float3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb * 0.25;
        c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(d.x, d.y)).rgb * 0.125;
        c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-d.x, d.y)).rgb * 0.125;
        c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(d.x, -d.y)).rgb * 0.125;
        c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - d).rgb * 0.125;
        c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(d.x, 0)).rgb * 0.0625;
        c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(d.x, 0)).rgb * 0.0625;
        c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0, d.y)).rgb * 0.0625;
        c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0, d.y)).rgb * 0.0625;
        return float4(c, 0.0);
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
            #pragma fragment FragComposite
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragExtract
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDown
            ENDHLSL
        }
    }
}
