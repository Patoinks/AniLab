// Material de distorção do AnimLab: não aparece na imagem; escreve desvios de ecrã (xy em UV) no buffer de
// distorção (passe "AnimLabDistortion" da AnimLabRenderFeature), que depois refrata a cena.
// O desvio é o gradiente de um ruído que corre sobre a malha:
//   _Mode 0 (ar quente): o ruído sobe ao longo de uv.y;
//   _Mode 1 (vento): o ruído gira à volta (uv.x) e sobe devagar;
//   _Mode 2 (onda de choque): anel — o desvio empurra para fora a partir do centro do ecrã do objeto.
// Mais forte de frente (fresnel invertido), apaga nas pontas (uv.y) e atrás de geometria (profundidade da cena).
// _Strength é o desvio máximo em UV a 10 m; mais perto desloca mais.
Shader "KonoSuba/Distortion"
{
    Properties
    {
        _Strength ("Força (UV a 10 m)", Float) = 0.006
        _Scale ("Escala do ruído (u, v)", Vector) = (6, 3, 0, 0)
        _Speed ("Velocidade (u, v)", Vector) = (0, 0.8, 0, 0)
        _Mode ("Modo (0 ar quente, 1 vento, 2 onda)", Float) = 0
        _Fade ("Desvanecer", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Distortion"
            Tags { "LightMode" = "AnimLabDistortion" }
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            float _Strength, _Mode, _Fade;
            float4 _Scale, _Speed;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screen : TEXCOORD1;
                float eye : TEXCOORD2;
                float facing : TEXCOORD3;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.screen = ComputeScreenPos(o.pos);
                o.eye = -UnityObjectToViewPos(v.vertex).z;
                float3 n = UnityObjectToWorldNormal(v.normal);
                float3 view = normalize(WorldSpaceViewDir(v.vertex));
                o.facing = abs(dot(n, view));
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            float Field(float2 p) { return Noise(p) * 0.65 + Noise(p * 2.1 + 3.7) * 0.35; }

            float4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                float2 uv = i.uv;
                float2 p = uv * _Scale.xy - t * _Speed.xy;
                const float e = 0.15;
                float n0 = Field(p);
                float2 grad = float2(Field(p + float2(e, 0)) - n0, Field(p + float2(0, e)) - n0) / e;

                float ends = smoothstep(0.0, 0.15, uv.y) * smoothstep(1.0, 0.7, uv.y);
                float mask = i.facing * i.facing * ends;
                if (_Mode > 1.5)
                {
                    // Onda de choque: anel fino a meio da largura (uv.y radial), desvio para fora.
                    mask = exp(-pow((uv.y - 0.5) * 6.0, 2.0));
                    float2 sp = i.screen.xy / i.screen.w;
                    float4 centre = UnityObjectToClipPos(float4(0, 0, 0, 1));
                    float2 c = ComputeScreenPos(centre).xy / centre.w;
                    grad = normalize(sp - c + 1e-5) * 1.5 + grad * 0.3;
                }

                // Atrás de geometria não há ar a distorcer.
                float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
                mask *= saturate((scene - i.eye) / 0.3);

                float strength = _Strength * 10.0 / max(i.eye, 1.0);
                return float4(grad * strength * mask * _Fade, 0, 0);
            }
            ENDCG
        }
    }
}
