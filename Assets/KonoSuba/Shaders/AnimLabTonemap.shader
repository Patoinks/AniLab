// Tonemapper próprio do AnimLab (AnimLabTonemap.cs): curva de Lottes no canal máximo, escalada nos três canais
// (mantém o tom), com as altas luzes a rolar para branco e saturação final. Entrada HDR linear, saída 0..1 linear.
Shader "Hidden/KonoSuba/Tonemap"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    float4 _TonemapCurve;  // a (contraste), d (ombro), b, c
    float4 _TonemapParams; // exposição, dessaturação das altas luzes, saturação, —

    // Lottes 2016: y = x^a / (x^(a·d)·b + c)
    float Curve(float x)
    {
        x = max(x, 0.0);
        float a = _TonemapCurve.x, d = _TonemapCurve.y;
        return pow(x, a) / (pow(x, a * d) * _TonemapCurve.z + _TonemapCurve.w);
    }

    float4 Frag(Varyings i) : SV_Target
    {
        float4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.texcoord);
        float3 c = max(src.rgb, 0.0) * _TonemapParams.x;

        float peak = max(c.r, max(c.g, c.b));
        float mapped = Curve(peak);
        float3 keepHue = c * (mapped / max(peak, 1e-5));
        float3 perChannel = float3(Curve(c.r), Curve(c.g), Curve(c.b));
        // Só as luzes muito fortes passam a curva por canal (rolam para branco).
        float t = smoothstep(0.75, 1.0, mapped) * _TonemapParams.y;
        float3 o = lerp(keepHue, perChannel, t);

        float l = dot(o, float3(0.2126, 0.7152, 0.0722));
        o = lerp(l.xxx, o, _TonemapParams.z);
        return float4(saturate(o), src.a);
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
