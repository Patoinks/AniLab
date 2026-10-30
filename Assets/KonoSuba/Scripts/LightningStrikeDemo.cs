using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>Ponto de teste: trovões a cair do céu a intervalos, com pontas ligeiramente aleatórias.</summary>
    public class LightningStrikeDemo : MonoBehaviour
    {
        public Material material;
        public LightningImpactMaterials impact;
        public float interval = 1.3f, height = 12f, width = 0.22f;
        float next;
        int seed = 1;

        void Update()
        {
            if (Time.time < next || material == null) return;
            next = Time.time + interval;
            Vector2 top = Random.insideUnitCircle * 2.5f, bottom = Random.insideUnitCircle * 0.4f;
            Vector3 p = transform.position;
            AnimeLightningStrike.Spawn(p + new Vector3(top.x, height, top.y), p + new Vector3(bottom.x, 0.02f, bottom.y), material, width, seed++, impact);
        }
    }
}
