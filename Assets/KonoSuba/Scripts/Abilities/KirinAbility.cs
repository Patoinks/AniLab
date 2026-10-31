using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>Kirin, tecla 4: invoca um dragão elétrico que morde o destino e descarrega pela água.</summary>
    public class KirinAbility : Ability
    {
        public Material boltMaterial;
        [Tooltip("Traço do dragão em estilo anime (KonoSuba/LightningAnime).")]
        public Material animeMaterial;
        [Tooltip("Olhos do dragão (LightningAnime em vermelho).")]
        public Material eyeMaterial;
        [Tooltip("Impacto no chão (o mesmo do trovão).")]
        public LightningImpactMaterials impact;
        public float range = 14f;
        [Header("Golpe")]
        public float damage = 90f;
        [Tooltip("Raio da descarga no impacto: os corpos dentro levam dano (até 40% na borda).")]
        public float blastRadius = 2.5f;

        void Reset()
        {
            displayName = "Kirin";
            element = AbilityElement.Lightning;
            slot = 3;
            manaCost = 60f;
            cooldown = 15f;
        }

        protected override bool Cast(AbilityContext ctx)
        {
            if (animeMaterial == null || ctx.caster == null) return false;
            Vector3 destination = ctx.target != null ? ctx.target.transform.position : ctx.aimPoint;
            Vector3 delta = destination - ctx.caster.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > range * range) return false; // TryCast não inicia recarga nem gasta mana
            KirinEffect.Create(ctx, this);
            return true;
        }
    }
}
