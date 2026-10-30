using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Brilho do Raio na água. Os segmentos novos dos canais são carimbados numa máscara de 512 × 512 (40 × 40 m)
    /// com, por píxel: R = instante de chegada, G = ID do ramo (-1 = vazio), B = comprimento acumulado (s),
    /// A = cobertura do canal. O chão (CheckerWaterFloor) avalia a mesma função de brilho do raio
    /// (LightningCommon: chegada, reacendimentos, apagões), por isso a água acende-se e apaga-se com o raio.
    /// Um só dono por píxel (o canal com mais cobertura).
    /// Globais: _LightningGround, _LightningGroundRect (x mín., z mín., tamanho, ativo).
    /// </summary>
    public class LightningGroundField
    {
        const int Res = 512;
        const float Size = 40f;
        const float Width = 0.4f; // largura total do brilho na água
        static readonly int TexId = Shader.PropertyToID("_LightningGround");
        static readonly int RectId = Shader.PropertyToID("_LightningGroundRect");

        readonly Texture2D tex;
        readonly Color[] pixels = new Color[Res * Res];
        readonly Vector2 min;
        bool dirty;

        public LightningGroundField(Vector3 center)
        {
            min = new Vector2(center.x - Size * 0.5f, center.z - Size * 0.5f);
            tex = new Texture2D(Res, Res, TextureFormat.RGBAFloat, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0f, -1f, 0f, 0f);
            dirty = true;
            Flush();
            Shader.SetGlobalTexture(TexId, tex);
        }

        /// <summary>Carimba um segmento novo (só rente à água).</summary>
        public void Stamp(Vector3 a, Vector3 b, float arrivalA, float arrivalB, float sA, float sB, int branch)
        {
            if (a.y > 0.25f || b.y > 0.25f) return;
            float px = Res / Size;
            float r = Width * 0.5f * px;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) * px));
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                Vector3 p = Vector3.Lerp(a, b, t);
                float fx = (p.x - min.x) * px, fz = (p.z - min.y) * px;
                float arrival = Mathf.Lerp(arrivalA, arrivalB, t), len = Mathf.Lerp(sA, sB, t);
                int cx = Mathf.RoundToInt(fx), cz = Mathf.RoundToInt(fz), ir = Mathf.CeilToInt(r);
                for (int z = cz - ir; z <= cz + ir; z++)
                    for (int x = cx - ir; x <= cx + ir; x++)
                    {
                        if (x < 0 || z < 0 || x >= Res || z >= Res) continue;
                        float d = Mathf.Sqrt((x - fx) * (x - fx) + (z - fz) * (z - fz)) / r;
                        if (d >= 1f) continue;
                        float cover = (1f - d * d) * (1f - d * d);
                        int i = z * Res + x;
                        if (cover <= pixels[i].a) continue;
                        pixels[i] = new Color(arrival, branch, len, cover);
                        dirty = true;
                    }
            }
        }

        /// <summary>Envia para a GPU o que mudou (uma vez por frame).</summary>
        public void Flush()
        {
            if (!dirty) return;
            tex.SetPixels(pixels);
            tex.Apply(false);
            dirty = false;
        }

        // Água e nevoeiro continuam globais: com Raio e Kirin simultâneos prevalece o último a escrever.
        public void SetActive(bool on)
        {
            Shader.SetGlobalTexture(TexId, tex);
            Shader.SetGlobalVector(RectId, new Vector4(min.x, min.y, Size, on ? 1f : 0f));
        }

        public void Release()
        {
            if (Shader.GetGlobalTexture(TexId) == tex)
            {
                Shader.SetGlobalVector(RectId, Vector4.zero);
                Shader.SetGlobalTexture(TexId, Texture2D.blackTexture);
            }
            Object.Destroy(tex);
        }
    }
}
