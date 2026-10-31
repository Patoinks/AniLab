using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Ciclo de vida de uma Bola de Vento canalizada (o mesmo da Bola de Água):
    ///   1. Canalizar  — rajadas chegam em espiral de 360° a ritmo constante (mana por rajada); a bola cresce.
    ///   2. Comprimir  — a segurar o clique: a canalização pára, a bola encolhe, gira cada vez mais depressa e treme.
    ///   3. Disparo    — ao soltar: clique simples lança a direito; comprimida sai como bala de ar e rebenta o nevoeiro.
    ///   4. Impacto    — WindBallImpact: tornado pequeno (solta) ou lança de ar que atravessa (comprimida).
    /// Visual, tudo malhas 3D com os shaders do vento: anéis em arco em planos inclinados próprios (cada um a girar
    /// no seu eixo), núcleo com estrias em espiral, poeira em órbita (WindDust modo esfera) e esteira em hélice
    /// atrás da bola (só em voo; mais longa quanto mais comprimida).
    /// </summary>
    public class WindBallProjectile : MonoBehaviour
    {
        enum State { Channeling, Compressing, Flying }

        WindBallAbility ability;
        Transform caster;
        AbilityBar mana;
        LockOnTarget target;
        Vector3 aimPoint, velocity;
        State state;
        float stateTime, travelled, radius, compression, spawnClock;
        int absorbed;
        bool finished;

        Transform visual, rings, wake;
        readonly List<Transform> spinners = new List<Transform>();
        readonly List<float> speeds = new List<float>();
        readonly List<Renderer> ringRenderers = new List<Renderer>(), wakeRenderers = new List<Renderer>();
        Renderer core, dust;
        MaterialPropertyBlock block;

        // Rajadas a caminho da bola (fitas em espiral, filhas da bola, com a cabeça do rasto a avançar).
        class Gust { public Transform t; public Renderer r; public float age; }
        readonly List<Gust> gusts = new List<Gust>();
        const float BaseRadius = 1f; // o visual é construído com raio 1 e escalado para o raio atual

        /// <summary>Usado por scripts de teste para simular o botão esquerdo seguro.</summary>
        public bool HeldByScript { get; set; }

        public void Init(WindBallAbility ability, Transform caster)
        {
            this.ability = ability;
            this.caster = caster;
            mana = caster.GetComponent<AbilityBar>();
            if (mana != null) mana.SetChanneling(true);
            transform.position = ability.ShoulderPoint(caster);
            block = new MaterialPropertyBlock();
            Build(Random.Range(0, 100000));
        }

        void Build(int seed)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            visual = new GameObject("Bola").transform;
            visual.SetParent(transform, false);
            visual.localScale = Vector3.zero;

            rings = new GameObject("Anéis").transform;
            rings.SetParent(visual, false);
            for (int i = 0; i < 16; i++)
            {
                bool hero = i < 4;
                float r = BaseRadius * (0.7f + R() * 0.45f);
                float a0 = R() * Mathf.PI * 2f, turns = 0.45f + R() * 0.8f;
                float width = hero ? 0.1f + R() * 0.08f : 0.025f + R() * 0.05f;
                Mesh mesh = WindTornado.Strip(64, t =>
                {
                    float a = a0 + t * turns * Mathf.PI * 2f;
                    return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                }, width, R(), hero ? 0.9f + R() * 0.1f : 0.4f + R() * 0.4f, false);
                var ring = Part("Anel " + i, mesh, ability.streakMaterial, rings, ringRenderers);
                ring.localRotation = Quaternion.Euler(R() * 180f - 90f, R() * 360f, R() * 180f - 90f);
                spinners.Add(ring);
                speeds.Add((420f + R() * 420f) * (R() < 0.2f ? -1f : 1f));
            }
            core = Part("Núcleo", Sphere(BaseRadius * 0.55f), ability.coreMaterial, visual, null).GetComponent<Renderer>();
            dust = Part("Poeira", WindTornado.DustMesh(rng, 160, 0, 1f), ability.ballDustMaterial, transform, null).GetComponent<Renderer>();
            dust.GetComponent<MeshFilter>().sharedMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 8f);

            // Esteira: hélices ao longo de -Z (atrás do voo), a apertar para o fim.
            wake = new GameObject("Esteira").transform;
            wake.SetParent(transform, false);
            for (int i = 0; i < 7; i++)
            {
                float a0 = i * Mathf.PI * 2f / 7f + R() * 0.5f, turns = 0.8f + R() * 0.9f;
                float length = 1.6f + R() * 1.4f, r0 = 0.75f + R() * 0.3f;
                Mesh mesh = WindTornado.Strip(80, t =>
                {
                    float a = a0 + t * turns * Mathf.PI * 2f, r = r0 * (1f - 0.75f * t);
                    return new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, -t * length);
                }, 0.03f + R() * 0.06f, R(), 0.5f + R() * 0.5f, false, p => new Vector3(p.x, p.y, 0f).normalized);
                Part("Esteira " + i, mesh, ability.streakMaterial, wake, wakeRenderers);
            }
        }

        Transform Part(string name, Mesh mesh, Material material, Transform parent, List<Renderer> list)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            list?.Add(r);
            return go.transform;
        }

        /// <summary>Esfera UV aberta nos polos: uv.x à volta, uv.y de baixo para cima (WindStreak modo casca).</summary>
        static Mesh Sphere(float radius)
        {
            const int Seg = 48, Rings = 24;
            var verts = new Vector3[(Seg + 1) * (Rings + 1)];
            var uvs = new Vector2[verts.Length];
            var colors = new Color[verts.Length];
            for (int y = 0; y <= Rings; y++)
                for (int x = 0; x <= Seg; x++)
                {
                    float u = x / (float)Seg, v = y / (float)Rings;
                    float a = u * Mathf.PI * 2f, b = (v - 0.5f) * Mathf.PI;
                    int i = y * (Seg + 1) + x;
                    verts[i] = new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(b), Mathf.Sin(a) * Mathf.Cos(b)) * radius;
                    uvs[i] = new Vector2(u, v);
                    colors[i] = new Color(0.5f, 1f, 0f, 1f);
                }
            var tris = new int[Seg * Rings * 6];
            int t = 0;
            for (int y = 0; y < Rings; y++)
                for (int x = 0; x < Seg; x++)
                {
                    int a = y * (Seg + 1) + x, b = a + Seg + 1;
                    tris[t++] = a; tris[t++] = a + 1; tris[t++] = b;
                    tris[t++] = a + 1; tris[t++] = b + 1; tris[t++] = b;
                }
            var mesh = new Mesh { name = "Núcleo da bola de vento", vertices = verts, uv = uvs, colors = colors, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        void OnDestroy()
        {
            StopChanneling();
            if (ability != null) ability.Released(this);
        }

        void StopChanneling()
        {
            foreach (Gust g in gusts) if (g.t != null) Destroy(g.t.gameObject);
            gusts.Clear();
            if (mana != null) mana.SetChanneling(false);
            mana = null;
        }

        void Update()
        {
            if (finished) return;
            if (ability == null || caster == null && state != State.Flying) { Destroy(gameObject); return; }
            stateTime += Time.deltaTime;
            switch (state)
            {
                case State.Channeling: Channel(); break;
                case State.Compressing: Compress(); break;
                case State.Flying: Flying(); break;
            }
            if (!finished) Draw(Time.deltaTime);
        }

        float TargetRadius => ability.maxRadius * Mathf.Pow(Mathf.Clamp01(absorbed / (float)Mathf.Max(1, ability.gustsForMaxSize)), 1f / 3f);
        float ShownRadius => radius * Mathf.Lerp(1f, ability.compressedScale, 1f - (1f - compression) * (1f - compression));

        void Channel()
        {
            // Ritmo constante; no tamanho máximo deixam de vir rajadas (e de gastar mana).
            if (absorbed + gusts.Count < ability.gustsForMaxSize)
            {
                spawnClock += Time.deltaTime * ability.gustsPerSecond;
                while (spawnClock >= 1f)
                {
                    spawnClock -= 1f;
                    if (mana != null && !mana.TrySpend(ability.manaPerGust)) break;
                    SpawnGust();
                }
            }
            TickGusts(Time.deltaTime);
            radius = Mathf.Lerp(radius, TargetRadius, 1f - Mathf.Exp(-8f * Time.deltaTime));
            transform.position = ability.HoldPoint(caster, radius) + Vector3.up * (Mathf.Sin(stateTime * 5f) * 0.03f);
            if (Input.GetMouseButtonDown(0)) StartCompress();
        }

        // Uma rajada parte de um ponto à volta do player (360°, junto ao chão) e entra na bola em espiral.
        void SpawnGust()
        {
            float a = Random.value * Mathf.PI * 2f;
            float d = Random.Range(ability.gustMinDistance, ability.gustMaxDistance);
            Vector3 start = caster.position + new Vector3(Mathf.Cos(a) * d, Random.Range(0.2f, 1.6f), Mathf.Sin(a) * d) - transform.position;
            float turns = Random.Range(0.35f, 0.8f) * (Random.value < 0.5f ? -1f : 1f);
            float lift = Random.Range(0.3f, 1.2f);
            Mesh mesh = WindTornado.Strip(48, t =>
            {
                // Aproxima-se a encurtar o raio e a rodar à volta do eixo vertical da bola; arqueia para cima.
                float e = 1f - (1f - t) * (1f - t);
                Vector3 p = Quaternion.AngleAxis(turns * 360f * t, Vector3.up) * (start * (1f - e));
                return p + Vector3.up * Mathf.Sin(Mathf.PI * t) * lift;
            }, Random.Range(0.07f, 0.16f), Random.value, Random.Range(0.7f, 1f), false, p => p.sqrMagnitude > 1e-6f ? p.normalized : Vector3.up);
            var g = new Gust { t = Part("Rajada", mesh, ability.streakMaterial, transform, null) };
            g.r = g.t.GetComponent<Renderer>();
            g.t.GetComponent<MeshFilter>().sharedMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 16f);
            gusts.Add(g);
        }

        void TickGusts(float dt)
        {
            for (int i = gusts.Count - 1; i >= 0; i--)
            {
                Gust g = gusts[i];
                g.age += dt;
                float t = g.age / ability.gustTravelTime;
                if (t >= 1f)
                {
                    Destroy(g.t.gameObject);
                    gusts.RemoveAt(i);
                    absorbed++;
                    continue;
                }
                // A cabeça do rasto percorre a fita até à bola.
                block.Clear();
                block.SetFloat("_Head", t);
                block.SetFloat("_Window", 0.6f);
                block.SetFloat("_Base", 0f);
                block.SetFloat("_Sway", 0f);
                block.SetFloat("_Wobble", 0.05f);
                block.SetFloat("_Intensity", 5f);
                g.r.SetPropertyBlock(block);
            }
        }

        public void StartCompress()
        {
            if (state != State.Channeling || radius < ability.minFireRadius) return;
            StopChanneling();
            state = State.Compressing;
            stateTime = 0f;
        }

        void Compress()
        {
            float held = stateTime;
            compression = held < ability.clickThreshold ? 0f : Mathf.Clamp01((held - ability.clickThreshold) / (ability.compressTime / ability.CastSpeed));
            transform.position = ability.HoldPoint(caster, ShownRadius) + Random.insideUnitSphere * 0.015f * compression;
            if (!Input.GetMouseButton(0) && !HeldByScript) Fire();
        }

        void Fire()
        {
            var lockOn = caster.GetComponent<LockOn>();
            target = lockOn != null ? lockOn.Current : null;
            Camera cam = Camera.main;
            Ray ray = cam != null ? cam.ScreenPointToRay(Input.mousePosition) : new Ray(caster.position, caster.forward);
            aimPoint = new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float d) ? ray.GetPoint(d) : caster.position + caster.forward * 10f;
            Launch();
        }

        /// <summary>Dispara já, com a compressão pedida (usado também por scripts de teste).</summary>
        public void Launch(LockOnTarget forcedTarget, Vector3 forcedAim, float forcedCompression)
        {
            target = forcedTarget;
            aimPoint = forcedAim;
            compression = Mathf.Clamp01(forcedCompression);
            Launch();
        }

        void Launch()
        {
            if (radius < ability.minFireRadius) radius = ability.minFireRadius;
            StopChanneling();
            ability.Released(this);
            state = State.Flying;
            stateTime = 0f;
            float c = compression * compression;
            Vector3 to = target != null && target.isActiveAndEnabled ? target.AimPoint : aimPoint;
            Vector3 delta = to - transform.position;
            if (delta.sqrMagnitude < 0.01f) delta = caster != null ? caster.forward : Vector3.forward;
            velocity = delta.normalized * Mathf.Lerp(ability.speed, ability.bulletSpeed, c);
            if (compression > 0.25f) PressureRelease(c);
        }

        // Ar comprimido a sair: o nevoeiro rebenta num anel à saída, o recuo sopra para trás e fica um corredor.
        void PressureRelease(float c)
        {
            Vector3 from = transform.position;
            Vector3 flat = Vector3.ProjectOnPlane(velocity, Vector3.up);
            Vector3 ground = new Vector3(from.x, 0f, from.z);
            FogField.Blast(ground, 7.5f * c, Mathf.Min(1f, 1.5f * c), 5f, 0.18f, 0.25f, false);
            FogField.Wind(ground, flat, ability.maxRange, 2.6f, Mathf.Min(1f, 1.5f * c), 6f, true, 0.4f);
            FogField.Wind(ground, -flat, 12f, 8f, Mathf.Min(1f, 1.2f * c), 4.5f, false, 0.2f);
        }

        bool Own(Collider body) => body == null || (caster != null && body.transform.IsChildOf(caster));

        void Flying()
        {
            float dt = Time.deltaTime;
            Vector3 step = velocity * dt;
            float distance = step.magnitude;
            if (distance > 1e-6f && Physics.SphereCast(transform.position, Mathf.Max(0.1f, ShownRadius * 0.7f), step / distance,
                    out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore) && !Own(hit.collider))
            {
                transform.position += step / distance * hit.distance;
                Impact(hit.collider.gameObject.layer == 4 ? null : hit.collider);
                return;
            }
            transform.position += step;
            travelled += distance;
            if (travelled > ability.maxRange || transform.position.y < ShownRadius * 0.5f) Impact(null);
        }

        void Impact(Collider body)
        {
            if (finished) return;
            finished = true;
            WindBallImpact.Create(transform.position, velocity, body, ability, radius / Mathf.Max(0.01f, ability.maxRadius), compression);
            Destroy(gameObject);
        }

        void Draw(float dt)
        {
            bool flying = state == State.Flying;
            float shown = ShownRadius;
            transform.rotation = flying && velocity.sqrMagnitude > 0.01f ? Quaternion.LookRotation(velocity)
                : caster != null ? caster.rotation : transform.rotation;

            // Comprimida: mais pequena, esticada no sentido do voo e a girar muito mais depressa.
            float c = compression;
            float stretch = flying ? Mathf.Lerp(1.05f, 2.2f, c * c) : 1f;
            visual.localScale = new Vector3(shown, shown, shown * stretch);
            float spin = Mathf.Lerp(1f, 3.2f, c);
            for (int i = 0; i < spinners.Count; i++) spinners[i].Rotate(0f, speeds[i] * spin * dt, 0f, Space.Self);
            wake.Rotate(0f, 0f, 380f * spin * dt, Space.Self);
            wake.localScale = new Vector3(Mathf.Max(shown, 0.1f), Mathf.Max(shown, 0.1f), Mathf.Lerp(1f, 3.5f, c));

            float grow = Mathf.Clamp01(radius / Mathf.Max(0.01f, ability.minFireRadius));
            block.Clear();
            block.SetFloat("_Fade", grow);
            block.SetFloat("_Sway", 0f);
            block.SetFloat("_Wobble", 0.03f * shown);
            block.SetFloat("_Intensity", 2.8f * (1f + c));
            foreach (Renderer r in ringRenderers) r.SetPropertyBlock(block);
            block.SetFloat("_Fade", flying ? 1f : 0f);
            foreach (Renderer r in wakeRenderers) r.SetPropertyBlock(block);
            block.SetFloat("_Fade", grow);
            block.SetFloat("_Intensity", 1.4f + 2.2f * c);
            core.SetPropertyBlock(block);

            block.Clear();
            block.SetFloat("_Radius", Mathf.Max(shown, 0.05f));
            block.SetFloat("_Spin", 3.5f * spin);
            block.SetFloat("_Sway", 0f);
            block.SetFloat("_Fade", grow);
            dust.SetPropertyBlock(block);
        }
    }

    /// <summary>
    /// Impacto da bola de vento.
    ///   Solta: um tornado pequeno (WindTornado) que cresce, gira e se desfaz, com onda de choque no chão.
    ///   Comprimida: lança de ar — o mesmo vórtice deitado no sentido do voo, a atravessar o alvo, com onda de
    ///   choque à volta do ponto de impacto e o nevoeiro a rebentar.
    /// Empurra corpos com Rigidbody (mais com a bola maior).
    /// </summary>
    public class WindBallImpact : MonoBehaviour
    {
        WindBallAbility ability;
        WindTornado tornado;
        Transform shock;
        Renderer shockRenderer;
        MaterialPropertyBlock block;
        float age, size;
        bool lance;

        public static void Create(Vector3 point, Vector3 velocity, Collider body, WindBallAbility ability, float size01, float compression)
        {
            bool lance = compression >= ability.pressureImpactFrom;
            Vector3 dir = velocity.sqrMagnitude > 1e-4f ? velocity.normalized : Vector3.forward;
            Vector3 origin = point;
            if (!lance)
            {
                // O tornado assenta no chão por baixo do ponto de impacto.
                origin.y = Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore)
                    && (body == null || !hit.collider.transform.IsChildOf(body.transform)) ? hit.point.y : 0f;
            }

            var go = new GameObject("WindBallImpact");
            go.transform.position = origin;
            // Lança: o eixo do vórtice (Y local) segue o voo e começa um pouco antes do alvo.
            if (lance) go.transform.SetPositionAndRotation(point - dir * 0.6f, Quaternion.FromToRotation(Vector3.up, dir));
            var impact = go.AddComponent<WindBallImpact>();
            impact.ability = ability;
            impact.block = new MaterialPropertyBlock();
            impact.lance = lance;
            impact.size = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(size01));

            var root = new GameObject("Vórtice");
            root.transform.SetParent(go.transform, false);
            root.transform.localScale = Vector3.one * 0.01f;
            impact.tornado = root.AddComponent<WindTornado>();
            impact.tornado.streakMaterial = ability.streakMaterial;
            impact.tornado.sparkMaterial = ability.sparkMaterial;
            impact.tornado.shellMaterial = ability.shellMaterial;
            impact.tornado.coreMaterial = ability.coreMaterial;
            impact.tornado.discMaterial = ability.discMaterial;
            impact.tornado.dustMaterial = ability.dustMaterial;
            impact.tornado.distortionMaterial = ability.distortionMaterial;
            impact.tornado.streaks = 22;
            impact.tornado.sparks = 24;
            impact.tornado.dust = 220;
            impact.tornado.groundDust = lance ? 0 : 70;
            impact.tornado.seed = Random.Range(0, 100000);

            // Onda de choque: no chão (solta) ou perpendicular ao voo, à volta do ponto de impacto (lança).
            var shock = new GameObject("Onda de choque");
            shock.transform.SetParent(go.transform, false);
            if (lance) shock.transform.localPosition = Vector3.up * 0.6f;
            shock.AddComponent<MeshFilter>().sharedMesh = WindTornado.Disc(0.75f, 1f, lance ? 0f : 0.04f);
            impact.shockRenderer = shock.AddComponent<MeshRenderer>();
            impact.shockRenderer.sharedMaterial = ability.discMaterial;
            impact.shockRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            impact.shock = shock.transform;

            Vector3 ground = new Vector3(point.x, 0f, point.z);
            FogField.Blast(ground, (lance ? 6f : 4.5f) * impact.size, lance ? 1f : 0.7f, 5f, 0.2f, 0.3f, false);

            Rigidbody rb = body != null ? body.attachedRigidbody : null;
            if (rb != null && !rb.isKinematic)
            {
                Vector3 push = new Vector3(dir.x, 0f, dir.z).normalized + Vector3.up * (lance ? 0.3f : 0.8f);
                rb.AddForce(push.normalized * ability.knockback * impact.size * (lance ? 1.5f : 1f), ForceMode.Impulse);
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            float life = (ability != null ? ability.impactDuration : 1.6f) * (lance ? 0.7f : 1f);
            float t = age / life;
            if (t >= 1f) { Destroy(gameObject); return; }

            // Vórtice: cresce depressa, mantém-se e desfaz-se a alargar. A lança é mais fina e mais comprida.
            float grow = 1f - Mathf.Pow(1f - Mathf.Clamp01(age / (lance ? 0.12f : 0.25f)), 3f);
            float fadeOut = Mathf.Clamp01((1f - t) / 0.4f);
            float baseScale = (ability != null ? ability.impactScale : 0.55f) * size;
            float spread = 1f + 0.25f * (1f - fadeOut);
            tornado.transform.localScale = lance
                ? new Vector3(baseScale * 0.45f * spread, baseScale * (0.3f + 0.9f * grow), baseScale * 0.45f * spread)
                : Vector3.one * baseScale * (0.3f + 0.7f * grow) * spread;
            tornado.SetFade(Mathf.Clamp01(age / 0.1f) * fadeOut, lance ? 0f : 1f);

            // Onda de choque: anel que abre até ~4 m nos primeiros 0,5 s e apaga.
            float s = Mathf.Clamp01(age / 0.5f);
            shock.localScale = Vector3.one * Mathf.Lerp(0.6f, 4.2f * size, 1f - (1f - s) * (1f - s));
            block.Clear();
            block.SetFloat("_Fade", (1f - s) * (1f - s));
            block.SetFloat("_Intensity", 2.4f);
            block.SetFloat("_Wobble", 0f);
            block.SetFloat("_Sway", 0f);
            shockRenderer.SetPropertyBlock(block);
        }
    }
}
