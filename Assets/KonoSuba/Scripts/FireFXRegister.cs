using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>Põe o renderer deste objeto no caminho de cor do fogo (FireFX) enquanto estiver ativo.</summary>
    [RequireComponent(typeof(Renderer))]
    public class FireFXRegister : MonoBehaviour
    {
        /// <summary>Raio da casca à volta do centro dos bounds (ordem de desenho; ver FireFX).</summary>
        public float radius;
        Renderer r;

        /// <summary>Muda o raio já registado (o AddComponent regista logo no OnEnable, antes de se atribuir o campo).</summary>
        public void SetRadius(float value)
        {
            radius = value;
            if (isActiveAndEnabled) FireFX.Register(r, radius);
        }

        void OnEnable()
        {
            r = GetComponent<Renderer>();
            FireFX.Register(r, radius);
        }

        void OnDisable() => FireFX.Unregister(r);
    }
}
