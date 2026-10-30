Shader "KonoSuba/FireTornadoGround"
{
    Properties { _FireTime ("Time",Float) = 0 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "FireFXCommon.cginc"
            #include "FireTornadoFlow.cginc"
            float _FireTime;
            struct v2f { float4 pos:SV_POSITION; float2 p:TEXCOORD0; float4 screen:TEXCOORD1; float eye:TEXCOORD2; };
            v2f vert(appdata_base v)
            {
                v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.p=v.texcoord.xy;
                o.screen=ComputeScreenPos(o.pos); o.eye=-UnityObjectToViewPos(v.vertex).z; return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float r=length(i.p), angle=atan2(i.p.y,i.p.x);
                float travel=-log(max(r,.15)/.9359)/.52;
                float label=angle-FireTurn(min(travel,0),1);
                float3 domain=float3(cos(label)*3.1,sin(label)*3.1,(travel-_FireTime)*5.7);
                float n=FireFbm(domain);
                float filaments=smoothstep(.54,.75,FireNoise(domain*float3(2.1,2.1,.7)+6.3));
                float field=exp(-r*r*.25)*(.72+n*.22);
                float streak=filaments*.14*smoothstep(.6,1.05,r)*(1-smoothstep(2.35,2.95,r));
                float alpha=(field+streak)*(1-smoothstep(2.5,3.02,r));
                float heat=.34+n*.24+streak*.9+exp(-r*r*1.1)*.25;
                float3 color=FirePaletteRamp(heat)*1.65;
                alpha*=FireSceneVisibility(i.screen,i.eye,.006);
                return float4(color*alpha,alpha);
            }
            ENDCG
        }
    }
}
