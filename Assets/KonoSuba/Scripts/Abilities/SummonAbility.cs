using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Invoca um VFX no chão (tornado de fogo, tornado de vento…): copia o modelo desativado da cena para o alvo
    /// selecionado ou para o ponto debaixo do rato (até <see cref="range"/>), e o TimedVFX fá-lo crescer, durar e
    /// encolher.
    /// </summary>
    public class SummonAbility : Ability
    {
        [Tooltip("Modelo desativado na cena (é copiado a cada uso).")]
        public GameObject template;
        public GameHud.Glyph glyph = GameHud.Glyph.Fire;
        public float duration = 7f;
        public float range = 18f;

        protected override bool Cast(AbilityContext ctx)
        {
            if (template == null || ctx.caster == null) return false;
            Vector3 point = ctx.target != null ? ctx.target.transform.position : ctx.aimPoint;
            Vector3 delta = point - ctx.caster.position;
            delta.y = 0f;
            if (delta.magnitude > range) point = ctx.caster.position + delta.normalized * range;
            point.y = 0f;
            GameObject vfx = Instantiate(template, point, Quaternion.identity);
            vfx.name = displayName;
            vfx.SetActive(true);
            vfx.AddComponent<TimedVFX>().duration = duration;
            return true;
        }
    }
}
