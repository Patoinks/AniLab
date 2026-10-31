using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Ciclo de vida de uma Bola de Água canalizada (sem salpicos: é tudo água "inteira"):
    ///   1. Canalizar  — bolinhas chegam a ritmo constante (1 de mana cada); a bola cresce até ao máximo (WaterGather).
    ///   2. Comprimir  — a segurar o clique esquerdo: a canalização pára e a bola encolhe numa bolinha densa.
    ///   3. Disparo    — ao soltar: clique simples lança com gravidade; comprimida sai como bala quase instantânea.
    ///   4. Impacto    — a água continua como massa contínua (WaterImpactSurface): envolve, atravessa, escorre.
    /// </summary>
    public class WaterBallProjectile : MonoBehaviour
    {
        enum State { Channeling, Compressing, Flying }

        WaterBallAbility ability;
        Transform caster;
        AbilityBar mana;
        LockOnTarget target;
        Vector3 aimPoint;
        WaterGather gather;

        State state;
        float stateTime, travelled, radius, compression, gravityScale = 1f;
        Vector3 velocity;
        Transform orb;
        MeshRenderer orbRenderer;
        MaterialPropertyBlock block;
        float jiggle;
        static readonly int JiggleId = Shader.PropertyToID("_Jiggle");
        static readonly int AbsorbId = Shader.PropertyToID("_Absorb");
        static readonly int SwirlSpeedId = Shader.PropertyToID("_SwirlSpeed");
        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int AuraId = Shader.PropertyToID("_Aura");
        static readonly int ImpactsId = Shader.PropertyToID("_Impacts");
        const int MaxImpacts = 16;
        readonly Vector3[] impactDirs = new Vector3[MaxImpacts];
        readonly float[] impactTimes = new float[MaxImpacts];
        readonly Vector4[] impacts = new Vector4[MaxImpacts];
        int nextImpact;

        public void Init(WaterBallAbility ability, Transform caster)
        {
            this.ability = ability;
            this.caster = caster;
            mana = caster.GetComponent<AbilityBar>();
            if (mana != null) mana.SetChanneling(true);

            transform.position = ability.ShoulderPoint(caster);
            orb = new GameObject("Orb").transform;
            orb.SetParent(transform, false);
            orb.localScale = Vector3.zero;
            orb.gameObject.AddComponent<MeshFilter>().sharedMesh = SmoothSphere();
            orbRenderer = orb.gameObject.AddComponent<MeshRenderer>();
            orbRenderer.sharedMaterial = ability.orbMaterial;
            orbRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            block = new MaterialPropertyBlock();

            gather = WaterGather.Create(caster, ability);
            gather.OnPierce = Pierce;
            for (int i = 0; i < MaxImpacts; i++) impactTimes[i] = -10f;
        }

        static Mesh smoothSphere;

        // Esfera densa (raio 0.5): a esfera do Unity tem poucos polígonos e ficava facetada ao deformar-se.
        public static Mesh SmoothSphere()
        {
            if (smoothSphere != null) return smoothSphere;
            const int lon = 64, lat = 48;
            var verts = new Vector3[(lon + 1) * (lat + 1)];
            var uvs = new Vector2[verts.Length];
            for (int y = 0; y <= lat; y++)
                for (int x = 0; x <= lon; x++)
                {
                    float v = y / (float)lat, u = x / (float)lon;
                    float th = v * Mathf.PI, ph = u * Mathf.PI * 2f;
                    verts[y * (lon + 1) + x] = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), -Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)) * 0.5f;
                    uvs[y * (lon + 1) + x] = new Vector2(u, v);
                }
            var tris = new int[lon * lat * 6];
            int t = 0;
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = y * (lon + 1) + x, b = a + lon + 1;
                    tris[t++] = a; tris[t++] = b; tris[t++] = a + 1;
                    tris[t++] = a + 1; tris[t++] = b; tris[t++] = b + 1;
                }
            smoothSphere = new Mesh { name = "WaterBallSphere", vertices = verts, uv = uvs, triangles = tris };
            // Normais exatas da esfera: calculadas pelos triângulos, os vértices dos polos ficavam com normais
            // diferentes e, ao deformar a bola, abriam um bico no topo.
            var normals = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++) normals[i] = verts[i].normalized;
            smoothSphere.normals = normals;
            smoothSphere.RecalculateBounds();
            return smoothSphere;
        }

        void OnDestroy()
        {
            StopChanneling();
            if (ability != null) ability.Released(this);
        }

        void StopChanneling()
        {
            if (gather != null) Destroy(gather.gameObject);
            gather = null;
            if (mana != null) mana.SetChanneling(false);
            mana = null;
        }

        void Update()
        {
            stateTime += Time.deltaTime;
            // O balanço acalma sozinho; a água a entrar na bola volta a pô-la a abanar.
            jiggle *= Mathf.Exp(-3.5f * Time.deltaTime);
            block.SetFloat(JiggleId, Mathf.Clamp01(jiggle));
            // Água comprimida: mais densa (mais cor) e a girar muito mais depressa por dentro.
            block.SetFloat(AbsorbId, Mathf.Lerp(0.45f, 0.9f, compression));
            block.SetFloat(SwirlSpeedId, Mathf.Lerp(2.5f, 14f, compression));
            // Brilho da pressão, com um pulsar irregular.
            float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 9f, 0.37f);
            block.SetFloat(GlowId, ability.compressedGlow * compression * compression * flicker);
            // Aura de mana: forte enquanto se segura a bola, mais fraca depois de largada.
            block.SetFloat(AuraId, state == State.Channeling || state == State.Compressing ? 1f : 0.5f);
            for (int i = 0; i < MaxImpacts; i++)
            {
                Vector3 d = impactDirs[i];
                impacts[i] = new Vector4(d.x, d.y, d.z, Time.time - impactTimes[i]);
            }
            block.SetVectorArray(ImpactsId, impacts);
            orbRenderer.SetPropertyBlock(block);
            switch (state)
            {
                case State.Channeling: Channel(); break;
                case State.Compressing: Compress(); break;
                case State.Flying: Flying(); break;
            }
        }

        float VolumeRadius => Mathf.Min(ability.maxRadius, WaterBallAbility.RadiusOf(gather != null ? gather.Volume : 0f));

        void Channel()
        {
            // Ritmo constante; ao chegar ao máximo deixam de sair bolinhas (e de gastar mana).
            if (VolumeRadius < ability.maxRadius - 1e-3f) gather.Tick(Time.deltaTime);
            // A bola cresce suavemente até ao volume que já entrou (não aparece de repente com a primeira bolinha).
            radius = Mathf.Lerp(radius, VolumeRadius, 1f - Mathf.Exp(-10f * Time.deltaTime));
            if (gather.AbsorbedThisFrame) jiggle = Mathf.Min(0.5f, jiggle + 0.08f); // o grosso do efeito são as ondas
            transform.position = ability.HoldPoint(caster, radius) + Vector3.up * (Mathf.Sin(stateTime * 6f) * 0.03f);
            orb.localScale = Vector3.one * radius * 2f;
            gather.BallCenter = transform.position;
            gather.BallRadius = radius;

            if (Input.GetMouseButtonDown(0)) StartCompress();
        }

        // Uma bolinha furou a superfície: cova e anel de onda a partir desse ponto (em espaço local da bola).
        void Pierce(Vector3 worldDir)
        {
            impactDirs[nextImpact] = orb.InverseTransformDirection(worldDir).normalized;
            impactTimes[nextImpact] = Time.time;
            nextImpact = (nextImpact + 1) % MaxImpacts;
        }

        /// <summary>Usado por scripts de teste para simular o botão esquerdo seguro.</summary>
        public bool HeldByScript { get; set; }

        /// <summary>Começa a comprimir (clique esquerdo premido); daqui em diante já não entra mais água.</summary>
        public void StartCompress()
        {
            if (state != State.Channeling || radius < ability.minFireRadius) return;
            StopChanneling();
            state = State.Compressing;
            stateTime = 0f;
        }

        void Compress()
        {
            // A segurar: a bola encolhe e fica densa; o volume de água é o mesmo (só muda o aspeto e a força).
            float held = stateTime;
            compression = held < ability.clickThreshold ? 0f
                : Mathf.Clamp01((held - ability.clickThreshold) / (ability.compressTime / ability.CastSpeed));
            float e = 1f - (1f - compression) * (1f - compression);
            float shown = radius * Mathf.Lerp(1f, ability.compressedScale, e);
            jiggle = Mathf.Max(jiggle, 0.15f + compression * 0.25f);

            Vector3 shoulder = ability.HoldPoint(caster, shown);
            transform.position = shoulder + Random.insideUnitSphere * 0.01f * compression; // treme com a pressão
            orb.localScale = Vector3.one * shown * 2f;

            if (!Input.GetMouseButton(0) && !HeldByScript) Fire();
        }

        // Ao soltar: dispara para o alvo selecionado nesse momento ou para o ponto do rato.
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
            StopChanneling();
            ability.Released(this);
            state = State.Flying;
            stateTime = 0f;
            // Comprimida: muito mais rápida e quase sem queda (bala); solta: lançamento com gravidade.
            float c = compression * compression;
            float speed = Mathf.Lerp(ability.speed, ability.bulletSpeed, c);
            gravityScale = ability.gravityScale * Mathf.Lerp(1f, 0.04f, c);
            velocity = Ballistic(transform.position, AimPosition(), speed);
            if (compression > 0.25f) PressureRelease(c);
            jiggle = Mathf.Max(jiggle, 0.6f * (1f - compression)); // a bola solta abana ao sair; a comprimida não
        }

        // A água comprimida sai com tanta força que o ar à volta rebenta: o nevoeiro abre-se num anel à saída,
        // o recuo sopra-o para trás do player, e fica um rasto até ao alvo que depois se dispersa
        // (o vento também cava um rasto fino na película de água).
        void PressureRelease(float c)
        {
            // O rasto segue exatamente o caminho da bala (a direção da velocidade, a partir do ponto de saída).
            Vector3 from = transform.position;
            Vector3 flat = Vector3.ProjectOnPlane(velocity, Vector3.up);
            float length = Vector3.ProjectOnPlane(AimPosition() - from, Vector3.up).magnitude + 3f;
            Vector3 ground = new Vector3(from.x, 0f, from.z);
            // A água é muito mais pesada: o mesmo sopro quase não a mexe (bem menos que ao nevoeiro).
            FogField.Blast(ground, 6.5f * c, Mathf.Min(1f, 1.3f * c), 5f, 0.22f, 0.06f);
            FogField.Wind(ground, flat, Mathf.Min(length, ability.maxRange), 2f, Mathf.Min(1f, 1.3f * c), 6f, true, 0.2f);
            FogField.Wind(ground, -flat, 11f, 7f, Mathf.Min(1f, 1.1f * c), 4.5f, false, 0.1f); // recuo
        }

        // A bala comprimida rebenta no impacto: o nevoeiro explode à volta do ponto onde bateu.
        void ImpactBurst(Vector3 ground)
        {
            float c = compression * compression;
            if (c < 0.06f) return;
            FogField.Blast(ground, 5.5f * c, Mathf.Min(1f, 1.3f * c), 5f, 0.15f, 0.12f);
        }

        float ShownRadius => radius * Mathf.Lerp(1f, ability.compressedScale, 1f - (1f - compression) * (1f - compression));

        // Velocidade inicial para, com a gravidade, chegar ao ponto pedido à velocidade de disparo.
        Vector3 Ballistic(Vector3 from, Vector3 to, float speed)
        {
            float g = Physics.gravity.magnitude * gravityScale;
            Vector3 d = to - from;
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            float time = Mathf.Max(flat.magnitude / speed, 0.02f);
            Vector3 v = flat / time;
            v.y = (d.y + 0.5f * g * time * time) / time;
            return v;
        }

        Vector3 AimPosition()
        {
            if (target != null && target.isActiveAndEnabled) return target.AimPoint;
            return aimPoint;
        }

        void Flying()
        {
            float dt = Time.deltaTime;
            velocity += Physics.gravity * gravityScale * dt;
            Vector3 step = velocity * dt;

            if (Physics.SphereCast(transform.position, ShownRadius * 0.7f, step.normalized, out RaycastHit hit,
                                   step.magnitude, ~0, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(caster))
            {
                // Centro da bola no instante do contacto (não no ponto de contacto: evita um salto).
                transform.position += step.normalized * hit.distance;
                Impact(hit.collider.gameObject.layer == 4 ? null : hit.collider);
                return;
            }

            transform.position += step;
            travelled += step.magnitude;
            // Esticada na direção do voo; comprimida e muito rápida, vira um risco de água.
            orb.rotation = Quaternion.LookRotation(velocity);
            float d = ShownRadius * 2f;
            float stretch = Mathf.Lerp(1.12f, 4f, compression * compression);
            orb.localScale = new Vector3(d * 0.95f, d * 0.95f, d * stretch);

            if (travelled > ability.maxRange || transform.position.y < ShownRadius) Impact(null);
        }

        // Impacto: a bola não é trocada por pedaços — a própria água continua como uma massa contínua
        // (WaterImpactSurface) que se espalma, envolve o corpo, o atravessa, escorre e entra na película.
        void Impact(Collider body)
        {
            Vector3 c = transform.position;
            c.y = Mathf.Max(c.y, ShownRadius);
            ImpactBurst(new Vector3(c.x, 0f, c.z));
            // Comprimida: lança de água (atravessa e descomprime-se atrás); solta: massa que envolve e escorre.
            if (compression >= ability.pressureImpactFrom)
            {
                if (body != null) WaterPressureImpact.OnBody(body, c, velocity, ShownRadius, radius, ability);
                else WaterPressureImpact.OnWater(c, velocity, ShownRadius, radius, ability);
            }
            else WaterImpactSurface.Spawn(c, velocity, ShownRadius, radius, compression, body, ability.impactMaterial);
            Combatant hitBody = Combat.Of(body);
            if (hitBody != null)
            {
                // Mais água = mais dano e mais empurrão; comprimida (lança) fura mais.
                float size01 = Mathf.Clamp01(radius / Mathf.Max(ability.maxRadius, 0.01f));
                float pierce = compression >= ability.pressureImpactFrom ? 1.4f : 1f;
                Vector3 push = velocity; push.y = 0f;
                hitBody.TakeHit(new HitInfo
                {
                    damage = Mathf.Lerp(ability.minDamage, ability.maxDamage, size01) * pierce * ability.SpellPower,
                    element = AbilityElement.Water, status = StatusType.Wet, statusDuration = 6f,
                    stagger = Mathf.Lerp(10f, 60f, size01) * pierce,
                    knockback = push.normalized * Mathf.Lerp(2f, 8f, size01) + Vector3.up * Mathf.Lerp(0f, 2f, size01),
                    point = c, source = caster
                });
            }
            Destroy(gameObject);
        }
    }
}
