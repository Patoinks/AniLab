// Cristal facetado e luminoso: cada face recebe um tom diferente (normais planas) e um rim de fresnel.
Shader "KonoSuba/Crystal"
{
    Properties
    {
        _Color ("Cor", Color) = (0.35, 0.72, 1.0, 1)
        _RimColor ("Cor do contorno", Color) = (0.8, 0.95, 1.0, 1)
        _Intensity ("Intensidade", Float) = 2.2
        _Alpha ("Opacidade", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _Color, _RimColor;
            float _Intensity, _Alpha;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = WorldSpaceViewDir(v.vertex);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 v = normalize(i.viewDir);
                float facet = dot(n, normalize(float3(0.3, 0.8, -0.45))) * 0.5 + 0.5;
                float fres = pow(1.0 - saturate(dot(n, v)), 2.0);
                float3 col = (_Color.rgb * (0.3 + 0.9 * facet * facet) + _RimColor.rgb * fres) * _Intensity;
                fixed4 c = fixed4(col * _Alpha, _Alpha);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, fixed4(0, 0, 0, 0));
                return c;
            }
            ENDCG
        }
    }
}
