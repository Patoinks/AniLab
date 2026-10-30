// Aura de mana na película de água à volta de quem canaliza: um brilho azul suave, aditivo, com braços que
// rodam devagar para dentro (a mana a puxar a água). _Intensity é posto pelo script (aparece e some).
Shader "KonoSuba/ManaAura"
{
    Properties
    {
        _Color ("Cor da mana", Color) = (0.35, 0.6, 1, 1)
        _Intensity ("Intensidade", Range(0, 2)) = 0
        _Arms ("Braços", Float) = 7
        _Spin ("Rotação", Float) = 0.6
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Intensity, _Arms, _Spin;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy * 2.0 - 1.0;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float d = length(i.uv);
                float a = atan2(i.uv.y, i.uv.x);
                float t = _Time.y;
                // Espiral que roda para dentro, com uma segunda camada mais lenta para não parecer mecânica.
                float s1 = 0.5 + 0.5 * sin(a * _Arms + d * 16.0 + t * _Spin * 4.0);
                float s2 = 0.5 + 0.5 * sin(a * (_Arms - 3.0) - d * 11.0 + t * _Spin * 2.3 + 1.7);
                float pattern = 0.6 + 0.4 * s1 * (0.5 + 0.5 * s2);
                float falloff = smoothstep(1.0, 0.15, d) * (0.55 + 0.45 * smoothstep(0.0, 0.35, d));
                float flicker = 0.9 + 0.1 * sin(t * 7.3) * sin(t * 3.1 + 0.6);
                return fixed4(_Color.rgb * pattern * falloff * flicker * _Intensity * 0.22, 1);
            }
            ENDCG
        }
    }
}
