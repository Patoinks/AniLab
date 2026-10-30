// Simulação de ondas (equação de onda num heightfield) usada por WaterSim.cs.
// Estado em RG: R = altura atual, G = altura no passo anterior.
// Fontes: impulsos pontuais (impactos, passos) e rajadas de vento, que cavam uma ravina de água ao longo
// do caminho (a água é atirada para as margens); quando a rajada passa, a ravina volta a encher e as
// ondas saem para os lados.
// A viscosidade apaga primeiro as ondas curtas: as ondulações finas morrem depressa, as grandes duram mais.
// A água é uma película rasa: a altura nunca desce abaixo de -_WaterDepth (aí o chão fica a seco)
// e as rajadas radiais (habilidade 360°) varrem a água para fora, deixando um círculo seco com um rebordo.
Shader "Hidden/KonoSuba/WaterSim"
{
    Properties { _MainTex ("Estado", 2D) = "black" {} }
    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            float4 _SimArea;        // centro x, centro z, tamanho (m), -
            float2 _SimShift;       // deslocamento em UV quando a área segue o player
            float _C2;              // (c * dt / dx)^2
            float _Damping;
            float _Viscosity;
            float _WaterDepth;
            float4 _Radials[4];      // centro x, centro z, raio da frente, largura do rebordo
            float4 _RadialParams[4]; // profundidade a varrer neste passo, -, -, -
            int _RadialCount;

            float4 _Impulses[16];   // x, z, raio, altura a somar
            float4 _ImpulseShape[16]; // ondulação inicial (rad/m), -, -, -
            int _ImpulseCount;
            float4 _Transfers[16];
            int _TransferCount;
            float4 _Pushers[4];     // origem x, origem z, dir.x, dir.z
            float4 _PusherParams[4];// largura, distância percorrida pela frente, profundidade a cavar neste passo, comprimento total
            int _PusherCount;

            float2 State(float2 uv)
            {
                if (any(uv < 0.0) || any(uv > 1.0)) return 0; // fora da área: água parada
                return tex2D(_MainTex, uv).rg;
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv + _SimShift;
                float2 t = _MainTex_TexelSize.xy;
                float2 s = State(uv);
                float2 r = State(uv + float2(t.x, 0)), l = State(uv - float2(t.x, 0));
                float2 u = State(uv + float2(0, t.y)), d = State(uv - float2(0, t.y));
                float lap = r.r + l.r + u.r + d.r - 4.0 * s.r;
                float lapPrev = r.g + l.g + u.g + d.g - 4.0 * s.g;
                float h = (2.0 * s.r - s.g + _C2 * lap + _Viscosity * (lap - lapPrev)) * _Damping;
                float prev = s.r;

                // Faixa de absorção nas bordas: as ondas saem da área sem fazer eco.
                float2 e = min(i.uv, 1.0 - i.uv);
                float sponge = saturate(min(e.x, e.y) / 0.07);
                float k = lerp(0.85, 1.0, sponge);
                h *= k;
                prev *= k;

                float2 w = _SimArea.xy + (i.uv - 0.5) * _SimArea.z;

                for (int n = 0; n < _ImpulseCount; n++)
                {
                    float4 imp = _Impulses[n];
                    float2 d = w - imp.xy;
                    float r2 = dot(d, d);
                    // Perfil ondulado (cos) em vez de uma bossa simples: a água real solta vários anéis.
                    h += imp.w * exp(-r2 / (imp.z * imp.z)) * cos(sqrt(r2) * _ImpulseShape[n].x);
                }

                for (int m = 0; m < _PusherCount; m++)
                {
                    float4 p = _Pushers[m];
                    float4 pp = _PusherParams[m];
                    float2 dir = p.zw;
                    float2 dv = w - p.xy;
                    float along = dot(dv, dir);
                    float across = abs(dot(dv, float2(-dir.y, dir.x)));

                    // Só a parte do corredor por onde a frente já passou; mais forte logo atrás da frente.
                    float swept = smoothstep(-0.5, 0.5, along) * (1.0 - smoothstep(pp.y - 0.5, pp.y + 0.5, along))
                                * (1.0 - smoothstep(pp.w - 2.0, pp.w, along));
                    float recent = 0.35 + 0.65 * exp(-max(pp.y - along, 0.0) / 3.0);
                    // Abre em leque como o corredor no nevoeiro.
                    float width = pp.x * (0.5 + 0.5 * saturate(along / max(pp.w, 1e-3)));

                    // Margens irregulares ao longo do corredor.
                    width *= 1.0 + 0.06 * sin(along * 0.6 + p.x * 2.7) + 0.03 * sin(along * 1.7 + p.y * 1.9);
                    // Perfil da ravina: fundo no centro, a água empurrada sobe em margens largas.
                    float x = across / max(width, 1e-3);
                    float profile = -exp(-x * x / 0.12) + 0.5 * exp(-(x - 0.6) * (x - 0.6) / 0.08);
                    h += pp.z * swept * recent * profile;
                }

                for (int q = 0; q < _RadialCount; q++)
                {
                    float4 c = _Radials[q];
                    float2 dv = w - c.xy;
                    float dist = length(dv);
                    // Frente irregular: o ar não empurra a água por igual em todas as direções.
                    float ang = atan2(dv.y, dv.x);
                    float front = c.z * (1.0 + 0.035 * sin(ang * 3.0 + c.x * 3.1) + 0.02 * sin(ang * 7.0 + c.y * 2.3));
                    // Dentro da frente a água é varrida; à frente dela acumula-se num rebordo que é empurrado.
                    float inside = 1.0 - smoothstep(front - c.w, front, dist);
                    float rim = exp(-((dist - front - c.w * 0.3) * (dist - front - c.w * 0.3)) / (c.w * c.w));
                    h += _RadialParams[q].x * (-inside + 0.55 * rim);
                }

                for (int v = 0; v < _TransferCount; v++)
                {
                    float4 transfer = _Transfers[v];
                    float2 offset = w - transfer.xy;
                    float change = transfer.w * exp(-dot(offset, offset) / (transfer.z * transfer.z));
                    h += change;
                    prev += change;
                }

                // Película rasa: não se pode tirar mais água do que a que existe (o chão fica a seco),
                // e o que se acumula num sítio também é limitado.
                h = clamp(h, -_WaterDepth, _WaterDepth * 4.0);
                prev = clamp(prev, -_WaterDepth, _WaterDepth * 4.0);

                return float4(h, prev, 0, 0);
            }
            ENDCG
        }
    }
}
