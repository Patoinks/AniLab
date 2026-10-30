using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// A bola de fogo em voo: fogo estilizado (KonoSuba/FireToon) num quadrado virado para a câmara — núcleo
    /// amarelo, chamas em espiral, traços escuros e faúlhas —, com a cauda para trás no plano do ecrã (cresce com
    /// a velocidade; parada, as chamas sobem) e uma luz laranja a tremeluzir.
    /// Na formação a bola cresce de um ponto, já a arder.
    /// </summary>
    public class FireBallFlightVisual : MonoBehaviour
    {
        MeshRenderer ball;
        Light glow;
        MaterialPropertyBlock block;
        float unit, seed, age;

        /// <param name="radius">Raio visível das chamas (m).</param>
        public static FireBallFlightVisual Create(Vector3 position, float radius, int seed, Material material)
        {
            var v = new GameObject("FireBallVisual").AddComponent<FireBallFlightVisual>();
            v.unit = radius / 1.4f; // o núcleo é a unidade do shader; as chamas chegam a ~1,4
            v.seed = seed % 997;
            v.block = new MaterialPropertyBlock();
            v.ball = FireBallVisual.Billboard("Bola", v.transform, material);
            v.glow = FireBallVisual.Glow(v.transform, 7f);
            v.Tick(0f, position, Vector3.zero, 0f);
            return v;
        }

        public void Tick(float dt, Vector3 position, Vector3 velocity, float formation)
        {
            age += dt;
            float speed = velocity.magnitude;
            Vector3 back = speed > 0.5f ? -velocity / speed : Vector3.zero;
            float size = unit * Mathf.Lerp(0.12f, 1f, formation);

            // Cauda: a direção para trás projetada no plano do ecrã (encolhe quando a bola vem para a câmara).
            Camera cam = Camera.main;
            Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;
            Vector3 local = Quaternion.Inverse(face) * (back + Vector3.up * 0.25f);
            Vector2 tail = new Vector2(local.x, local.y);
            float foreshortening = tail.magnitude;
            tail = foreshortening > 1e-3f ? tail / foreshortening : Vector2.up;
            float tailLen = 0.6f + 3f * Mathf.Clamp01(speed / 20f) * Mathf.Clamp01(foreshortening);
            float extent = 2.3f + tailLen;

            ball.transform.SetPositionAndRotation(position, face);
            ball.transform.localScale = Vector3.one * size * 2f * extent;
            FireBallVisual.SetToon(ball, block, seed, tail, tailLen, extent, burst: 0f, spin: 1f,
                fade: Mathf.Clamp01(formation * 2f));

            glow.transform.position = position;
            glow.intensity = 3.2f * formation * (0.85f + 0.15f * Mathf.PerlinNoise(age * 9f, seed));
        }

        public void Release() => Destroy(gameObject);
    }
}
