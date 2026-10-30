using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Bola de Água (elemento água), canalizada:
    ///   tecla 2           — começa a canalizar (pode-se andar): bolinhas de água saltam da película do chão,
    ///                       a ritmo constante, e convergem numa bola no ombro que vai crescendo até
    ///                       <see cref="maxRadius"/> (cada bolinha custa <see cref="manaPerDroplet"/> de mana; a mana
    ///                       não regenera enquanto canaliza).
    ///   clique esquerdo   — lança a bola com gravidade.
    ///   segurar e soltar  — enquanto se segura, a bola comprime-se numa bolinha densa; ao soltar sai como uma
    ///                       bala quase instantânea (mais comprimida = mais rápida e mais direita).
    /// O alvo é o selecionado nesse momento ou, sem alvo, o ponto do chão debaixo do rato.
    /// Sem água no chão, as bolinhas condensam do ar <see cref="generatedTimeMultiplier"/>× mais devagar.
    /// Ao cair na água, a água da bola volta à película.
    /// </summary>
    public class WaterBallAbility : Ability
    {
        [Header("Canalização")]
        [Tooltip("Mana por cada bolinha que se junta à bola.")]
        public float manaPerDroplet = 0.5f;
        [Tooltip("Bolinhas por segundo enquanto canaliza (ritmo constante).")]
        public float channelDropletsPerSecond = 24f;
        [Tooltip("Tempo que cada bolinha demora da água até à bola (s).")]
        public float dropletTravelTime = 0.45f;
        [Tooltip("Mana gasta até a bola chegar ao tamanho máximo; cada bolinha leva a mesma parte de água.")]
        public float manaForMaxSize = 90f;
        [Tooltip("Sem água no ambiente demora isto vezes mais (5 = 500%).")]
        public float generatedTimeMultiplier = 5f;

        [Header("Bola")]
        [Tooltip("Posição da bola em relação ao player: direita, cima, frente.")]
        public Vector3 shoulderOffset = new Vector3(0.85f, 2.65f, 0.15f);
        [Tooltip("Distância mínima/máxima ao player de onde as bolinhas saltam da água (em 360°).")]
        public float dropletMinDistance = 1.5f, dropletMaxDistance = 5f;
        [Tooltip("Aura de mana no chão à volta do player enquanto canaliza.")]
        public Material auraMaterial;
        [Tooltip("Material da água no impacto (KonoSuba/WaterSheet).")]
        public Material impactMaterial;
        [Tooltip("Manga de spray do impacto comprimido (KonoSuba/WaterPressureVeil).")]
        public Material veilMaterial;
        [Tooltip("A partir desta compressão o impacto é de lança de água (atravessa e sai pelas costas).")]
        [Range(0f, 1f)] public float pressureImpactFrom = 0.5f;
        [Tooltip("Raio mínimo para se poder disparar.")]
        public float minFireRadius = 0.12f;
        [Tooltip("Raio máximo a canalizar; a partir daí deixam de sair bolinhas.")]
        public float maxRadius = 0.85f;

        [Header("Compressão (segurar o clique)")]
        [Tooltip("Tempo a segurar até a compressão ser total (s).")]
        public float compressTime = 0.6f;
        [Tooltip("Fração do raio que sobra quando totalmente comprimida.")]
        [Range(0.1f, 1f)] public float compressedScale = 0.2f;
        [Tooltip("Velocidade da bala totalmente comprimida (m/s).")]
        public float bulletSpeed = 110f;
        [Tooltip("Abaixo disto (s) conta como clique simples: lançamento normal.")]
        public float clickThreshold = 0.12f;
        [Tooltip("Brilho interno quando totalmente comprimida.")]
        public float compressedGlow = 1.6f;

        [Header("Disparo")]
        [Tooltip("Velocidade de saída (m/s).")]
        public float speed = 22f;
        [Tooltip("Multiplicador da gravidade durante o voo.")]
        public float gravityScale = 1f;
        public float maxRange = 35f;

        [Header("Aspeto")]
        [Tooltip("Material da água (bola e bolinhas que se juntam a ela).")]
        public Material orbMaterial;

        void Reset()
        {
            displayName = "Bola de Água";
            element = AbilityElement.Water;
            slot = 1;
            manaCost = 0f; // paga-se por bolinha durante a canalização
            cooldown = 0.6f;
        }

        public static float VolumeOf(float r) => 4f / 3f * Mathf.PI * r * r * r;
        public static float RadiusOf(float volume) => Mathf.Pow(volume * 3f / (4f * Mathf.PI), 1f / 3f);

        WaterBallProjectile active;

        protected override bool Cast(AbilityContext ctx)
        {
            // Só uma bola canalizada de cada vez: dispara-se com o clique esquerdo.
            if (active != null) return false;
            active = new GameObject("WaterBall").AddComponent<WaterBallProjectile>();
            active.Init(this, ctx.caster);
            return true;
        }

        /// <summary>A bola foi disparada: pode-se começar outra.</summary>
        public void Released(WaterBallProjectile ball)
        {
            if (active == ball) active = null;
        }

        /// <summary>De onde sai a água: um ponto à frente e à direita do player (visível com a câmara por trás).</summary>
        public Vector3 WaterSource(Transform caster)
        {
            Vector3 p = ShoulderPoint(caster) + caster.forward * 1.1f + caster.right * 0.2f;
            p.y = 0f;
            return p;
        }

        public Vector3 ShoulderPoint(Transform caster) =>
            caster.position + caster.right * shoulderOffset.x + Vector3.up * shoulderOffset.y + caster.forward * shoulderOffset.z;

        /// <summary>Onde a bola fica com o raio dado: afasta-se para fora e para cima ao crescer, para não entrar no player.</summary>
        public Vector3 HoldPoint(Transform caster, float r) =>
            ShoulderPoint(caster) + (caster.right * 0.7f + Vector3.up * 0.5f) * Mathf.Max(0f, r - 0.3f);
    }
}
