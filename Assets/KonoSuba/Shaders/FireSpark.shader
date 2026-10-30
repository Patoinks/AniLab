// Faúlhas da Bola de Fogo: partículas aditivas com ponto quente no centro. Em "Stretch" ficam riscos.
Shader "KonoSuba/FireSpark"
{
    Properties
    {
        _Intensity ("Intensidade", Float) = 5
    }
    SubShader
    {
        Tags { "Queue"="Transparent+7" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off
        Blend One One
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            float _Intensity;

            struct Attributes { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float r = length(i.uv * 2.0 - 1.0);
                float glow = saturate(1.0 - r);
                glow = glow * glow * (0.35 + 0.65 * glow);
                return float4(i.color.rgb * (i.color.a * glow * _Intensity), 0.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
