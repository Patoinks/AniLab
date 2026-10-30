using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Toque na água (passos, objetos a cair, impactos): empurra a água na simulação (WaterSim)
    /// e dispara os salpicos no ponto de contacto.
    /// Para passos de personagens: WaterRipples.Instance.Spawn(posicaoDoPe).
    /// </summary>
    public class WaterRipples : MonoBehaviour
    {
        public static WaterRipples Instance { get; private set; }

        [System.Serializable]
        public struct ImpactVfx
        {
            public ParticleSystem system;
            [Tooltip("Partículas emitidas num impacto de força 1.")]
            public int count;
        }

        [Header("Onda")]
        [Tooltip("Profundidade da cova feita por um toque de força 1 (m).")]
        public float depth = 0.015f;
        [Tooltip("Raio da zona empurrada (m).")]
        public float radius = 0.3f;
        [Tooltip("Ondulação inicial (rad/m): dá vários anéis em vez de um só, como na água real.")]
        public float ringFrequency = 6f;

        [Header("VFX do impacto")]
        [Tooltip("Sistemas emitidos no ponto de impacto (gotas, coroa, spray...).")]
        public ImpactVfx[] impactVfx = new ImpactVfx[0];

        void OnEnable() => Instance = this;

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Só o salpico (gotas e spray), sem onda. Para rajadas de vento a varrer a água.</summary>
        public void Spray(Vector3 position, float strength = 0.4f) => EmitVfx(position, strength, 0.6f);

        /// <summary>Toque na água em <paramref name="position"/>: onda na simulação + salpico. strength ~0..1.5.</summary>
        public void Spawn(Vector3 position, float strength = 1f)
        {
            strength *= Random.Range(0.85f, 1.15f); // nenhum toque é igual ao anterior
            WaterSim.Impulse(position, radius * Mathf.Lerp(0.8f, 1.3f, strength / 1.5f), -depth * strength, ringFrequency);
            EmitVfx(position, strength, 0.8f);
        }

        /// <summary>Só a ondinha, sem salpico (ex.: uma gota a cair na água).</summary>
        public void Touch(Vector3 position, float strength)
        {
            WaterSim.Impulse(position, radius * 0.6f, -depth * strength, ringFrequency);
        }

        void EmitVfx(Vector3 position, float strength, float minRandom)
        {
            Vector3 p = new Vector3(position.x, transform.position.y + 0.03f, position.z);
            var ep = new ParticleSystem.EmitParams { position = p, applyShapeToPosition = true };
            foreach (ImpactVfx vfx in impactVfx)
            {
                if (vfx.system == null) continue;
                int count = Mathf.RoundToInt(vfx.count * strength * Random.Range(minRandom, 1.2f));
                if (count > 0) vfx.system.Emit(ep, count);
            }
        }
    }
}
