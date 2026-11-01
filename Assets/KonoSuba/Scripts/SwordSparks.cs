using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Faíscas de metal: losangos 3D esticados na direção do voo (não são billboards), com gravidade,
    /// a encolher e a arrefecer (branco → cor) ao longo da vida. Desenhadas com DrawMesh, sem GameObjects.
    /// Também um clarão curto (esfera aditiva) no ponto de impacto.
    /// </summary>
    public class SwordSparks : MonoBehaviour
    {
        struct Spark { public Vector3 pos, vel; public float age, life, size; public Color color; }
        struct Flash { public Vector3 pos; public float age, life, size; public Color color; }

        static SwordSparks instance;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EdgeId = Shader.PropertyToID("_Edge");

        readonly List<Spark> sparks = new List<Spark>();
        readonly List<Flash> flashes = new List<Flash>();
        Mesh diamond, ball;
        Material material;
        MaterialPropertyBlock block;

        static SwordSparks Get()
        {
            if (instance != null) return instance;
            var shader = Shader.Find("KonoSuba/SwordSpark");
            if (shader == null) return null;
            instance = new GameObject("SwordSparks") { hideFlags = HideFlags.HideInHierarchy }.AddComponent<SwordSparks>();
            instance.material = new Material(shader) { enableInstancing = false };
            instance.block = new MaterialPropertyBlock();
            instance.diamond = Diamond();
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            instance.ball = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            return instance;
        }

        /// <param name="dir">Direção principal (as faíscas abrem num cone à volta dela).</param>
        /// <param name="spread">Abertura do cone, 0 (fio) a 1 (hemisfério).</param>
        public static void Burst(Vector3 point, Vector3 dir, int count, Color color, float speed = 7f, float spread = 0.6f, float flash = 0.35f)
        {
            SwordSparks s = Get();
            if (s == null) return;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.up;
            for (int i = 0; i < count; i++)
            {
                Vector3 v = (dir + Random.insideUnitSphere * spread * 1.6f).normalized * speed * Random.Range(0.45f, 1.25f);
                s.sparks.Add(new Spark
                {
                    pos = point, vel = v, life = Random.Range(0.14f, 0.32f),
                    size = Random.Range(0.6f, 1.2f), color = color
                });
            }
            if (flash > 0f) s.flashes.Add(new Flash { pos = point, life = 0.09f, size = flash, color = color });
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                Spark p = sparks[i];
                p.age += dt;
                if (p.age >= p.life) { sparks.RemoveAt(i); continue; }
                p.vel += Physics.gravity * 0.9f * dt;
                p.vel *= 1f - 2.5f * dt; // arrasto
                p.pos += p.vel * dt;
                sparks[i] = p;

                float k = p.age / p.life;
                float speed = p.vel.magnitude;
                // Comprimento segue a velocidade (rasto de luz); a espessura encolhe com a idade.
                float length = Mathf.Clamp(speed * 0.022f, 0.04f, 0.22f) * p.size;
                float width = 0.012f * p.size * (1f - k * 0.7f);
                Quaternion rot = speed > 1e-3f ? Quaternion.LookRotation(p.vel / speed) : Quaternion.identity;
                Matrix4x4 m = Matrix4x4.TRS(p.pos - p.vel / Mathf.Max(speed, 1e-3f) * length * 0.5f, rot, new Vector3(width, width, length));
                Color c = Color.Lerp(Color.white, p.color, Mathf.Clamp01(k * 1.6f)) * Mathf.Lerp(6f, 1.5f, k);
                block.SetColor(ColorId, c);
                block.SetFloat(EdgeId, 0.55f);
                Graphics.DrawMesh(diamond, m, material, 0, null, 0, block, false, false);
            }
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                Flash f = flashes[i];
                f.age += dt;
                if (f.age >= f.life) { flashes.RemoveAt(i); continue; }
                flashes[i] = f;
                float k = f.age / f.life;
                float size = f.size * Mathf.Lerp(0.6f, 1.2f, k);
                block.SetColor(ColorId, Color.Lerp(Color.white, f.color, 0.4f) * (1f - k) * (1f - k) * 1.6f);
                block.SetFloat(EdgeId, 0f);
                Graphics.DrawMesh(ball, Matrix4x4.TRS(f.pos, Quaternion.identity, Vector3.one * size), material, 0, null, 0, block, false, false);
            }
        }

        // Losango com 4 lados ao longo de Z (de -0.5 a 0.5), ponta fina atrás.
        static Mesh sharedDiamond;
        /// <summary>O mesmo losango 3D das faíscas (também usado na estrela do aviso de parry).</summary>
        public static Mesh DiamondMesh => sharedDiamond != null ? sharedDiamond : sharedDiamond = Diamond();
        static Mesh Diamond()
        {
            var v = new[]
            {
                new Vector3(0f, 0f, 0.5f), new Vector3(0f, 0f, -0.5f),
                new Vector3(1f, 0f, 0.25f), new Vector3(0f, 1f, 0.25f), new Vector3(-1f, 0f, 0.25f), new Vector3(0f, -1f, 0.25f),
            };
            var t = new[] { 0, 2, 3, 0, 3, 4, 0, 4, 5, 0, 5, 2, 1, 3, 2, 1, 4, 3, 1, 5, 4, 1, 2, 5 };
            var mesh = new Mesh { name = "SparkDiamond", vertices = v, triangles = t };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
