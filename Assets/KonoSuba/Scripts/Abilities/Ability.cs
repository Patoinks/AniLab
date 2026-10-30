using UnityEngine;

namespace AnimLab.KonoSuba
{
    public enum AbilityElement { Neutral, Water, Lightning, Fire, Air }

    /// <summary>
    /// Base das habilidades. Cada habilidade é um componente no player; a AbilityBar trata das teclas,
    /// da mana e da recarga, e mostra tudo no HUD.
    /// </summary>
    public abstract class Ability : MonoBehaviour
    {
        public string displayName = "Habilidade";
        public AbilityElement element = AbilityElement.Neutral;
        [Tooltip("Espaço da barra (0 = tecla 1).")]
        public int slot;
        public float manaCost = 10f;
        public float cooldown = 1f;

        float readyAt;

        public float CooldownRemaining01 => cooldown <= 0f ? 0f : Mathf.Clamp01((readyAt - Time.time) / cooldown);
        public bool IsReady => Time.time >= readyAt;

        /// <summary>Tenta lançar. Devolve false se não foi possível (a mana não é gasta).</summary>
        public bool TryCast(AbilityContext ctx)
        {
            if (!IsReady || !Cast(ctx)) return false;
            readyAt = Time.time + cooldown;
            return true;
        }

        protected abstract bool Cast(AbilityContext ctx);
    }

    /// <summary>O que uma habilidade precisa de saber no momento do lançamento.</summary>
    public struct AbilityContext
    {
        public Transform caster;
        public LockOnTarget target;   // alvo selecionado (pode ser null)
        public Vector3 aimPoint;      // ponto no chão debaixo do rato
        public Vector3 moveDirection; // direção de WASD no plano (zero se parado)
    }
}
