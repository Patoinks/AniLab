// A true 3D emitting medium, bounded by a mesh. No camera-facing geometry or tiled image.
Shader "KonoSuba/FireFlame"
{
    Properties
    {
        _Alpha ("Density", Range(0,1)) = 0.98
        _Core ("Legacy core setting", Float) = 0
        _Emission ("HDR emission", Float) = 0.55
        _Height ("Height", Float) = 4.9
        _FireTime ("Time", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One OneMinusSrcAlpha ZWrite Off Cull Front
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "FireFXCommon.cginc"
            #include "FireTornadoFlow.cginc"
            float _Alpha, _Core, _Emission, _Height, _FireTime;
            sampler2D _FireSolidDepth;
            struct v2f { float4 pos:SV_POSITION; float3 local:TEXCOORD0; float4 screen:TEXCOORD1; float eye:TEXCOORD2; };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.local = v.vertex.xyz;
                o.pos=UnityObjectToClipPos(v.vertex);
                o.screen=ComputeScreenPos(o.pos); o.eye=-UnityObjectToViewPos(v.vertex).z;
                return o;
            }
            float BodyRadius(float h)
            {
                return .66+.34*exp(-max(0,h)*3.8)+.36*smoothstep(1.25,4.7,h);
            }
            float4 Medium(float3 position)
            {
                float h=position.y, top=saturate(h/_Height);
                float3 q=position-FireAxis(h,_FireTime);
                float a=atan2(q.z,q.x);
                float r=length(q.xz)/FireRadialBend(a,h,_FireTime), radius=BodyRadius(h);
                float lane=r/radius;
                float border=1-smoothstep(.32,.72,lane);
                float envelope=smoothstep(0,.10,h)*(1-smoothstep(.72,1,top));
                if (border*envelope < .002) return 0;
                // The circular coordinate shrinks to zero at the axis, so there is no angular
                // singularity or artificial bundle of radial texture fibres in the heart.
                float3 p=FireDomain(a,h,max(.44,lane),_FireTime,r*3.4);
                p.z*=.72;
                float warp=FireNoise(p*.63+7.4);
                p.xy+=(warp-.5)*.85;
                float n=FireNoise(p)*.65+FireNoise(p*2.07+17.4)*.35;
                float fine=FireNoise(p*float3(3.1,3.1,1.3)+32.8);
                float core=exp(-r*r/ (.12+.045*(1-top)));
                float flames=smoothstep(.32,.75,n);
                float density=(pow(flames,1.5)*6.5+core*2.5)*border*envelope*_Alpha;
                // Cooler surfaces enclose a broken incandescent interior.
                float heat=.25+flames*.53+smoothstep(.53,.78,fine)*.15+core*.30-top*.07;
                float3 color=FirePaletteRamp(saturate(heat));
                color*=1+pow(saturate(heat),8)*_Emission;
                return float4(color,density);
            }
            float4 frag(v2f i):SV_Target
            {
                float3 eye=mul(unity_WorldToObject,float4(_WorldSpaceCameraPos,1)).xyz;
                float3 ray=normalize(i.local-eye);
                float scene=LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture,UNITY_PROJ_COORD(i.screen)));
                scene=min(scene,tex2Dproj(_FireSolidDepth,UNITY_PROJ_COORD(i.screen)).r);
                float viewStep=-mul((float3x3)UNITY_MATRIX_MV,ray).z;
                float available=min(3.05,max(0,(scene-i.eye)/max(.02,viewStep)));
                float stepSize=3.05/48;
                float3 samplePosition=i.local+ray*(stepSize*.5);
                float4 sum=0;
                [loop] for (int s=0;s<48;s++)
                {
                    if (s*stepSize>available || sum.a>.994) break;
                    float4 medium=Medium(samplePosition);
                    float alpha=1-exp(-medium.a*stepSize);
                    sum.rgb+=(1-sum.a)*medium.rgb*alpha;
                    sum.a+=(1-sum.a)*alpha;
                    samplePosition+=ray*stepSize;
                }
                return sum;
            }
            ENDCG
        }
    }
}
