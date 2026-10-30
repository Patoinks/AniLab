Shader "KonoSuba/FireTornadoStroke"
{
    Properties
    {
        _FlowTex ("Fine flame grain", 2D) = "gray" {}
        _Kind ("Flame / ink / spark", Float) = 0
        _Heat ("Maximum heat", Float) = 0.7
        _Seed ("Material variation", Float) = 0
        _Opacity ("Life", Float) = 1
        _Top ("Normalized height", Float) = 0
        _Ground ("Ground feeder", Float) = 0
        _FireTime ("Time", Float) = 0
    }
    CGINCLUDE
            #include "FireFXCommon.cginc"
            #include "FireTornadoFlow.cginc"
            float _Kind, _Heat, _Seed, _Opacity, _FireTime, _Top, _Ground;
            sampler2D _FlowTex;
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float4 screen:TEXCOORD1; float eye:TEXCOORD2; float2 metric:TEXCOORD3; };
            v2f vert(appdata_full v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy;
                o.metric = v.texcoord1.xy;
                o.screen = ComputeScreenPos(o.pos); o.eye = -UnityObjectToViewPos(v.vertex).z;
                return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float u = i.uv.x, v = i.uv.y;
                float3 p = float3(i.metric.x*(1.1+frac(_Seed*.73)*.55)-_FireTime*lerp(.22,1.1,_Ground),
                    i.metric.y*(13+frac(_Seed*.31)*4), _Seed*5.17);
                float bend = FireNoise(p * float3(.65,.48,1) + 13.4);
                p.y += (bend-.5)*.85;
                float broad = FireFbm(p);
                float detail = FireFbm(p * float3(2.8,3.6,1) + 23.1);
                float torn = FireNoise(p * float3(5.7,2.1,1) + 51.6);
                float fibres = FireFbm(p * float3(1.5,3.7,1) + 81.9);
                float2 grainUV=float2(i.metric.x*(.22+frac(_Seed*.67)*.18)-_FireTime*.16,
                    i.metric.y*(1.2+frac(_Seed*.39)*.8)) + float2(_Seed*.271,_Seed*.713);
                grainUV+=float2(bend-.5,broad-.5)*float2(.25,.18);
                float grainA=FirePaintFlow(_FlowTex,grainUV);
                float grainB=FirePaintFlow(_FlowTex,grainUV*float2(1.73,.79)+float2(.371,_Seed*.183)-float2(_FireTime*.08,0));
                float grain=grainA*.64+grainB*.36;
                float across = 1 - abs(u*2-1);
                float spine = u - .43 - (bend-.5)*.18;
                float centre = exp(-spine*spine*17);
                float filament = exp(-pow((spine + (broad-.5)*.35)*18,2)) * smoothstep(.31,.69,fibres);
                float edge = across - .19 + (broad-.5)*.78 + (detail-.5)*.38 - (1-grain)*.16 - _Top*.07;
                float aa = max(fwidth(edge)*1.1,.018);
                float alpha = smoothstep(-aa,aa,edge);
                // Narrow tears, concentrated on the tail and edges, rather than holes everywhere.
                alpha *= 1 - smoothstep(.52,.76,torn) * (1-centre*.55) * (1-v*.5);
                alpha *= smoothstep(0,.03,v) * (1-smoothstep(.95,1,v));
                float flameMask=grain*.65+broad*.35+centre*.16;
                float erosion=.27+_Top*.12+(1-centre)*.15;
                alpha*=smoothstep(erosion,erosion+.13,flameMask);
                float heat = .10 + _Heat*.45 + centre*.12 + filament*.24
                    + (broad-.5)*.24 + smoothstep(.35,.70,fibres)*.20 + (grain-.5)*.15 - (1-v)*.14 - _Top*.10 + _Ground*.08;
                heat = lerp(heat,.04+_Heat*.70+centre*.10+(broad-.5)*.16+(grain-.5)*.10,step(_Heat,.3));
                heat = saturate(heat);
                float3 color = FirePaletteRamp(heat) * (1 + pow(heat,9)*.65);
                if (_Kind > .5 && _Kind < 1.5)
                {
                    alpha = smoothstep(-aa,aa,edge-.025) * smoothstep(0,.045,v) * (1-smoothstep(.90,1,v));
                    alpha *= 1 - smoothstep(.69,.88,torn)*(1-v)*.8;
                    color = lerp(float3(.157,.09,.059),float3(.294,.063,0),broad*.65);
                    color += float3(.34,.035,.004)*pow(saturate(1-centre),3)*(.3+broad*.3);
                }
                if (_Kind > 1.5)
                {
                    alpha = pow(saturate(sin(u*UNITY_PI)),1.5) * pow(saturate(sin(v*UNITY_PI)),.6);
                    color = lerp(float3(1,.38,.05),float3(1,.94,.58),v)*2.1;
                }
                // Coverage is always zero at the geometric boundary, even where noise is positive.
                alpha *= smoothstep(0,.085,u)*(1-smoothstep(.915,1,u));
                alpha *= _Opacity * FireSceneVisibility(i.screen,i.eye,.012);
                return float4(color*alpha,alpha);
            }

            float4 fragDepth(v2f i):SV_Target
            {
                float coverage=frag(i).a;
                clip(coverage-.975);
                return float4(i.eye,0,0,1);
            }
    ENDCG
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        Pass
        {
            Name "FireColor"
            ZWrite Off ZTest LEqual Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
        Pass
        {
            Name "FireDepth"
            ZWrite On ZTest LEqual Blend Off ColorMask R
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragDepth
            ENDCG
        }
    }
}
