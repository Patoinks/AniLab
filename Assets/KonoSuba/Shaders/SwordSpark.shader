// Faíscas e clarões da espada: aditivo, cor por objeto (_Color já traz a intensidade).
// O clarão (esfera) desvanece nas bordas pelo ângulo com a câmara, para não se ver o contorno da esfera.
Shader "KonoSuba/SwordSpark"
{
    Properties
    {
        _Color ("Cor", Color) = (1, 1, 1, 1)
        _Edge ("Brilho nas bordas", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+30" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Edge;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float facing : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                float3 view = normalize(WorldSpaceViewDir(v.vertex));
                o.facing = saturate(dot(n, view));
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // Losangos finos: quase sempre de lado, por isso o mínimo é alto (0.55); o clarão usa 0.
                float soft = lerp(_Edge, 1.0, i.facing * i.facing);
                return float4(_Color.rgb * soft, 0);
            }
            ENDCG
        }
    }
}
