Shader "KonoSuba/FireHitGlow"
{
    Properties { _Fade ("Calor residual", Range(0,1)) = 1 }
    SubShader
    {
        Tags { "Queue"="Transparent+7" "RenderType"="Transparent" "IgnoreProjector"="True" }
        ZWrite Off Blend One One
        Offset -1, -1
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            #include "FireCommon.cginc"
            float _Fade;
            struct Varyings { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; float3 view : TEXCOORD1; };
            Varyings Vert(appdata_base v)
            {
                Varyings o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 w = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.view = _WorldSpaceCameraPos - w;
                return o;
            }
            float4 Frag(Varyings i) : SV_Target
            {
                float rim = pow(1.0 - saturate(dot(normalize(i.normal), normalize(i.view))), 2.0);
                return float4(FireColor(0.45) * (0.10 + rim * 0.55) * _Fade, 0);
            }
            ENDCG
        }
    }
    Fallback Off
}
