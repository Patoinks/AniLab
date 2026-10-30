using UnityEngine;

namespace AnimLab
{
    /// <summary>
    /// Gera relevo com ruído Perlin em várias oitavas e pinta as camadas por altura e inclinação.
    /// Espera 3 TerrainLayers no TerrainData: 0 = relva, 1 = rocha, 2 = areia.
    /// Usa o menu de contexto do componente (⋮ > Generate) para regenerar no editor.
    /// </summary>
    [RequireComponent(typeof(Terrain))]
    public class ProceduralTerrain : MonoBehaviour
    {
        [Header("Relevo")]
        public int seed = 1234;
        [Tooltip("Escala do ruído em metros: valores maiores dão colinas mais largas.")]
        public float noiseScale = 80f;
        [Range(1, 8)] public int octaves = 5;
        [Range(0f, 1f)] public float persistence = 0.45f;
        public float lacunarity = 2f;
        [Tooltip("Expoente aplicado à altura: >1 aplana vales e acentua picos.")]
        public float heightCurve = 1.6f;

        [Header("Zona plana central (spawn)")]
        public float flatRadius = 18f;
        public float flatBlend = 25f;
        [Range(0f, 1f)] public float flatHeight = 0.12f;

        [Header("Pintura")]
        [Range(0f, 1f)] public float sandHeight = 0.08f;
        public float rockSlope = 32f;

        [ContextMenu("Generate")]
        public void Generate()
        {
            Terrain terrain = GetComponent<Terrain>();
            TerrainData data = terrain.terrainData;
            GenerateHeights(data);
            PaintLayers(data);
            terrain.Flush();
        }

        void GenerateHeights(TerrainData data)
        {
            int res = data.heightmapResolution;
            Vector3 size = data.size;
            var rng = new System.Random(seed);
            Vector2 offset = new Vector2(rng.Next(-10000, 10000), rng.Next(-10000, 10000));
            Vector2 center = new Vector2(size.x, size.z) * 0.5f;

            float[,] heights = new float[res, res];
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    // Coordenadas no mundo (em metros) do ponto do heightmap.
                    float wx = (float)x / (res - 1) * size.x;
                    float wz = (float)y / (res - 1) * size.z;

                    float amplitude = 1f, frequency = 1f, value = 0f, max = 0f;
                    for (int o = 0; o < octaves; o++)
                    {
                        float nx = (wx + offset.x) / noiseScale * frequency;
                        float nz = (wz + offset.y) / noiseScale * frequency;
                        value += Mathf.PerlinNoise(nx, nz) * amplitude;
                        max += amplitude;
                        amplitude *= persistence;
                        frequency *= lacunarity;
                    }
                    float h = Mathf.Pow(value / max, heightCurve);

                    // Aplana o centro para o personagem começar num sítio jogável.
                    float dist = Vector2.Distance(new Vector2(wx, wz), center);
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(flatRadius, flatRadius + flatBlend, dist));
                    heights[y, x] = Mathf.Lerp(flatHeight, h, t);
                }
            }
            data.SetHeights(0, 0, heights);
        }

        void PaintLayers(TerrainData data)
        {
            int layers = data.alphamapLayers;
            if (layers < 3)
            {
                Debug.LogWarning("ProceduralTerrain: são precisas 3 TerrainLayers (relva, rocha, areia) para pintar.", this);
                return;
            }

            int res = data.alphamapResolution;
            float[,,] alpha = new float[res, res, layers];
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float u = (float)x / (res - 1);
                    float v = (float)y / (res - 1);
                    float height = data.GetInterpolatedHeight(u, v) / data.size.y;
                    float slope = data.GetSteepness(u, v);

                    float rock = Mathf.InverseLerp(rockSlope - 8f, rockSlope + 8f, slope);
                    float sand = (1f - rock) * Mathf.InverseLerp(sandHeight + 0.03f, sandHeight, height);
                    float grass = Mathf.Max(0f, 1f - rock - sand);

                    alpha[y, x, 0] = grass;
                    alpha[y, x, 1] = rock;
                    alpha[y, x, 2] = sand;
                }
            }
            data.SetAlphamaps(0, 0, alpha);
        }
    }
}
