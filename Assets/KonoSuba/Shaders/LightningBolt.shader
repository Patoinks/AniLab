// Raios do Raio: fitas viradas para a câmara, alargadas no vertex shader com largura em PÍXEIS (núcleo ~1,8 px,
// halo ~14 px; limites em metros), para se lerem igual a qualquer distância da câmara.
// Por vértice (LightningDischarge): posição do ponto, tangente (normal), uv0 = (s ao longo do ramo, lado ±1),
// uv1 = (instante de chegada, ID do ramo), uv2 = (fator de largura do núcleo, afinar na ponta).
// O brilho vem de LightningCommon (chegada, reacendimentos, apagões por ramo) e de pontos quentes ao longo do canal.
// _LightningTime/_LightningBranches/_LightningFade aceitam MaterialPropertyBlock; sem ele usam os globais.
Shader "KonoSuba/LightningBolt"
{
    Properties
    {
        _Glow ("Halo", Color) = (0.55, 0.2, 1, 1)
        _Core ("Núcleo", Color) = (0.94, 0.88, 1, 1)
        _CoreIntensity ("Intensidade do núcleo", Float) = 7
        _GlowIntensity ("Intensidade do halo", Float) = 2.4
        _CorePixels ("Núcleo (px)", Float) = 4.5
        _HaloPixels ("Halo (px)", Float) = 30
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
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "LightningCommon.cginc"

            float4 _Glow, _Core;
            float _CoreIntensity, _GlowIntensity, _CorePixels, _HaloPixels;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;     // tangente do raio
                float2 uv0 : TEXCOORD0;     // s, lado
                float2 uv1 : TEXCOORD1;     // chegada, ID
                float2 uv2 : TEXCOORD2;     // fator de largura do núcleo, afinar
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float across : TEXCOORD0;
                float coreRatio : TEXCOORD1;
                float4 data : TEXCOORD2;    // chegada, ID, s, afinar
            };

            v2f vert(appdata v)
            {
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                // Pontos repetidos (p.ex. a boca a fechar) dão tangente nula: sem isto o normalize dá NaN e o bloom
                // espalha-o pelo ecrã inteiro (um fotograma preto).
                float3 tw = mul((float3x3)unity_ObjectToWorld, v.normal);
                float3 tangent = dot(tw, tw) > 1e-12 ? normalize(tw) : float3(0, 0, 1);
                float3 toCam = _WorldSpaceCameraPos - wp;
                float3 side = cross(tangent, toCam);
                side = dot(side, side) > 1e-10 ? normalize(side) : float3(1, 0, 0);

                // Metros por píxel à profundidade deste ponto (perspetiva ou ortográfica).
                float depth = -mul(UNITY_MATRIX_V, float4(wp, 1)).z;
                float mpp = unity_OrthoParams.w > 0.5 ? 2.0 * unity_OrthoParams.y / _ScreenParams.y
                                                      : 2.0 * depth / (unity_CameraProjection._m11 * _ScreenParams.y);
                float taper = v.uv2.y;
                float halo = clamp(_HaloPixels * mpp, 0.09, 0.5) * lerp(0.35, 1.0, taper);
                float core = clamp(_CorePixels * mpp, 0.014, 0.08) * v.uv2.x * taper;

                wp += side * v.uv0.y * halo * 0.5;
                v2f o;
                o.pos = mul(UNITY_MATRIX_VP, float4(wp, 1));
                o.across = v.uv0.y;
                o.coreRatio = core / halo;
                o.data = float4(v.uv1.x, v.uv1.y, v.uv0.x, taper);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float sig = LightningSignal(i.data.y, i.data.x, i.data.z);
                if (sig <= 0.0) return 0;
                float seed = LightningBranchRow(i.data.y, 0).y;
                float hot = LightningHotSpots(i.data.z, seed);

                float x = abs(i.across);
                float aa = max(fwidth(x) * 1.5, 1e-3); // borda do núcleo suavizada: não serrilha mesmo fino
                float core = 1.0 - smoothstep(i.coreRatio - aa, i.coreRatio + aa, x);
                float halo = exp(-x * x * 4.0) * (1.0 - x * x);          // suave e a chegar a zero na borda
                float3 col = _Core.rgb * core * _CoreIntensity + _Glow.rgb * halo * _GlowIntensity;
                float3 outCol = col * sig * hot * i.data.w;
                return float4(any(isnan(outCol)) ? 0 : max(outCol, 0), 1); // nunca deixar passar NaN para o bloom
            }
            ENDCG
        }
    }
}
