// Amostra de cor plana pelo caminho do fogo (teste da etapa 1): escreve a cor tal como está (sRGB, projeto em
// Gamma) com cobertura total, para se medir no ecrã se sai igual depois da composição.
Shader "KonoSuba/FireSwatch"
{
    Properties
    {
        _Color ("Cor", Color) = (1, 0.36, 0.1, 1)
    }
    CGINCLUDE
            #include "FireFXCommon.cginc"

            fixed4 _Color;

            struct v2f { float4 pos : SV_POSITION; float4 screenPos : TEXCOORD0; float eye : TEXCOORD1; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.screenPos = ComputeScreenPos(o.pos);
                o.eye = -UnityObjectToViewPos(v.vertex).z;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float a = FireSceneVisibility(i.screenPos, i.eye, 0.02) * _Color.a;
                return float4(_Color.rgb * a, a);
            }
    ENDCG

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        // Passe 0: faces de trás; passe 1: faces da frente (o FireFX intercala-os pela profundidade).
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }
}
