using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Canalização da Bola de Água: enquanto dura, bolinhas de água saltam da película à frente do player,
    /// fazem uma curva no ar e entram na bola, que cresce com cada uma. Cada bolinha custa
    /// <see cref="WaterBallAbility.manaPerDroplet"/> de mana (sem mana, deixam de sair).
    /// As bolinhas vêm de toda a volta (360°), até <see cref="WaterBallAbility.dropletMaxDistance"/>; enquanto dura,
    /// uma aura de mana brilha na água à volta do player.
    /// Cada salto deixa uma ondinha e tira essa água do chão. Onde não há água, as bolinhas condensam
    /// no ar à volta da bola, mais devagar (<see cref="WaterBallAbility.generatedTimeMultiplier"/>).
    /// </summary>
    public class WaterGather : MonoBehaviour
    {
        class Blob
        {
            public Vector3 start, side;
            public float age, travel, radius, merge;
            public Vector3 normal;
            public bool fromWater, entered;
            public Transform body;
        }

        Transform caster;
        WaterBallAbility ability;
        AbilityBar mana;
        readonly List<Blob> blobs = new List<Blob>();
        readonly Stack<Transform> pool = new Stack<Transform>();
        float spawnDebt, elapsed;
        Transform aura;
        float auraLevel;
        MeshRenderer auraRenderer;
        MaterialPropertyBlock auraBlock;
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        /// <summary>Volume (m³) que já entrou na bola.</summary>
        public float Volume { get; private set; }
        /// <summary>Verdadeiro no frame em que uma bolinha entra na bola (para a bola abanar).</summary>
        public bool AbsorbedThisFrame { get; private set; }
        /// <summary>Falso quando falta mana para mais bolinhas.</summary>
        public bool Feeding { get; private set; } = true;

        // Água que cada bolinha leva: a bola chega ao máximo exatamente com manaForMaxSize de mana gasta.
        float DropletShare => WaterBallAbility.VolumeOf(ability.maxRadius) * ability.manaPerDroplet / Mathf.Max(ability.manaForMaxSize, 1e-3f);

        /// <summary>Centro e raio da bola como se vê (postos pela bola em cada frame).</summary>
        public Vector3 BallCenter { get; set; }
        public float BallRadius { get; set; }
        /// <summary>Chamado quando uma bolinha fura a superfície da bola, com a direção (mundo) do ponto de entrada.</summary>
        public System.Action<Vector3> OnPierce;

        public Vector3 Center => BallRadius > 0f ? BallCenter : ability.HoldPoint(caster, 0f);

        public static WaterGather Create(Transform caster, WaterBallAbility ability)
        {
            var g = new GameObject("WaterGather").AddComponent<WaterGather>();
            g.caster = caster;
            g.ability = ability;
            g.mana = caster.GetComponent<AbilityBar>();
            g.CreateAura();
            return g;
        }

        /// <summary>Avança a canalização: bolinhas a um ritmo constante enquanto houver mana.</summary>
        public void Tick(float dt)
        {
            AbsorbedThisFrame = false;
            elapsed += dt;

            bool water = WaterSim.HasWaterAt(caster.position);
            float rate = ability.channelDropletsPerSecond;
            if (!water) rate /= ability.generatedTimeMultiplier;

            spawnDebt += rate * dt;
            while (spawnDebt >= 1f)
            {
                spawnDebt -= 1f;
                // Conta também as bolinhas a caminho: assim a mana gasta pára exatamente no tamanho máximo.
                if (Volume + (Pending + 1) * DropletShare > WaterBallAbility.VolumeOf(ability.maxRadius) * 1.0001f) { spawnDebt = 0f; break; }
                if (mana != null && !mana.TrySpend(ability.manaPerDroplet)) { Feeding = false; spawnDebt = 0f; break; }
                Feeding = true;
                Spawn(water);
            }

            UpdateAura(dt);

            Vector3 c = Center;
            for (int i = blobs.Count - 1; i >= 0; i--)
            {
                Blob b = blobs[i];
                b.age += dt;

                if (b.entered)
                {
                    // A fundir-se: presa à superfície no ponto onde tocou, afunda e funde-se com a bola
                    // (a parte de fora é a bossa que fica na superfície; a de dentro vê-se refratada).
                    b.merge += dt / MergeTime;
                    float k = Mathf.Clamp01(b.merge);
                    float r = b.radius * (1f - k);
                    b.body.position = c + b.normal * (BallRadius + r * (0.4f - 1.4f * k));
                    b.body.localScale = Vector3.one * r * 2f;
                    if (k >= 1f) Remove(i);
                    continue;
                }

                float u = Mathf.Clamp01(b.age / b.travel);
                // Puxada pela bola como por gravidade: um pequeno salto para fora da água e depois quase a direito,
                // a acelerar até tocar na bola (começa devagar, chega depressa).
                Vector3 control = Vector3.Lerp(b.start, c, 0.25f) + Vector3.up * (b.fromWater ? 0.45f : 0.1f) + b.side * 0.12f;
                float e = u * u; // aceleração constante, como a cair
                Vector3 p = (1 - e) * (1 - e) * b.start + 2f * (1 - e) * e * control + e * e * c;
                Vector3 vel = 2f * (1 - e) * (control - b.start) + 2f * e * (c - control);
                float s = b.radius * 2f * Mathf.Clamp01(u / 0.15f);
                b.body.position = p;
                if (vel.sqrMagnitude > 1e-4f) b.body.rotation = Quaternion.LookRotation(vel);
                b.body.localScale = Vector3.one * s;

                // Ao tocar na superfície, a água passa logo a ser da bola e a bolinha começa a fundir-se.
                float dist = Vector3.Distance(p, c);
                bool touches = BallRadius > 0.05f ? dist < BallRadius + b.radius * 0.4f : u >= 1f;
                if (!touches) continue;
                Volume += DropletShare;
                AbsorbedThisFrame = true;
                if (BallRadius <= 0.05f) { Remove(i); continue; } // as primeiras formam a bola
                b.entered = true;
                b.merge = 0f;
                b.normal = dist > 1e-4f ? (p - c) / dist : Vector3.up;
                OnPierce?.Invoke(b.normal);
            }
        }

        const float MergeTime = 0.12f;

        // Bolinhas ainda a caminho (a água delas ainda não está na bola).
        int Pending
        {
            get
            {
                int n = 0;
                foreach (Blob b in blobs) if (!b.entered) n++;
                return n;
            }
        }

        void Remove(int i)
        {
            blobs[i].body.gameObject.SetActive(false);
            pool.Push(blobs[i].body);
            blobs.RemoveAt(i);
        }

        void Spawn(bool water)
        {
            var b = new Blob { radius = Random.Range(0.035f, 0.07f) };
            Vector3 c = Center;
            // De qualquer lado (360°), entre a distância mínima e a máxima; as de mais longe demoram mais.
            float angle = Random.value * Mathf.PI * 2f;
            float k = Mathf.Sqrt(Random.value); // espalhadas por igual na área, não amontoadas perto
            float dist = Mathf.Lerp(ability.dropletMinDistance, ability.dropletMaxDistance, k);
            Vector3 ground = caster.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
            ground.y = 0f;
            b.fromWater = water && WaterSim.HasWaterAt(ground);
            if (b.fromWater)
            {
                b.start = ground;
                b.travel = ability.dropletTravelTime * Mathf.Lerp(0.75f, 1.5f, k);

                // A gota sai mesmo da película: ondinha e menos água naquele ponto.
                float vol = DropletShare;
                // Só parte sai da película (e numa zona larga): senão, com tantas bolinhas, o chão secava aos buracos.
                WaterSim.TransferVolume(b.start, 0.8f, -vol * 0.35f);
                if (WaterRipples.Instance != null) WaterRipples.Instance.Touch(b.start, 0.35f);
            }
            else
            {
                b.start = c + Random.onUnitSphere * Random.Range(1f, 1.6f);
                b.start.y = Mathf.Max(b.start.y, 0.4f);
                b.travel = ability.dropletTravelTime * ability.generatedTimeMultiplier;
            }
            b.side = Vector3.Cross(Vector3.up, (c - b.start).normalized).normalized * Random.Range(-1f, 1f);

            b.body = pool.Count > 0 ? pool.Pop() : CreateBody();
            b.body.gameObject.SetActive(true);
            b.body.localScale = Vector3.zero;
            blobs.Add(b);
        }

        // Disco de luz no chão, centrado no player, do tamanho da zona de onde vêm as bolinhas.
        void CreateAura()
        {
            if (ability.auraMaterial == null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(go.GetComponent<Collider>());
            go.name = "ManaAura";
            aura = go.transform;
            aura.SetParent(transform, false);
            aura.rotation = Quaternion.Euler(90f, 0f, 0f);
            aura.localScale = Vector3.one * (ability.dropletMaxDistance + 1f) * 2f;
            auraRenderer = go.GetComponent<MeshRenderer>();
            auraRenderer.sharedMaterial = ability.auraMaterial;
            auraRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            auraRenderer.receiveShadows = false;
            auraBlock = new MaterialPropertyBlock();
        }

        void UpdateAura(float dt)
        {
            if (aura == null) return;
            aura.position = new Vector3(caster.position.x, 0.015f, caster.position.z);
            // Aparece em ~0.4 s; sem mana, apaga-se quase toda.
            auraLevel = Mathf.MoveTowards(auraLevel, Feeding ? 1f : 0.2f, dt * 2.5f);
            auraBlock.SetFloat(IntensityId, auraLevel);
            auraRenderer.SetPropertyBlock(auraBlock);
        }

        Transform CreateBody()
        {
            var t = new GameObject("Droplet").transform;
            t.SetParent(transform, false);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = WaterBallProjectile.SmoothSphere();
            var mr = t.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ability.orbMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.sortingOrder = -1; // desenhada antes da bola, para se ver refratada lá dentro
            var block = new MaterialPropertyBlock();
            block.SetFloat(Shader.PropertyToID("_Jiggle"), 0f);
            block.SetFloat(Shader.PropertyToID("_Aura"), 0.7f);
            mr.SetPropertyBlock(block);
            return t;
        }
    }
}
