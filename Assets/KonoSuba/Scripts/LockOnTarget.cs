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

        public string displayName = "Enemy";
        [Tooltip("Altura do ponto que a câmara enquadra (m).")]
        public float aimHeight = 1.1f;
        [Tooltip("Círculo no chão debaixo do alvo; mostrado só enquanto está selecionado.")]
        public GameObject lockMarker;
        public float markerSpinSpeed = 60f;

        public Vector3 AimPoint => transform.position + Vector3.up * aimHeight;

        // Tudo o que se pode selecionar também se pode atingir.
        void Awake()
        {
            if (GetComponent<Combatant>() == null) gameObject.AddComponent<Combatant>();
        }

        void OnEnable() { All.Add(this); SetLocked(false); }
        void OnDisable() => All.Remove(this);

        /// <summary>Cria o círculo a partir de um molde, deitado aos pés e à medida do corpo.</summary>
        public void CreateMarker(GameObject template)
        {
            lockMarker = Instantiate(template, transform);
            lockMarker.name = "TargetRing";
            lockMarker.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            lockMarker.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            float radius = 0.4f;
            if (GetComponent<CharacterController>() is CharacterController cc) radius = cc.radius;
            else if (GetComponent<CapsuleCollider>() is CapsuleCollider cap) radius = cap.radius;
            lockMarker.transform.localScale = Vector3.one * Mathf.Max(1.3f, radius * 4.3f);
            lockMarker.SetActive(false);
        }

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
