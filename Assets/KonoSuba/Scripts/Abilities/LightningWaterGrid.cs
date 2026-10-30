using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Onde há água para o Raio conduzir, consultado com preguiça: cada célula de 0,35 m só é verificada
    /// (WaterSim.HasWaterAt) quando uma ponta lá quer passar, e fica em cache durante a descarga.
    /// Também conta as visitas, para as pontas preferirem água ainda não percorrida.
    /// A resolução é a da célula: faixas secas mais estreitas não são vistas (aceite de propósito).
    /// </summary>
    public class LightningWaterGrid
    {
        public const float Cell = 0.35f;
        readonly Dictionary<long, bool> water = new Dictionary<long, bool>();
        readonly Dictionary<long, int> visits = new Dictionary<long, int>();

        static long Key(Vector3 p)
        {
            long x = Mathf.FloorToInt(p.x / Cell), z = Mathf.FloorToInt(p.z / Cell);
            return (x << 32) ^ (z & 0xffffffffL);
        }

        public bool Water(Vector3 p)
        {
            long k = Key(p);
            if (!water.TryGetValue(k, out bool w))
            {
                Vector3 c = new Vector3((Mathf.FloorToInt(p.x / Cell) + 0.5f) * Cell, 0f, (Mathf.FloorToInt(p.z / Cell) + 0.5f) * Cell);
                w = WaterSim.HasWaterAt(c);
                water[k] = w;
            }
            return w;
        }

        /// <summary>Segmento todo com água (início, meio e fim, e de 2,5 em 2,5 cm entre eles).</summary>
        public bool Segment(Vector3 a, Vector3 b)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(a, b) / 0.025f));
            for (int i = 0; i <= n; i++) if (!Water(Vector3.Lerp(a, b, i / (float)n))) return false;
            return true;
        }

        public int Visits(Vector3 p) => visits.TryGetValue(Key(p), out int v) ? v : 0;
        public void Visit(Vector3 p) { long k = Key(p); visits[k] = Visits(p) + 1; }

        /// <summary>O ponto com água mais perto (até <paramref name="radius"/>), ou falso se não há.</summary>
        public bool NearestWater(Vector3 from, float radius, out Vector3 found)
        {
            found = from;
            if (Water(from)) return true;
            for (float r = 0.1f; r <= radius; r += 0.1f)
                for (int a = 0; a < 12; a++)
                {
                    Vector3 p = from + Quaternion.Euler(0f, a * 30f, 0f) * Vector3.forward * r;
                    if (Water(p)) { found = p; return true; }
                }
            return false;
        }
    }
}
