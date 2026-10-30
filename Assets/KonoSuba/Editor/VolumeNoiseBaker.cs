using UnityEditor;
using UnityEngine;

namespace AnimLab.KonoSuba.EditorTools
{
    /// <summary>
    /// Ruído 3D repetível (tileable) para volumes (névoa, nuvens), cozido uma vez num Texture3D:
    /// R = Perlin-Worley (formas grandes e fofas, como a base das nuvens),
    /// G = Worley em 3 oitavas (detalhe que desfia as bordas), B = Perlin de alta frequência (turbulência).
    /// </summary>
    public static class VolumeNoiseBaker
    {
        public const string Path = "Assets/KonoSuba/VFX/VolumeNoise3D.asset";

        public static Texture3D Get(int size = 64)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture3D>(Path);
            if (t != null) return t;
            t = Bake(size);
            AssetDatabase.CreateAsset(t, Path);
            return t;
        }

        static Texture3D Bake(int size)
        {
            var px = new Color32[size * size * size];
            for (int z = 0; z < size; z++)
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector3(x, y, z) / size;
                float perlin = 0.5f + 0.5f * (Perlin(p, 4) * 0.6f + Perlin(p, 8) * 0.28f + Perlin(p, 16) * 0.12f) * 1.6f;
                float worleyLow = 1f - Worley(p, 4);
                float pw = Mathf.Clamp01(Remap(Mathf.Clamp01(perlin), worleyLow * 0.55f - 0.1f, 1f, 0f, 1f));
                float detail = (1f - Worley(p, 8)) * 0.6f + (1f - Worley(p, 16)) * 0.28f + (1f - Worley(p, 32)) * 0.12f;
                float turb = Mathf.Clamp01(0.5f + 0.5f * (Perlin(p, 16) * 0.65f + Perlin(p, 32) * 0.35f) * 1.5f);
                px[x + y * size + z * size * size] = new Color32((byte)(pw * 255f), (byte)(Mathf.Clamp01(detail) * 255f), (byte)(turb * 255f), 255);
            }
            var t = new Texture3D(size, size, size, TextureFormat.RGBA32, true)
            {
                name = "VolumeNoise3D",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
            };
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }

        static float Remap(float v, float a, float b, float c, float d) => c + (v - a) / Mathf.Max(b - a, 1e-4f) * (d - c);

        static int Wrap(int i, int period) => ((i % period) + period) % period;

        static uint Hash(int x, int y, int z, int salt)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ (uint)(salt * 2654435761u);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return h;
        }

        static float Hash01(int x, int y, int z, int salt) => (Hash(x, y, z, salt) & 0xFFFFFF) / 16777215f;

        static Vector3 Gradient(int x, int y, int z, int period)
        {
            x = Wrap(x, period); y = Wrap(y, period); z = Wrap(z, period);
            var g = new Vector3(Hash01(x, y, z, 1) * 2f - 1f, Hash01(x, y, z, 2) * 2f - 1f, Hash01(x, y, z, 3) * 2f - 1f);
            return g.normalized;
        }

        // Ruído de gradiente com período = frequência (repete-se no cubo 0..1).
        static float Perlin(Vector3 p, int freq)
        {
            p *= freq;
            int ix = Mathf.FloorToInt(p.x), iy = Mathf.FloorToInt(p.y), iz = Mathf.FloorToInt(p.z);
            Vector3 f = p - new Vector3(ix, iy, iz);
            Vector3 u = new Vector3(Fade(f.x), Fade(f.y), Fade(f.z));
            float n = 0f;
            for (int c = 0; c < 8; c++)
            {
                int dx = c & 1, dy = (c >> 1) & 1, dz = (c >> 2) & 1;
                float w = (dx == 1 ? u.x : 1f - u.x) * (dy == 1 ? u.y : 1f - u.y) * (dz == 1 ? u.z : 1f - u.z);
                n += w * Vector3.Dot(Gradient(ix + dx, iy + dy, iz + dz, freq), f - new Vector3(dx, dy, dz));
            }
            return n;
        }

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        // Distância ao ponto de célula mais próximo (F1), com as células a repetir no cubo.
        static float Worley(Vector3 p, int cells)
        {
            p *= cells;
            int ix = Mathf.FloorToInt(p.x), iy = Mathf.FloorToInt(p.y), iz = Mathf.FloorToInt(p.z);
            float best = 9f;
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = ix + dx, cy = iy + dy, cz = iz + dz;
                int wx = Wrap(cx, cells), wy = Wrap(cy, cells), wz = Wrap(cz, cells);
                var feature = new Vector3(cx + Hash01(wx, wy, wz, 11), cy + Hash01(wx, wy, wz, 12), cz + Hash01(wx, wy, wz, 13));
                best = Mathf.Min(best, (feature - p).sqrMagnitude);
            }
            return Mathf.Clamp01(Mathf.Sqrt(best));
        }
    }
}
