using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Marca um objeto como alvo selecionável (inimigos). Mostra o círculo no chão quando está selecionado.
    /// </summary>
    public class LockOnTarget : MonoBehaviour
    {
        public static readonly List<LockOnTarget> All = new List<LockOnTarget>();

        public string displayName = "Inimigo";
        [Tooltip("Altura do ponto que a câmara enquadra (m).")]
        public float aimHeight = 1.1f;
        [Tooltip("Círculo no chão debaixo do alvo; mostrado só enquanto está selecionado.")]
        public GameObject lockMarker;
        public float markerSpinSpeed = 60f;

        public Vector3 AimPoint => transform.position + Vector3.up * aimHeight;

        void OnEnable() { All.Add(this); SetLocked(false); }
        void OnDisable() => All.Remove(this);

        public void SetLocked(bool locked)
        {
            if (lockMarker != null) lockMarker.SetActive(locked);
        }

        void LateUpdate()
        {
            // O círculo roda devagar no chão, como o indicador de alvo do Metin2.
            if (lockMarker != null && lockMarker.activeSelf)
                lockMarker.transform.Rotate(0f, markerSpinSpeed * Time.deltaTime, 0f, Space.World);
        }
    }
}
