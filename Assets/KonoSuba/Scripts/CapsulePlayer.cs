using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Player de teste: cápsula com dois pés procedurais.
    /// Controlo misto: WASD relativo à câmara (Genshin/Roblox/Smite) ou manter o botão esquerdo
    /// para andar até ao cursor (PoE/LoL). Shift corre.
    /// Os pés dão passos alternados: quando um pé fica para trás, levanta-se num arco e assenta
    /// à frente; ao assentar cria uma onda na água.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class CapsulePlayer : MonoBehaviour
    {
        [Header("Movimento")]
        public float walkSpeed = 3.5f;
        public float runSpeed = 6.5f;
        public float acceleration = 20f;
        public float turnSpeed = 900f;
        public float gravity = -20f;
        public bool clickToMove = true;

        [Header("Pés")]
        public Transform leftFoot;
        public Transform rightFoot;
        [Tooltip("Afastamento lateral dos pés em relação ao centro (m).")]
        public float footSpacing = 0.17f;
        [Tooltip("Distância que um pé fica para trás antes de dar um passo (m).")]
        public float stepThreshold = 0.35f;
        public float stepDuration = 0.18f;
        public float stepHeight = 0.12f;
        [Tooltip("Quanto o pé se adianta na direção do movimento ao assentar.")]
        public float stepLead = 0.12f;

        [Header("Corpo")]
        public Transform body;
        public float bobAmount = 0.04f;

        [Header("Água")]
        public float walkRippleStrength = 0.35f;
        public float runRippleStrength = 0.6f;

        CharacterController controller;
        Camera cam;
        Vector3 velocity;
        float verticalVelocity;
        Vector3 moveTarget;
        bool hasMoveTarget;
        Vector3 bodyBase;

        class Foot
        {
            public Transform t;
            public float side;
            public Vector3 planted, from, to;
            public bool stepping;
            public float progress;
        }

        Foot left, right;
        Foot lastStepped;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            cam = Camera.main;
            if (body != null) bodyBase = body.localPosition;
            left = new Foot { t = leftFoot, side = -1f };
            right = new Foot { t = rightFoot, side = 1f };
            foreach (Foot f in new[] { left, right })
            {
                if (f.t == null) continue;
                f.t.SetParent(null, true); // os pés vivem no mundo: ficam assentes enquanto o corpo avança
                f.planted = FootHome(f, 0f);
                f.t.position = f.planted;
            }
        }

        void OnDestroy()
        {
            if (left?.t != null) Destroy(left.t.gameObject);
            if (right?.t != null) Destroy(right.t.gameObject);
        }

        void Update()
        {
            if (cam == null) cam = Camera.main;
            var rig = cam != null ? cam.GetComponent<Metin2Camera>() : null;

            // Com a câmara Metin2, os controlos (WASD, clique, alvo) vêm dela.
            Vector3 wish;
            bool setFacing = false;
            float facingYaw = 0f;
            if (rig != null)
            {
                Metin2Camera.MoveCommand cmd = rig.ReadMovement(transform);
                wish = cmd.wish;
                setFacing = cmd.setFacing;
                facingYaw = cmd.facingYaw;
            }
            else wish = ReadInput();

            bool running = Input.GetKey(KeyCode.LeftShift);
            float maxSpeed = running ? runSpeed : walkSpeed;
            velocity = Vector3.MoveTowards(velocity, wish * maxSpeed, acceleration * Time.deltaTime);

            // Vira-se para o alvo quando a câmara o pede (a ir até ele / ao pé dele); senão para onde anda.
            Quaternion? look = null;
            if (setFacing) look = Quaternion.Euler(0f, facingYaw, 0f);
            else if (wish.sqrMagnitude > 0.001f) look = Quaternion.LookRotation(wish, Vector3.up);
            if (look.HasValue)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look.Value, turnSpeed * Time.deltaTime);

            verticalVelocity = controller.isGrounded ? -2f : verticalVelocity + gravity * Time.deltaTime;
            controller.Move((velocity + Vector3.up * verticalVelocity) * Time.deltaTime);

            UpdateFeet(running);
            UpdateBody();
        }

        // Controlos simples para quando não há rig de câmara: WASD relativo à câmara ou clique para andar.
        Vector3 ReadInput()
        {
            Vector2 axis = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (axis.sqrMagnitude > 0.01f)
            {
                hasMoveTarget = false;
                Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
                Vector3 right = cam != null ? cam.transform.right : Vector3.right;
                fwd.y = 0f; right.y = 0f;
                Vector3 dir = fwd.normalized * axis.y + right.normalized * axis.x;
                return Vector3.ClampMagnitude(dir, 1f);
            }

            if (clickToMove && cam != null && Input.GetMouseButton(0))
            {
                var ground = new Plane(Vector3.up, Vector3.zero);
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                if (ground.Raycast(ray, out float d)) { moveTarget = ray.GetPoint(d); hasMoveTarget = true; }
            }

            if (hasMoveTarget)
            {
                Vector3 to = moveTarget - transform.position;
                to.y = 0f;
                if (to.magnitude < 0.15f) { hasMoveTarget = false; return Vector3.zero; }
                // Abranda ao chegar, para não ultrapassar o destino.
                return to.normalized * Mathf.Clamp01(to.magnitude / 0.6f);
            }
            return Vector3.zero;
        }

        // Onde o pé "quer" estar: ao lado do corpo, adiantado na direção do movimento.
        Vector3 FootHome(Foot f, float lead)
        {
            Vector3 p = transform.position + transform.right * (f.side * footSpacing);
            Vector3 v = velocity; v.y = 0f;
            p += v.normalized * (lead * Mathf.Clamp01(v.magnitude / walkSpeed));
            p.y = 0.04f;
            return p;
        }

        void UpdateFeet(bool running)
        {
            float speed01 = Mathf.Clamp01(velocity.magnitude / runSpeed);
            float duration = Mathf.Lerp(stepDuration, stepDuration * 0.7f, speed01);
            float threshold = Mathf.Lerp(stepThreshold, stepThreshold * 1.6f, speed01);

            foreach (Foot f in new[] { left, right })
            {
                if (f.t == null) continue;
                if (f.stepping)
                {
                    f.progress += Time.deltaTime / duration;
                    float k = Mathf.Clamp01(f.progress);
                    float ease = k * k * (3f - 2f * k);
                    Vector3 p = Vector3.Lerp(f.from, f.to, ease);
                    p.y += Mathf.Sin(k * Mathf.PI) * stepHeight * (0.6f + 0.6f * speed01);
                    f.t.position = p;
                    f.t.rotation = Quaternion.Slerp(f.t.rotation, transform.rotation, 0.5f);
                    if (k >= 1f)
                    {
                        f.stepping = false;
                        f.planted = f.to;
                        Land(f, running);
                    }
                }
                else
                {
                    f.t.position = f.planted;
                }
            }

            // Só um pé de cada vez, e alterna: dá o ritmo esquerdo-direito.
            if (left.stepping || right.stepping) return;
            Foot candidate = lastStepped == left ? right : left;
            Foot other = candidate == left ? right : left;
            Foot pick = null;
            if (NeedsStep(candidate, threshold)) pick = candidate;
            else if (NeedsStep(other, threshold * 1.4f)) pick = other;
            if (pick == null) return;

            pick.stepping = true;
            pick.progress = 0f;
            pick.from = pick.planted;
            pick.to = FootHome(pick, stepLead + threshold * 0.5f);
            lastStepped = pick;
        }

        bool NeedsStep(Foot f, float threshold)
        {
            if (f.t == null) return false;
            Vector3 home = FootHome(f, 0f);
            Vector3 d = home - f.planted; d.y = 0f;
            // Parado: arruma os pés quando ficam tortos depois de rodar.
            float limit = velocity.sqrMagnitude < 0.01f ? 0.12f : threshold;
            return d.magnitude > limit;
        }

        void Land(Foot f, bool running)
        {
            if (WaterRipples.Instance == null) return;
            float moving = Mathf.Clamp01(velocity.magnitude / walkSpeed);
            float strength = Mathf.Lerp(0.15f, running ? runRippleStrength : walkRippleStrength, moving);
            WaterRipples.Instance.Spawn(f.planted, strength);
        }

        void UpdateBody()
        {
            if (body == null) return;
            // O corpo sobe ligeiramente a meio de cada passo e desce ao assentar: dá peso ao andar.
            float lift = 0f;
            if (left.stepping) lift = Mathf.Max(lift, Mathf.Sin(Mathf.Clamp01(left.progress) * Mathf.PI));
            if (right.stepping) lift = Mathf.Max(lift, Mathf.Sin(Mathf.Clamp01(right.progress) * Mathf.PI));
            body.localPosition = bodyBase + Vector3.up * (lift * bobAmount);
        }
    }
}
