// Partículas de nevoeiro: forma suave erodida por ruído animado, soft particles (sem cortes no chão)
// e fade perto da câmara para não "bater" na lente.
Shader "KonoSuba/FogParticle"
{
    Properties
    {
        _MainTex ("Forma", 2D) = "white" {}
        _NoiseTex ("Ruído (tileable)", 2D) = "white" {}
        _Tint ("Cor", Color) = (0.70, 0.75, 0.85, 0.5)
        _NoiseAmount ("Erosão pelo ruído", Range(0, 1)) = 0.65
        _NoiseScroll ("Velocidade do ruído", Float) = 0.015
        _Distortion ("Força do fluxo (enrolar)", Range(0, 0.3)) = 0
        _DistortSpeed ("Velocidade do fluxo", Float) = 0.08
        _AtlasTiles ("Tiles do atlas (1 = sem atlas)", Float) = 1
        _SoftDistance ("Soft particles (m)", Float) = 1.5
        _CameraFadeStart ("Fade câmara início (m)", Float) = 1
        _CameraFadeEnd ("Fade câmara fim (m)", Float) = 6
        _GlowColor ("Brilho do foco central", Color) = (0.45, 0.47, 0.52, 1)
        _GlowRadius ("Raio do brilho (m)", Float) = 14
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "FogField.cginc"

            sampler2D _MainTex, _NoiseTex;
            float4 _NoiseTex_ST;
            fixed4 _Tint, _GlowColor;
            float _GlowRadius;
            float _Distortion, _DistortSpeed, _AtlasTiles;
            float _NoiseAmount, _NoiseScroll, _SoftDistance, _CameraFadeStart, _CameraFadeEnd;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 projPos : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                UNITY_FOG_COORDS(3)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.projPos = ComputeScreenPos(o.pos);
                COMPUTE_EYEDEPTH(o.projPos.z);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            // Campo de fluxo sem divergência (curl do ruído): o fumo roda e enrola-se em vez de se esticar.
            float2 CurlFlow(float2 p)
            {
                const float e = 1.0 / 128.0;
                float n0 = tex2D(_NoiseTex, p).r;
                float nx = tex2D(_NoiseTex, p + float2(e, 0)).r;
                float ny = tex2D(_NoiseTex, p + float2(0, e)).r;
                float2 curl = float2(ny - n0, n0 - nx) / e;
                return curl / (1.0 + length(curl)); // limita a magnitude a < 1
            }

            // Flow map em duas fases: cada fase avança ao longo do fluxo e reinicia, e as duas cruzam-se
            // de forma a que o reinício nunca se veja. Resultado: movimento contínuo, sem esticar para sempre.
            void FlowPhases(float time, out float p0, out float p1, out float w)
            {
                p0 = frac(time);
                p1 = frac(time + 0.5);
                w = abs(1.0 - 2.0 * p0); // peso da fase 1 (1 quando a fase 0 reinicia)
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float p0, p1, w;

                // 1) A forma do fiapo é arrastada por um fluxo de curl: enrola-se e evolui ao longo do tempo.
                float2 seedOff = i.worldPos.xz * 0.011;
                float2 flowA = CurlFlow(i.uv * 0.7 + seedOff + _Time.y * _DistortSpeed * 0.2) * _Distortion;
                FlowPhases(_Time.y * _DistortSpeed + seedOff.x * 3.0, p0, p1, w);
                // Não deixa a distorção sair do tile atual do atlas (evitaria "fantasmas" do vizinho).
                float tile = 1.0 / _AtlasTiles;
                float2 tileMin = floor(i.uv * _AtlasTiles) * tile;
                float2 uvA = clamp(i.uv - flowA * p0, tileMin + 0.004, tileMin + tile - 0.004);
                float2 uvB = clamp(i.uv - flowA * p1, tileMin + 0.004, tileMin + tile - 0.004);
                float shape = lerp(tex2D(_MainTex, uvA).a, tex2D(_MainTex, uvB).a, w);

                // 2) A estrutura interna (ruído em espaço de mundo) também é advectada pelo fluxo,
                //    por isso o nevoeiro parece fluir através das partículas e não deslizar com elas.
                //    n1 varia no plano horizontal, n2 na vertical (senão os billboards ficam com riscas).
                float t = _Time.y * _NoiseScroll;
                float3 wp = i.worldPos * 0.06;
                float2 uvH = wp.xz + float2(t, t * 0.4);
                float2 flowB = CurlFlow(uvH * 0.35) * 0.18;
                FlowPhases(_Time.y * _NoiseScroll * 8.0, p0, p1, w);
                float n1 = lerp(tex2D(_NoiseTex, uvH - flowB * p0).r,
                                tex2D(_NoiseTex, uvH - flowB * p1 + float2(0.37, 0.21)).r, w);
                float2 uvV = float2(wp.x + wp.z, wp.y * 2.5) * 1.7 + float2(-t * 1.3, t * 0.7);
                float n2 = tex2D(_NoiseTex, uvV - flowB * 0.5).r;
                float noise = saturate(n1 * n2 * 1.8);
                float alpha = shape * lerp(1.0, smoothstep(0.25, 0.85, noise) * 1.4, _NoiseAmount);

                float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.projPos)));
                float partZ = i.projPos.z;
                float soft = saturate((sceneZ - partZ) / _SoftDistance);
                float camFade = saturate((partZ - _CameraFadeStart) / (_CameraFadeEnd - _CameraFadeStart));

                // Simula o nevoeiro iluminado pelo foco central (o shader é unlit).
                float2 d = i.worldPos.xz;
                float glow = exp(-dot(d, d) / (_GlowRadius * _GlowRadius));

                fixed4 col = _Tint * i.color;
                col.rgb += _GlowColor.rgb * glow;
                col.a *= saturate(alpha * FogClearing(i.worldPos.xz)) * soft * camFade;
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
