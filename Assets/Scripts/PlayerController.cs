using UnityEngine;

namespace AnimLab
{
    /// <summary>
    /// Movimento em terceira pessoa com CharacterController.
    /// WASD move relativo à câmara, Shift corre, Espaço salta.
    /// Alimenta o Animator com os parâmetros "Speed" (0 parado, 0.5 andar, 1 correr) e "Grounded".
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movimento")]
        public float walkSpeed = 2f;
        public float runSpeed = 5.5f;
        public float acceleration = 12f;
        public float turnSpeed = 720f;

        [Header("Salto e gravidade")]
        public float jumpHeight = 1.2f;
        public float gravity = -20f;
        [Tooltip("Tempo de tolerância para continuar 'no chão' depois de sair de uma rampa.")]
        public float groundedGrace = 0.12f;

        [Header("Referências")]
        public Transform cameraTransform;
        public Animator animator;

        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int GroundedHash = Animator.StringToHash("Grounded");

        CharacterController controller;
        Vector3 horizontalVelocity;
        float verticalVelocity;
        float lastGroundedTime;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        }

        void Update()
        {
            Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            input = Vector2.ClampMagnitude(input, 1f);
            bool running = Input.GetKey(KeyCode.LeftShift);

            // Direção relativa à câmara, projetada no plano horizontal.
            Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
            Vector3 right = cameraTransform != null ? cameraTransform.right : Vector3.right;
            forward.y = 0f; right.y = 0f;
            forward.Normalize(); right.Normalize();
            Vector3 moveDir = forward * input.y + right * input.x;

            float targetSpeed = (running ? runSpeed : walkSpeed) * input.magnitude;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, moveDir.normalized * targetSpeed, acceleration * Time.deltaTime);

            if (moveDir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
            }

            if (controller.isGrounded)
            {
                lastGroundedTime = Time.time;
                if (verticalVelocity < 0f) verticalVelocity = -2f; // mantém colado ao chão
            }
            bool grounded = Time.time - lastGroundedTime <= groundedGrace;

            if (grounded && Input.GetButtonDown("Jump"))
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                lastGroundedTime = -1f;
                grounded = false;
            }

            verticalVelocity += gravity * Time.deltaTime;
            controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);

            if (animator != null)
            {
                animator.SetFloat(SpeedHash, NormalizedSpeed(horizontalVelocity.magnitude), 0.1f, Time.deltaTime);
                animator.SetBool(GroundedHash, grounded);
            }
        }

        // Converte velocidade real para o espaço do blend tree: 0 = idle, 0.5 = walk, 1 = run.
        float NormalizedSpeed(float speed)
        {
            if (speed <= walkSpeed) return 0.5f * speed / walkSpeed;
            return 0.5f + 0.5f * Mathf.InverseLerp(walkSpeed, runSpeed, speed);
        }
    }
}
