// Aplica a distorção do AnimLab (AnimLabRenderFeature): lê a cor da cena deslocada pelos desvios de ecrã que os
// materiais "AnimLabDistortion" escreveram em _DistortionTex (xy em UV). Uma separação cromática mínima nas zonas
// mais deslocadas dá ao ar quente e às ondas de choque um rebordo de refração.
Shader "Hidden/KonoSuba/DistortionApply"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    TEXTURE2D(_DistortionTex);

    float4 Frag(Varyings i) : SV_Target
    {
        float2 uv = i.texcoord;
        float2 offset = SAMPLE_TEXTURE2D(_DistortionTex, sampler_LinearClamp, uv).xy;
        float amount = length(offset);
        if (amount < 1e-5) return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        float2 split = offset * 0.12;
        float4 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset);
        c.r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset + split).r;
        c.b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset - split).b;
        return c;
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
