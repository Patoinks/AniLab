// Bola de fogo estilizada (referência "Level 16 Elements"), num quadrado virado para a câmara.
// Em unidades do núcleo (raio 1): núcleo amarelo quase branco ao centro, bandas nítidas para fora (amarelo →
// âmbar → laranja → vermelho), silhueta de chamas que rodopiam em espiral com bordas recortadas e vermelho-escuras,
// buracos no anel exterior (vê-se o fundo), traços escuros em espiral à volta e faúlhas a orbitar.
// Na direção _TailDir a forma estica numa cauda de línguas de fogo a correr para trás.
//   _TailDir  xy  direção da cauda no plano do quadrado (unitária)    _TailLen  comprimento (unidades)
//   _Extent       metade do lado do quadrado (unidades)                _Burst    0 inteira → 1 desfeita
//   _Spin         velocidade do rodopio
// Pré-multiplicado: o fogo emite (HDR) e tapa; os traços escuros tapam sem emitir; o brilho e as faúlhas somam.
Shader "KonoSuba/FireToon"
{
    Properties
    {
        _Intensity ("Intensidade", Float) = 1.0
        _Fade ("Opacidade", Range(0, 1)) = 1
        _Seed ("Semente", Float) = 0
        _TailDir ("Direção da cauda", Vector) = (0, 1, 0, 0)
        _TailLen ("Comprimento da cauda", Float) = 0.6
        _Extent ("Meio lado do quadrado", Float) = 2.6
        _Burst ("Desfazer", Range(0, 1)) = 0
        _Spin ("Rodopio", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "FireCommon.cginc"

            float _Intensity, _Fade, _Seed, _TailLen, _Extent, _Burst, _Spin;
            float4 _TailDir;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            // Degrau com a borda suavizada só o suficiente para não serrilhar (traço nítido de desenho).
            float Band(float x, float th)
            {
                float w = max(fwidth(x), 1e-4);
                return smoothstep(th - w, th + w, x);
            }

            float2 Rot(float2 p, float a)
            {
                float s = sin(a), c = cos(a);
                return float2(c * p.x - s * p.y, s * p.x + c * p.y);
            }

            float Hash2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float4 frag(v2f i) : SV_Target
            {
                const float TAU = 6.2831853;
                float2 p = (i.uv * 2.0 - 1.0) * _Extent;
                float t = _Time.y + _Seed;
                float r = length(p);
                float ang = atan2(p.y, p.x);

                // Forma em gota: círculo à frente, cauda que afina para trás.
                float2 td = normalize(_TailDir.xy + float2(0, 1e-5));
                float along = dot(p, td), perp = dot(p, float2(-td.y, td.x));
                float L = max(_TailLen, 0.001);
                float k = saturate(along / L);
                float width = pow(1.0 - k, 0.7);
                float R = along <= 0.0 ? r : abs(perp) / max(width, 0.05);
                if (along > L) R = 99.0;

                // Rodopio em espiral (a cabeça) e línguas a correr para trás (a cauda).
                float2 sp = Rot(p, r * 1.5 - t * 1.6 * _Spin);
                float nHead = FireFbm2(sp * 1.25 + float2(_Seed, -t * 0.7));
                float nTail = FireFbm2(float2(along * 1.5 - t * 4.5, perp * 2.6) + _Seed);
                float n = lerp(nHead, nTail, k);
                float boundary = 1.3 + (n - 0.5) * (1.0 + k * 1.4); // a cauda parte-se em línguas
                float edge = boundary - R;                  // > 0 dentro das chamas

                // Calor: 1 no centro, 0 na silhueta; a cauda e a explosão a desfazer-se arrefecem.
                // As bandas seguem o rodopio (não anéis concêntricos).
                float nBand = FireFbm2(Rot(p, r * 2.4 - t * 2.0 * _Spin) * 1.6 + _Seed * 1.7);
                float h = saturate(1.0 - R / max(boundary, 0.2) + (nBand - 0.5) * 0.55);
                h *= (1.0 - 0.45 * k) * (1.0 - 0.45 * _Burst);

                // Buracos no anel exterior e na cauda (vê-se o fundo); crescem quando a bola se desfaz.
                float nHole = FireFbm2(sp * 2.4 + float2(-t * 0.6, 3.1 + _Seed));
                float cut = nHole + h * 1.3 - 0.38 - k * 0.5 - _Burst * 1.1;
                float aFlame = Band(edge, 0.0) * Band(cut, 0.0);

                // Bandas de cor nítidas (saturação já a contar com o grading da cena).
                // Muito saturadas de propósito: o grading da cena tira saturação e o bloom clareia.
                float3 col = float3(0.42, 0.03, 0.0);                       // borda vermelho-escura
                col = lerp(col, float3(1.0, 0.14, 0.0), Band(edge, 0.07));  // vermelho-laranja
                col = lerp(col, float3(1.0, 0.32, 0.0), Band(h, 0.2));      // laranja
                col = lerp(col, float3(1.0, 0.52, 0.0), Band(h, 0.42));     // âmbar
                col = lerp(col, float3(1.0, 0.76, 0.04), Band(h, 0.64));    // amarelo
                col = lerp(col, float3(1.0, 0.92, 0.45), Band(h, 0.88));    // núcleo
                col *= lerp(1.0, 1.6, smoothstep(0.6, 1.0, h)) * _Intensity;

                // Brilho quente à volta (só soma).
                float3 glow = float3(1.0, 0.3, 0.02) * exp(-max(R - boundary, 0.0) * 3.5) * 0.28 * _Intensity * (1.0 - k);

                // Traços escuros em espiral à volta da cabeça, aos bocados.
                float u = (ang + r * 2.2 - t * 1.4 * _Spin + _Seed) * 3.0 / TAU;
                float f = abs(frac(u) - 0.5);
                float lineW = 0.03 + 0.05 * saturate(1.6 - r);
                float aStroke = (1.0 - Band(f, lineW))
                              * Band(r, 0.75) * (1.0 - Band(r, 1.85))
                              * Band(FireFbm2(float2(u * 2.0, r * 1.7) + _Seed), 0.5)
                              * (1.0 - k) * (1.0 - _Burst) * 0.9;

                // Faúlhas a orbitar e a sair.
                float sa = ang / TAU * 26.0;
                float sr = r * 3.2 - t * 1.5;
                float2 cell = floor(float2(sa, sr));
                float2 fc = frac(float2(sa, sr)) - 0.5;
                float rnd = Hash2(cell + _Seed);
                float spark = (rnd > 0.88 ? 1.0 : 0.0) * (1.0 - Band(length(fc * float2(1.0, 2.2)), 0.12))
                            * Band(r, 1.15) * (1.0 - Band(r, 2.2));
                float3 sparks = float3(1.0, 0.55, 0.05) * spark * 2.0 * _Intensity;

                // Composição (pré-multiplicada).
                float4 o = float4(glow, 0);
                o = float4(col * aFlame + o.rgb * (1.0 - aFlame), aFlame);
                float3 dark = float3(0.14, 0.025, 0.01);
                o = float4(dark * aStroke + o.rgb * (1.0 - aStroke), aStroke + o.a * (1.0 - aStroke));
                o.rgb += sparks;
                o *= _Fade;
                return any(isnan(o)) ? 0 : max(o, 0);
            }
            ENDCG
        }
    }
}
