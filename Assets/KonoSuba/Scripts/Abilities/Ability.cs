using UnityEngine;

namespace AnimLab.KonoSuba
{
    // (novos elementos só no fim: os valores ficam guardados como números nas cenas)
    public enum AbilityElement { Neutral, Water, Lightning, Fire, Air, Earth, Ice, Dark }

    /// <summary>
    /// Base das habilidades. Cada habilidade é um componente no player; a AbilityBar trata das teclas,
    /// da mana e da recarga, e mostra tudo no HUD.
    /// </summary>
    public abstract class Ability : MonoBehaviour
    {
        public string displayName = "Ability";
        public AbilityElement element = AbilityElement.Neutral;
        [Tooltip("Espaço da barra (0 = tecla 1).")]
        public int slot;
        public float manaCost = 10f;
        public float cooldown = 1f;

        float readyAt;
        CharacterStats stats;
        bool statsLooked;

        /// <summary>Ficha do lançador (só o player tem): velocidade de lançamento, poder mágico, recarga.</summary>
        public CharacterStats Stats
        {
            get
            {
                if (!statsLooked) { stats = GetComponent<CharacterStats>(); statsLooked = true; }
                return stats;
            }
        }
        /// <summary>Formações e canalizações correm este fator mais depressa.</summary>
        public float CastSpeed => Stats != null ? Stats.CastSpeed : 1f;
        /// <summary>Multiplicador do dano das magias (ataque mágico).</summary>
        public float SpellPower => Stats != null ? Stats.SpellScale : 1f;

        public float CooldownRemaining01 => cooldown <= 0f ? 0f : Mathf.Clamp01((readyAt - Time.time) / cooldown);
        public bool IsReady => Time.time >= readyAt;

        /// <summary>Tenta lançar. Devolve false se não foi possível (a mana não é gasta).</summary>
        public bool TryCast(AbilityContext ctx)
        {
            if (!IsReady || !Cast(ctx)) return false;
            readyAt = Time.time + cooldown * (1f - (Stats != null ? Stats.CooldownReduction : 0f));
            return true;
        }

        protected abstract bool Cast(AbilityContext ctx);

        /// <summary>Termina já o que esta habilidade tem em curso offline (ex.: uma canalização); usado ao entrar online.</summary>
        public virtual void CancelActive() { }
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
