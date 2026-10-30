using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Teste da interação com o nevoeiro, enquanto não há ataques a sério:
    ///   8 — rajada de vento em 360° à volta do player (limpa o nevoeiro e varre a água em círculo)
    ///   9 — rajada de vento para a frente do player
    /// (as teclas 1–7 são das habilidades)
    /// É exatamente o que um ataque real chamaria.
    /// </summary>
    public class FogInteractionDemo : MonoBehaviour
    {
        [Tooltip("Origem dos golpes. Sem player, usa a câmara.")]
        public Transform player;
        public float blastRadius = 7f;
        public float windLength = 24f;
        public float windWidth = 5f;

        void Update()
        {
            Transform from = player != null ? player : (Camera.main != null ? Camera.main.transform : null);
            if (from == null) return;
            Vector3 ground = new Vector3(from.position.x, 0f, from.position.z);

            if (Input.GetKeyDown(KeyCode.Alpha8))
                FogField.Blast(ground, blastRadius);

            if (Input.GetKeyDown(KeyCode.Alpha9))
                FogField.Wind(ground, from.forward, windLength, windWidth);
        }
    }
}
