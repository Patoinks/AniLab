Shader "KonoSuba/FireRing"
{
    Properties
    {
        _Age ("Tempo do impacto", Float) = 0
        _Fade ("Visibilidade", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+6" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "FireCommon.cginc"
            float _Age, _Fade;
            struct Varyings { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(appdata_base v)
            {
                Varyings o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o;
            }
            float4 Frag(Varyings i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0;
                float n = FireNoise(float3(p * 6.0, _Age * 3.5));
                float radius = length(p);
                float d = (radius - 0.73 - (n - 0.5) * 0.14) / 0.12;
                float ring = exp(-d * d) * (0.65 + n * 0.35);
                float tongues = exp(-pow((radius - 0.77) / 0.19, 2.0)) * smoothstep(0.43, 0.68, n);
                float border = 1.0 - smoothstep(0.91, 1.0, radius);
                float alpha = saturate((ring + tongues * 0.6) * border * _Fade);
                // A crista usa a mesma paleta compensada do volume, sem uma banda HDR branca.
                float heat = lerp(0.16, 0.78, saturate(ring * 0.85 + tongues * 0.35));
                return float4(FireColor(heat) * alpha, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
