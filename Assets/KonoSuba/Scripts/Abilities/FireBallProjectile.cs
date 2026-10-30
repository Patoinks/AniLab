using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>Formação curta e voo balístico. A esfera varre o percurso inteiro, incluindo entre fotogramas.</summary>
    public class FireBallProjectile : MonoBehaviour
    {
        FireBallAbility ability;
        Transform caster;
        LockOnTarget target;
        Vector3 aim, velocity;
        readonly RaycastHit[] hits = new RaycastHit[64];
        readonly Collider[] overlaps = new Collider[64];
        FireBallFlightVisual visual;
        float age, travelled;
        bool launched, finished;

        public bool Flying => launched && !finished;
        public Vector3 Velocity => velocity;

        public static FireBallProjectile Create(AbilityContext ctx, FireBallAbility ability)
        {
            var ball = new GameObject("FireBall").AddComponent<FireBallProjectile>();
            ball.ability = ability;
            ball.caster = ctx.caster;
            ball.target = ctx.target;
            ball.aim = ctx.aimPoint;
            ball.transform.position = ability.HoldPoint(ctx.caster);
            ball.transform.rotation = ctx.caster.rotation;
            // Raio visual independente do raio físico (a colisão continua com ability.radius).
            float radius = Mathf.Clamp(ability.visualRadius, 0.9f, 1.1f) * 0.85f;
            ball.visual = FireBallFlightVisual.Create(ball.transform.position, radius, Random.Range(0, 100000),
                ability.ballMaterial);
            ball.Draw(0f);
            return ball;
        }

        void Update()
        {
            if (finished) return;
            if (ability == null || (!launched && caster == null)) { Destroy(gameObject); return; }
            float previous = age;
            age += Time.deltaTime;
            float formation = Mathf.Max(0.05f, ability.formationTime);
            if (!launched)
            {
                transform.position = ability.HoldPoint(caster);
                if (age < formation) { Draw(Time.deltaTime); return; }
                Vector3 to = target != null && target.isActiveAndEnabled ? target.AimPoint : aim;
                Vector3 delta = to - transform.position;
                if (delta.sqrMagnitude < 0.01f) delta = caster.forward;
                float flight = Mathf.Max(delta.magnitude / Mathf.Max(1f, ability.speed), 0.05f);
                velocity = delta / flight + Vector3.up * (0.5f * ability.gravity * flight);
                launched = true;
            }
            // Só o tempo que sobrou depois da formação; subdividir também evita grandes cordas no arco.
            float remaining = Mathf.Min(Time.deltaTime, age - Mathf.Max(previous, formation));
            while (remaining > 0f && !finished)
            {
                float dt = Mathf.Min(remaining, 0.02f);
                Step(dt);
                remaining -= dt;
            }
            if (!finished) Draw(Time.deltaTime);
        }

        bool Own(Collider body) => body == null || (caster != null && body.transform.IsChildOf(caster));

        void Step(float dt)
        {
            // SphereCast não deteta sobreposições iniciais (p.ex. um lançamento encostado a um corpo).
            int count = Physics.OverlapSphereNonAlloc(transform.position, ability.radius, overlaps,
                ~0, QueryTriggerInteraction.Ignore);
            Collider touching = null;
            float closest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (Own(overlaps[i])) continue;
                float d = (overlaps[i].ClosestPoint(transform.position) - transform.position).sqrMagnitude;
                if (d < closest) { closest = d; touching = overlaps[i]; }
            }
            if (touching != null) { Impact(touching); return; }

            Vector3 acceleration = Vector3.down * ability.gravity;
            Vector3 step = velocity * dt + acceleration * (0.5f * dt * dt);
            velocity += acceleration * dt;
            float distance = step.magnitude;
            if (distance < 1e-6f) return;
            count = Physics.SphereCastNonAlloc(transform.position, ability.radius, step / distance,
                hits, distance, ~0, QueryTriggerInteraction.Ignore);
            int nearest = -1;
            closest = float.PositiveInfinity;
            // Filtrar o lançador ANTES de escolher o primeiro obstáculo, para não ocultar um alvo próximo.
            for (int i = 0; i < count; i++)
                if (!Own(hits[i].collider) && hits[i].distance < closest)
                { nearest = i; closest = hits[i].distance; }
            if (nearest >= 0)
            {
                transform.position += step / distance * closest;
                Impact(hits[nearest].collider);
                return;
            }
            transform.position += step;
            travelled += distance;
            // O domínio tem o plano de água em y=0; a colisão física continua a ter prioridade.
            if (transform.position.y <= ability.radius && WaterSim.HasWaterAt(transform.position)) Impact(null);
            else if (travelled >= ability.maxRange || age > 5f) { finished = true; Destroy(gameObject); }
        }

        void Impact(Collider body)
        {
            if (finished) return;
            finished = true;
            FireBallEffect.Impact(transform.position, body, caster, ability);
            Destroy(gameObject);
        }

        void Draw(float dt)
        {
            float f = Mathf.SmoothStep(0f, 1f, age / Mathf.Max(0.05f, ability.formationTime));
            if (Flying && velocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(velocity);
            if (visual != null) visual.Tick(dt, transform.position, Flying ? velocity : Vector3.zero, f);
        }

        // Qualquer saída (impacto, alcance máximo, lançador perdido) apaga o visual; as faúlhas morrem sozinhas.
        void OnDestroy()
        {
            if (visual != null) visual.Release();
        }
    }
}
