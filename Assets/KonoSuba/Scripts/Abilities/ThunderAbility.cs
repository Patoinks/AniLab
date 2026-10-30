using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Trovão (estilo anime): um raio cai do céu no alvo selecionado ou no ponto debaixo do rato, re-cai várias
    /// vezes e rebenta no chão (LightningImpact: clarão, fissuras, faíscas, detritos, arcos rasteiros, onda de choque).
    /// </summary>
    public class ThunderAbility : Ability
    {
        public Material boltMaterial;
        public LightningImpactMaterials impact;
        public float height = 14f, width = 0.22f, range = 18f;

        protected override bool Cast(AbilityContext ctx)
        {
            if (boltMaterial == null || ctx.caster == null) return false;
            Vector3 point = ctx.target != null ? ctx.target.transform.position : ctx.aimPoint;
            Vector3 delta = point - ctx.caster.position;
            delta.y = 0f;
            if (delta.magnitude > range) point = ctx.caster.position + delta.normalized * range;
            point.y = 0.02f;
            Vector2 top = Random.insideUnitCircle * 2.5f;
            AnimeLightningStrike.Spawn(point + new Vector3(top.x, height, top.y), point, boltMaterial, width, Random.Range(0, 100000), impact);
            FogField.Blast(new Vector3(point.x, 0f, point.z), 5f, 0.8f, 3f, 0.15f, 0.4f, false);
            WaterSim.Impulse(point, 0.6f, -0.02f);
            return true;
        }
    }
}
