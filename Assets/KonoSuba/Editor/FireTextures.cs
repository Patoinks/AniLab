using System.IO;
using UnityEditor;
using UnityEngine;

namespace AnimLab.KonoSuba.EditorTools
{
    /// <summary>
    /// Texturas pintadas do fogo, geradas no editor e sem costuras (ruído de gradiente periódico).
    /// FlameBody: fibras de pincel alongadas no eixo X da textura (o sentido do fluxo).
    ///   R = fibras largas, G = fibras finas (camada independente), B = manchas largas (massa/erosão).
    /// </summary>
    public static class FireTextures
    {
        public static Texture2D FlameBody(string path, int width = 1024, int height = 512)
        {
            var r = new float[width * height];
            var g = new float[width * height];
            var b = new float[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float u = x / (float)width, v = y / (float)height;
                    int i = y * width + x;
                    b[i] = Fbm(u * 3f, v * 5f, 3, 5, 3, 37);
                }
            PaintBrushes(r, g, width, height);
            Normalize(b, 1f);

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color(r[i], g[i], b[i], 1f);
            tex.SetPixels32(pixels);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Deliberate, tapered paint marks, not iso-contours of noise. Every mark has its own
        // length, curvature, heat and broken filaments; both axes wrap without visible seams.
        static void PaintBrushes(float[] broad, float[] filaments, int width, int height)
        {
            var random = new System.Random(3107);
            float R(float lo, float hi) => Mathf.Lerp(lo, hi, (float)random.NextDouble());
            for (int stroke = 0; stroke < 23; stroke++)
            {
                float x0 = R(0f, 1f), y0 = R(0f, 1f);
                float length = R(0.26f, 0.94f), halfWidth = R(0.016f, 0.067f);
                float bend = R(-0.07f, 0.07f), strength = R(0.57f, 1f), phase = R(0f, 12f);
                int steps = Mathf.CeilToInt(length * width);
                for (int x = 0; x <= steps; x++)
                {
                    float t = x / (float)steps;
                    float envelope = Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * Mathf.PI)), 0.65f) * (1.25f - t * 0.65f);
                    float cy = (y0 + bend * Mathf.Sin(t * Mathf.PI) + 0.008f * Mathf.Sin(t * 14 + phase)) * height;
                    float w = halfWidth * height * envelope;
                    if (w < 0.6f) continue;
                    int px = (Mathf.FloorToInt(x0 * width) + x) % width;
                    for (int y = Mathf.FloorToInt(cy - w - 2); y <= Mathf.CeilToInt(cy + w + 2); y++)
                    {
                        float across = (y - cy) / w;
                        float rag = (Mathf.PerlinNoise(t * 22 + phase, across * 2 + phase) - 0.5f) * 0.18f;
                        float coverage = Mathf.Clamp01(1 - Mathf.Abs(across) + rag);
                        float grain = Mathf.PerlinNoise(t * 9 + phase, across * 9 + 31);
                        float paint = Mathf.Pow(coverage, 0.55f) * strength * (0.82f + grain * 0.18f);
                        float line = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(across * 43 + Mathf.Sin(t * 9 + phase) * 1.5f), 7);
                        int py = ((y % height) + height) % height, i = py * width + px;
                        broad[i] = Mathf.Max(broad[i], paint);
                        filaments[i] = Mathf.Max(filaments[i], line * paint);
                    }
                }
            }
        }

        // Fibras: ruído "ridged" muito esticado em X, com o eixo Y torcido devagar para as fibras ondularem.
        // As oitavas finas apertam sobretudo em Y, para as fibras ficarem finas sem ficarem curtas.
        static float Fibers(float u, float v, int periodX, int periodY, int seed)
        {
            float bend = Noise(u * 2f, v * 3f, 2, 3, seed + 1) * 0.08f;
            float y = v + bend, sum = 0f, amp = 1f, norm = 0f;
            for (int o = 0; o < 2; o++)
            {
                int kx = 1 + o, ky = 1 << o;
                float n = 1f - Mathf.Abs(Noise(u * periodX * kx, y * periodY * ky, periodX * kx, periodY * ky, seed + 7 * o) * 1.6f);
                sum += Mathf.Pow(Mathf.Clamp01(n), 3f) * amp;
                norm += amp;
                amp *= 0.32f;
            }
            return sum / norm;
        }

        static float Fbm(float x, float y, int px, int py, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f;
            for (int o = 0; o < octaves; o++)
            {
                int k = 1 << o;
                sum += Noise(x * k, y * k, px * k, py * k, seed + o) * amp;
                amp *= 0.5f;
            }
            return sum;
        }

        // Ruído de gradiente (Perlin) periódico: a grelha dá a volta a cada (px, py) células.
        static float Noise(float x, float y, int px, int py, int seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            float ux = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f), uy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
            float a = Grad(ix, iy, px, py, seed, fx, fy), b = Grad(ix + 1, iy, px, py, seed, fx - 1f, fy);
            float c = Grad(ix, iy + 1, px, py, seed, fx, fy - 1f), d = Grad(ix + 1, iy + 1, px, py, seed, fx - 1f, fy - 1f);
            return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
        }

        static float Grad(int ix, int iy, int px, int py, int seed, float dx, float dy)
        {
            ix = ((ix % px) + px) % px;
            iy = ((iy % py) + py) % py;
            uint h = (uint)(ix * 374761393 + iy * 668265263 + seed * 144665);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            float angle = (h & 0xFFFF) / 65536f * Mathf.PI * 2f;
            return Mathf.Cos(angle) * dx + Mathf.Sin(angle) * dy;
        }

        // Estica para 0..1 (percentis 1–99) e aplica uma curva de contraste.
        static void Normalize(float[] values, float gamma)
        {
            var sorted = (float[])values.Clone();
            System.Array.Sort(sorted);
            float lo = sorted[sorted.Length / 100], hi = sorted[sorted.Length * 99 / 100];
            for (int i = 0; i < values.Length; i++)
                values[i] = Mathf.Pow(Mathf.Clamp01((values[i] - lo) / Mathf.Max(hi - lo, 1e-5f)), gamma);
        }
    }
}
