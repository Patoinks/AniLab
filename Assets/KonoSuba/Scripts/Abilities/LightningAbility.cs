using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Raio (elétrico), tecla 3: a descarga nasce em pontos aleatórios à volta do player e espalha-se muito
    /// depressa pela água ligada, aos impulsos, enquanto tiver energia (LightningDischarge). Sem água não cresce.
    /// Quem estiver junto de um canal leva o choque pelos pés; molhado (p.ex. pela Bola de Água) leva mais.
    /// O nevoeiro ganha cor roxa por cima dos raios (LightningFogGlow).
    /// </summary>
    public class LightningAbility : Ability
    {
        [Tooltip("Material dos raios (KonoSuba/LightningBolt).")]
        public Material boltMaterial;

        void Reset()
        {
            displayName = "Lightning";
            element = AbilityElement.Lightning;
            slot = 2;
            manaCost = 30f;
            cooldown = 3f;
        }

        protected override bool Cast(AbilityContext ctx)
        {
            if (boltMaterial == null) return false;
            LightningDischarge.Create(ctx.caster, this);
            return true;
        }
    }
}
