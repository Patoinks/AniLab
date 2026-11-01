// Brilho de parry: casca aditiva à volta da arma, com o contorno a brilhar (fresnel) para ler bem com bloom.
Shader "KonoSuba/ParryGlint"
{
    Properties
    {
        [HDR] _Color ("Cor", Color) = (3, 2.4, 1.1, 1)
        _Intensity ("Intensidade", Range(0, 2)) = 0
        _Inflate ("Afastamento da superfície (m)", Range(0, 0.03)) = 0.006
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+20" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
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
            float _Intensity, _Inflate;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float rim : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                // Um pouco para fora da arma: a casca não luta com a superfície original.
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                world += n * _Inflate;
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1));
                float3 view = normalize(_WorldSpaceCameraPos - world);
                o.rim = 1 - saturate(dot(n, view));
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // Corpo fraco, contorno forte: a lâmina "acende" sem esconder a forma.
                float k = 0.65 + 1.6 * i.rim * i.rim;
                return float4(_Color.rgb * k * _Intensity, 0);
            }
            ENDCG
        }
    }
}
