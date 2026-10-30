// Raio em estilo anime (AnimeLightning): núcleo branco nítido, contorno ciano e halo azul largo (aditivo, HDR
// para o bloom). Cada ponto do raio é uma fita virada para a câmara, alargada no vertex shader; a largura vem
// em metros (com um mínimo em píxeis para não desaparecer ao longe).
// Duas partes (_Part, por MaterialPropertyBlock), cada uma com a sua malha:
//   0 = traço: núcleo + contorno numa fita estreita, sobre o caminho detalhado (todas as dobras orgânicas);
//   1 = halo: brilho largo sobre o caminho suavizado — assim o halo não se dobra sobre si próprio e não
//       acumula brilho nas dobras pequenas (o raio não vira uma "minhoca" de luz).
// Por vértice: posição do ponto, normal = tangente do raio, uv0 = (s ao longo do ramo 0..1, lado ±1),
// uv1 = (largura do núcleo em m, ID do ramo 0..1).
// MaterialPropertyBlock: _Flash = brilho do golpe atual (re-cair); _Thin afina os fios e _Erode parte-os aos
// bocados ao longo do comprimento (o apagar: o raio desaparece a partir-se, não a escurecer).
Shader "KonoSuba/LightningAnime"
{
    Properties
    {
        [HDR] _Core ("Núcleo", Color) = (1, 1, 1, 1)
        [HDR] _Outline ("Contorno", Color) = (0.08, 0.62, 1, 1)
        [HDR] _Halo ("Halo", Color) = (0.03, 0.22, 1, 1)
        _CoreIntensity ("Intensidade do núcleo", Float) = 6
        _OutlineIntensity ("Intensidade do contorno", Float) = 2.2
        _HaloIntensity ("Intensidade do halo", Float) = 0.9
        _LineScale ("Largura do traço (× núcleo)", Float) = 2.6
        _HaloScale ("Largura do halo (× núcleo)", Float) = 9
        _MinPixels ("Núcleo mínimo (px)", Float) = 2
        _Part ("Parte (0 traço, 1 halo)", Float) = 0
        _Flash ("Clarão", Float) = 1
        _Thin ("Afinar", Range(0, 1)) = 1
        _Erode ("Erosão ao longo dos fios", Range(0, 1.1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Bolt"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Core, _Outline, _Halo;
            float _CoreIntensity, _OutlineIntensity, _HaloIntensity, _LineScale, _HaloScale, _MinPixels, _Part, _Flash, _Thin, _Erode;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float side : TEXCOORD0; float s : TEXCOORD1; float id : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float3 t = normalize(TransformObjectToWorldDir(v.normalOS));
                float3 view = normalize(GetCameraPositionWS() - p);
                float3 across = normalize(cross(t, view) + 1e-5);
                // Núcleo com um mínimo em píxeis (ao longe continua a ler-se).
                float eye = max(-TransformWorldToView(p).z, 0.01);
                float pixel = 2.0 * eye / (_ScreenParams.y * UNITY_MATRIX_P._m11);
                float core = max(v.uv1.x * _Thin, pixel * _MinPixels);
                float halfWidth = core * (_Part > 0.5 ? _HaloScale : _LineScale) * 0.5;
                p += across * v.uv0.y * halfWidth;
                o.positionCS = TransformWorldToHClip(p);
                o.side = v.uv0.y;
                o.s = v.uv0.x;
                o.id = v.uv1.y;
                return o;
            }

            float Hash1(float n) { return frac(sin(n) * 43758.5453); }

            // Ruído contínuo ao longo do fio (sem degraus: não deixa riscas transversais).
            float Noise1(float x)
            {
                float i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(Hash1(i), Hash1(i + 1.0), f);
            }

            float4 frag(Varyings i) : SV_Target
            {
                // Apagar: pedaços do fio somem (o padrão salta a cada instante, a piscar).
                if (_Erode > 0.0)
                {
                    float e = Noise1(i.s * 22.0 + i.id * 57.0 + floor(_Time.y * 24.0) * 3.1) * 0.7 + Noise1(i.s * 70.0 + i.id * 13.0) * 0.3;
                    clip(e - _Erode);
                }
                float x = abs(i.side);   // 0 no centro, 1 na borda da fita
                float flicker = 0.75 + 0.25 * sin(_Time.y * 90.0 + i.id * 61.0) * sin(_Time.y * 37.0 + i.id * 17.0);
                float3 col;
                if (_Part > 0.5)
                {
                    float halo = exp(-pow(x / 0.35, 2.0)) * 0.7 + exp(-x * 4.0) * 0.2;
                    col = _Halo.rgb * halo * _HaloIntensity;
                }
                else
                {
                    // Borda do núcleo irregular ao longo do fio (contínua, não é um tubo liso).
                    float wob = Noise1(i.s * 90.0 + i.id * 97.0) * 0.6 + Noise1(i.s * 31.0 + i.id * 31.0) * 0.4;
                    float c = (1.0 / _LineScale) * (0.75 + 0.45 * wob);
                    float aa = fwidth(x) * 1.5;
                    float core = 1.0 - smoothstep(c - aa, c + aa, x);
                    float outline = (1.0 - smoothstep(0.85 - aa, 0.85 + aa, x)) - core;
                    col = _Core.rgb * core * _CoreIntensity + _Outline.rgb * saturate(outline) * _OutlineIntensity;
                }
                return float4(col * _Flash * flicker, 0);
            }
            ENDHLSL
        }
    }
}
