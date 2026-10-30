using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Cor do nevoeiro durante o Raio: uma textura pequena (64 × 64 sobre 40 × 40 m) com o brilho atual dos raios
    /// no chão, espalhado e suave, refeita todos os frames com o mesmo envelope do shader (chegada + rasto).
    /// O GroundFog tinge-se de roxo por cima dela e puxa um pouco toda a névoa para roxo (tom geral).
    /// Globais: _LightningFogGlow, _LightningFogRect (x mín., z mín., tamanho, tom geral).
    /// </summary>
    public class LightningFogGlow
    {
        const int Res = 64;
        const float Size = 40f;
        static readonly int TexId = Shader.PropertyToID("_LightningFogGlow");
        static readonly int RectId = Shader.PropertyToID("_LightningFogRect");

        readonly Texture2D tex;
        readonly float[] buffer = new float[Res * Res];
        readonly Color[] pixels = new Color[Res * Res];
        readonly Vector2 min;

        public LightningFogGlow(Vector3 center)
        {
            min = new Vector2(center.x - Size * 0.5f, center.z - Size * 0.5f);
            tex = new Texture2D(Res, Res, TextureFormat.RHalf, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            // Começa a zeros: uma textura nova tem lixo (pode ser NaN) e o nevoeiro lê-a já neste fotograma.
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;
            tex.SetPixels(pixels);
            tex.Apply(false);
            Shader.SetGlobalTexture(TexId, tex);
        }

        // O mesmo envelope do LightningCommon (pico ao chegar, aceso até 0,5 s, apaga até 0,65 s).
        static float Envelope(float a)
        {
            if (a < 0f) return 0f;
            if (a < 0.035f) return 1f;
            if (a < 0.15f) return Mathf.Lerp(1f, 0.45f, (a - 0.035f) / 0.115f);
            if (a < 0.5f) return 0.45f;
            return 0.45f * Mathf.Clamp01(1f - (a - 0.5f) / 0.15f);
        }

        /// <summary>Refaz a textura com os pontos dos raios (posição e instante de chegada).</summary>
        public void Update(List<Vector3> positions, List<float> arrivals, float time, float fade, List<float> holds = null)
        {
            System.Array.Clear(buffer, 0, buffer.Length);
            float px = Res / Size;
            float total = 0f;
            for (int i = 0; i < positions.Count; i++)
            {
                float e = Envelope(time - arrivals[i]);
                if (holds != null && holds[i] > 0f && time >= arrivals[i])
                    e = time < holds[i] ? 0.85f : Envelope(time - holds[i]);
                if (e <= 0.01f) continue;
                total += e;
                Vector3 pos = positions[i];
                float fx = (pos.x - min.x) * px, fz = (pos.z - min.y) * px;
                int cx = Mathf.RoundToInt(fx), cz = Mathf.RoundToInt(fz);
                for (int z = cz - 2; z <= cz + 2; z++)
                    for (int x = cx - 2; x <= cx + 2; x++)
                    {
                        if (x < 0 || z < 0 || x >= Res || z >= Res) continue;
                        float d2 = (x - fx) * (x - fx) + (z - fz) * (z - fz);
                        buffer[z * Res + x] += e * Mathf.Exp(-d2 / 2.2f) * 0.12f;
                    }
            }
            for (int i = 0; i < buffer.Length; i++) pixels[i] = new Color(Mathf.Min(buffer[i], 1.5f) * fade, 0f, 0f, 1f);
            tex.SetPixels(pixels);
            tex.Apply(false);
            Shader.SetGlobalTexture(TexId, tex);
            // Tom geral: quanto mais raio aceso, mais a névoa toda puxa para roxo.
            float tint = Mathf.Clamp01(total / 120f) * fade;
            Shader.SetGlobalVector(RectId, new Vector4(min.x, min.y, Size, tint));
        }

        public void Release()
        {
            if (Shader.GetGlobalTexture(TexId) == tex)
            {
                Shader.SetGlobalVector(RectId, new Vector4(0f, 0f, Size, 0f));
                Shader.SetGlobalTexture(TexId, Texture2D.blackTexture);
            }
            Object.Destroy(tex);
        }
    }
}
