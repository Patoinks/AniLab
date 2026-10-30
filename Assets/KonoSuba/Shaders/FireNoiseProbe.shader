Shader "Hidden/KonoSuba/FireNoiseProbe"
{
    SubShader { Cull Off ZWrite Off ZTest Always Pass {
        CGPROGRAM
        #pragma target 3.0
        #pragma vertex vert_img
        #pragma fragment frag
        #include "UnityCG.cginc"
        #include "FireTornadoFlow.cginc"
        float4 frag(v2f_img i):SV_Target
        {
            float n=FireNoise(float3(i.uv*12,2.4));
            return float4(n,n,n,1);
        }
        ENDCG
    } }
}
