// Skybox do vazio: quase preto em cima, faixa de névoa azulada no horizonte e estrelas procedurais a cintilar.
Shader "KonoSuba/VoidSky"
{
    Properties
    {
        _TopColor ("Cor do topo", Color) = (0.005, 0.007, 0.014, 1)
        _HorizonColor ("Cor do horizonte", Color) = (0.07, 0.08, 0.10, 1)
        _BottomColor ("Cor de baixo", Color) = (0.03, 0.035, 0.045, 1)
        _HorizonWidth ("Largura da faixa", Range(0.01, 1)) = 0.22
        _StarScale ("Densidade da grelha", Float) = 90
        _StarChance ("Probabilidade de estrela", Range(0, 1)) = 0.06
        _StarSize ("Tamanho das estrelas", Range(0.02, 0.4)) = 0.12
        _StarColor ("Cor das estrelas", Color) = (0.55, 0.85, 1.0, 1)
        _StarBrightness ("Brilho das estrelas", Float) = 2.5
        _TwinkleSpeed ("Velocidade do cintilar", Float) = 1.5
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _TopColor, _HorizonColor, _BottomColor, _StarColor;
            float _HorizonWidth, _StarScale, _StarChance, _StarSize, _StarBrightness, _TwinkleSpeed;

            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float3 Hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            // Estrelas: grelha 3D à volta da esfera, no máximo uma estrela por célula.
            float Stars(float3 dir, float scale, float chance, float size)
            {
                float3 p = dir * scale;
                float3 cell = floor(p);
                float3 h = Hash33(cell);
                if (h.x > chance) return 0;
                float3 center = cell + 0.25 + 0.5 * Hash33(cell + 17.0);
                float dist = length(p - center);
                float s = size * (0.4 + h.y);
                float twinkle = 0.55 + 0.45 * sin(_Time.y * _TwinkleSpeed * (0.5 + h.z * 2.0) + h.y * 40.0);
                return smoothstep(s, 0.0, dist) * twinkle * (0.3 + h.z);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = saturate(d.y / _HorizonWidth);
                float down = saturate(-d.y / _HorizonWidth);
                float3 col = lerp(_HorizonColor.rgb, _TopColor.rgb, pow(up, 0.6));
                col = lerp(col, _BottomColor.rgb, pow(down, 0.6));

                // Duas camadas de estrelas (pequenas e muitas + raras e maiores), só acima do horizonte.
                float stars = Stars(d, _StarScale, _StarChance, _StarSize)
                            + Stars(d, _StarScale * 0.35, _StarChance * 0.5, _StarSize * 0.8) * 1.5;
                stars *= smoothstep(-0.02, 0.15, d.y);
                col += _StarColor.rgb * stars * _StarBrightness;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
