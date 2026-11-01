// Rasto de cleave: crescente aditivo do golpe, da cor A (cauda) à cor B (lâmina), mais forte junto à ponta.
// uv.x: 0 = cauda (o início do arco) → 1 = a lâmina agora; uv.y: 0 = raiz da lâmina → 1 = ponta.
Shader "KonoSuba/CleaveTrail"
{
    Properties
    {
        [HDR] _ColorA ("Cor da cauda", Color) = (1, 1, 1, 1)
        [HDR] _ColorB ("Cor junto à lâmina", Color) = (1, 1, 1, 1)
        _Intensity ("Intensidade", Range(0, 4)) = 1
        _Fade ("Desvanecer (0 = cheio)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+15" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _ColorA, _ColorB;
            float _Intensity, _Fade;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float hash(float n) { return frac(sin(n * 91.3458) * 47453.5453); }

            float4 frag(v2f i) : SV_Target
            {
                float x = i.uv.x, y = i.uv.y;
                // Ao longo do golpe: nasce na cauda e enche até à lâmina; o desvanecer come a cauda primeiro.
                float along = smoothstep(_Fade, 1.0, x) * (1.0 - _Fade);
                // Atravessado: quase nada na raiz, cheio na ponta, com um gume fino e brilhante.
                float across = smoothstep(0.0, 0.85, y) * (0.55 + 0.45 * smoothstep(0.75, 1.0, y));
                float edge = smoothstep(0.86, 0.985, y) * (1.0 - smoothstep(0.985, 1.0, y));
                // Estrias finas ao longo do arco (o ar cortado), fixas no rasto.
                float row = floor(y * 22.0);
                float streak = lerp(0.75, 1.15, hash(row)) * (0.85 + 0.15 * sin(y * 140.0));
                float3 col = lerp(_ColorA.rgb, _ColorB.rgb, x);
                // Núcleo do gume um pouco mais branco (lê-se com bloom sem perder a cor).
                col = lerp(col, float3(1, 1, 1) * max(col.r, max(col.g, col.b)), edge * 0.35);
                float k = along * (across * streak + edge * 1.6) * _Intensity;
                return float4(col * k, 0);
            }
            ENDCG
        }
    }
}
