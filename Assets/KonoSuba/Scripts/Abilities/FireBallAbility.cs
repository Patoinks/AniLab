using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>Bola de Fogo, tecla 5: condensa no ombro e segue para o alvo ou para o ponto de mira.</summary>
    public class FireBallAbility : Ability
    {
        public Material flameMaterial, steamMaterial, ringMaterial, hitMaterial;
        [Tooltip("Fogo estilizado da bola e da explosão (KonoSuba/FireToon).")]
        public Material ballMaterial;
        public Vector3 shoulderOffset = new Vector3(0.85f, 2.25f, 0.55f);
        [Min(0.05f)] public float formationTime = 0.2f;
        [Min(0.1f)] public float radius = 0.52f;
        [Tooltip("Raio visual em metros; as labaredas ultrapassam o corpo sem alargar a colisão.")]
        [Range(0.9f, 1.1f)] public float visualRadius = 1f;
        [Min(1f)] public float speed = 20f;
        [Tooltip("Gravidade própria (m/s²): um arco pequeno, compensado na mira inicial.")]
        public float gravity = 2.4f;
        public float maxRange = 36f;
        [Tooltip("Volume evaporado no impacto junto à água (m³). Não aplica Burning.")]
        public float evaporatedVolume = 0.12f;
        [Header("Dano")]
        public int minDamage = 110;
        public int maxDamage = 140;
        [Range(0f, 1f)] public float critChance = 0.2f;
        [Min(1f)] public float critMultiplier = 1.8f;

        /// <summary>Dano de um impacto (aleatório entre min e max; crítico multiplica).</summary>
        public int RollDamage(out bool crit)
        {
            crit = Random.value < critChance;
            float dmg = Random.Range(minDamage, maxDamage + 1);
            return Mathf.RoundToInt(crit ? dmg * critMultiplier : dmg);
        }

        void Reset()
        {
            displayName = "Bola de Fogo";
            element = AbilityElement.Fire;
            slot = 4;
            manaCost = 25f;
            cooldown = 1.5f;
        }

        public Vector3 HoldPoint(Transform caster) => caster.position + caster.right * shoulderOffset.x
            + Vector3.up * shoulderOffset.y + caster.forward * shoulderOffset.z;

        protected override bool Cast(AbilityContext ctx)
        {
            if (ctx.caster == null || flameMaterial == null || steamMaterial == null
                || ringMaterial == null || hitMaterial == null || ballMaterial == null) return false;
            FireBallProjectile.Create(ctx, this);
            return true;
        }
    }
}
