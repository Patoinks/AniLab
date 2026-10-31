using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Bola de Vento (elemento ar), canalizada como a Bola de Água:
    ///   tecla 6           — começa a canalizar (pode-se andar): rajadas de vento vêm em espiral de 360° à volta
    ///                       do player e entram numa bola no ombro, que cresce até <see cref="maxRadius"/>
    ///                       (cada rajada custa <see cref="manaPerGust"/>; a mana não regenera enquanto canaliza).
    ///   clique esquerdo   — lança a bola (a direito, o ar não cai).
    ///   segurar e soltar  — enquanto se segura, a bola comprime-se num núcleo denso a girar cada vez mais depressa;
    ///                       ao soltar sai como uma bala de ar (mais comprimida = mais rápida, rebenta o nevoeiro).
    /// Impacto: solta abre um tornado pequeno; comprimida (≥ <see cref="pressureImpactFrom"/>) vira uma lança de ar
    /// — um vórtice deitado que atravessa o alvo, com onda de choque.
    /// Usa os materiais do tornado de vento (WindStreak, WindDust).
    /// </summary>
    public class WindBallAbility : Ability
    {
        [Header("Materiais do vento")]
        public Material streakMaterial, sparkMaterial, shellMaterial, coreMaterial, discMaterial, dustMaterial;
        [Tooltip("Poeira em modo esfera (WindDust com _Shape = 1).")]
        public Material ballDustMaterial;
        [Tooltip("Ar a girar à volta do tornado do impacto (KonoSuba/Distortion); opcional.")]
        public Material distortionMaterial;

        [Header("Canalização")]
        [Tooltip("Mana por cada rajada que entra na bola.")]
        public float manaPerGust = 0.5f;
        [Tooltip("Rajadas por segundo enquanto canaliza.")]
        public float gustsPerSecond = 18f;
        [Tooltip("Tempo que cada rajada demora até à bola (s).")]
        public float gustTravelTime = 0.55f;
        [Tooltip("Rajadas até a bola chegar ao tamanho máximo.")]
        public int gustsForMaxSize = 90;
        [Tooltip("Distância mínima/máxima ao player de onde as rajadas partem (em 360°).")]
        public float gustMinDistance = 2f, gustMaxDistance = 5.5f;

        [Header("Bola")]
        [Tooltip("Posição da bola em relação ao player: direita, cima, frente.")]
        public Vector3 shoulderOffset = new Vector3(0.85f, 2.6f, 0.15f);
        [Tooltip("Raio mínimo para se poder disparar.")]
        public float minFireRadius = 0.15f;
        [Tooltip("Raio máximo a canalizar.")]
        public float maxRadius = 0.75f;

        [Header("Compressão (segurar o clique)")]
        public float compressTime = 0.6f;
        [Range(0.1f, 1f)] public float compressedScale = 0.3f;
        [Tooltip("Velocidade da bala totalmente comprimida (m/s).")]
        public float bulletSpeed = 90f;
        [Tooltip("Abaixo disto (s) conta como clique simples.")]
        public float clickThreshold = 0.12f;
        [Tooltip("A partir desta compressão o impacto é de lança de ar.")]
        [Range(0f, 1f)] public float pressureImpactFrom = 0.5f;

        [Header("Disparo e impacto")]
        public float speed = 20f;
        public float maxRange = 36f;
        [Tooltip("Duração do tornado do impacto (s).")]
        [Min(0.3f)] public float impactDuration = 1.6f;
        [Tooltip("Escala do tornado do impacto com a bola no tamanho máximo (1 = tornado de teste, 5,5 m).")]
        [Range(0.2f, 1f)] public float impactScale = 0.55f;
        [Tooltip("Empurrão aplicado a corpos com Rigidbody no impacto (impulso, bola máxima).")]
        public float knockback = 8f;

        void Reset()
        {
            displayName = "Wind Ball";
            element = AbilityElement.Air;
            slot = 5;
            manaCost = 0f; // paga-se por rajada durante a canalização
            cooldown = 0.6f;
        }

        WindBallProjectile active;

        /// <summary>A bola a ser canalizada agora (null se nenhuma).</summary>
        public WindBallProjectile Active => active;

        protected override bool Cast(AbilityContext ctx)
        {
            if (active != null || ctx.caster == null || streakMaterial == null || sparkMaterial == null || shellMaterial == null
                || coreMaterial == null || discMaterial == null || dustMaterial == null || ballDustMaterial == null) return false;
            active = new GameObject("WindBall").AddComponent<WindBallProjectile>();
            active.Init(this, ctx.caster);
            return true;
        }

        /// <summary>A bola foi disparada: pode-se começar outra.</summary>
        public void Released(WindBallProjectile ball)
        {
            if (active == ball) active = null;
        }

        public Vector3 ShoulderPoint(Transform caster) =>
            caster.position + caster.right * shoulderOffset.x + Vector3.up * shoulderOffset.y + caster.forward * shoulderOffset.z;

        /// <summary>Onde a bola fica com o raio dado: afasta-se para fora e para cima ao crescer.</summary>
        public Vector3 HoldPoint(Transform caster, float r) =>
            ShoulderPoint(caster) + (caster.right * 0.7f + Vector3.up * 0.5f) * Mathf.Max(0f, r - 0.3f);
    }
}
