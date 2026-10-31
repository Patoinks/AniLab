using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>Estados da Bíblia (Docs/Biblia.md), um por elemento.</summary>
    public enum StatusType { None, Wet, Static, Grounded, Frozen, Asphyxia, Burning, Blind }

    /// <summary>Um golpe: o que a habilidade entrega ao alvo no impacto.</summary>
    public struct HitInfo
    {
        public float damage;
        public AbilityElement element;
        public StatusType status;
        public float statusDuration;
        [Tooltip("Dano à postura: quando a postura parte, o alvo fica em stagger.")]
        public float stagger;
        [Tooltip("Impulso (m/s) aplicado ao alvo; a componente vertical atira-o ao ar.")]
        public Vector3 knockback;
        public Vector3 point;
        public Transform source;
        public bool critical;
    }

    public static class Combat
    {
        /// <summary>Texto grande no mundo (PARRY, CLASH, GUARD BREAK): ponto, texto, cor, tamanho.</summary>
        public static event System.Action<Vector3, string, Color, float> AnyCallout;
        /// <summary>Escreve os callouts na consola (gravações).</summary>
        public static bool LogCallouts;

        public static void Callout(Vector3 point, string text, Color color, float size = 26f)
        {
            if (LogCallouts) Debug.Log($"[CALLOUT] {Time.frameCount} {text} @ {point}");
            AnyCallout?.Invoke(point, text, color, size);
        }

        /// <summary>O corpo que recebe golpes a que este collider pertence (null se não houver).</summary>
        public static Combatant Of(Collider c) => c != null ? c.GetComponentInParent<Combatant>() : null;

        /// <summary>
        /// Explosão: todos os corpos no raio levam o golpe, com o dano a cair até 40% na borda e o
        /// knockback a apontar para fora do centro. O corpo atingido em cheio (direct) leva o golpe inteiro.
        /// </summary>
        public static void Explode(Vector3 center, float radius, HitInfo hit, Combatant direct = null)
        {
            var targets = new List<Combatant>(Combatant.All);
            foreach (Combatant c in targets)
            {
                if (c == null || (hit.source != null && c.transform.IsChildOf(hit.source))) continue;
                Vector3 closest = c.ClosestPoint(center);
                float d = c == direct ? 0f : Vector3.Distance(center, closest);
                if (d > radius) continue;
                float k = Mathf.Lerp(1f, 0.4f, d / Mathf.Max(radius, 0.01f));
                Vector3 away = c.transform.position - center; away.y = 0f;
                if (away.sqrMagnitude < 1e-4f) away = hit.knockback; away.y = 0f;
                HitInfo h = hit;
                h.damage *= k;
                h.stagger *= k;
                h.knockback = away.normalized * (new Vector2(hit.knockback.x, hit.knockback.z).magnitude * k)
                              + Vector3.up * (hit.knockback.y * k);
                h.point = closest;
                c.TakeHit(h);
            }
        }

        /// <summary>Nome do estado como aparece no jogo (o jogo é em inglês).</summary>
        public static string StatusName(StatusType s) => s switch
        {
            StatusType.Wet => "Wet",
            StatusType.Static => "Static",
            StatusType.Grounded => "Grounded",
            StatusType.Frozen => "Frozen",
            StatusType.Asphyxia => "Asphyxia",
            StatusType.Burning => "Burning",
            StatusType.Blind => "Blind",
            _ => ""
        };

        public static Color StatusColor(StatusType s) => s switch
        {
            StatusType.Wet => new Color(0.42f, 0.72f, 1f),
            StatusType.Static => new Color(0.78f, 0.7f, 1f),
            StatusType.Grounded => new Color(0.8f, 0.62f, 0.4f),
            StatusType.Frozen => new Color(0.7f, 0.93f, 1f),
            StatusType.Asphyxia => new Color(0.68f, 0.95f, 0.82f),
            StatusType.Burning => new Color(1f, 0.55f, 0.22f),
            StatusType.Blind => new Color(0.66f, 0.5f, 0.85f),
            _ => Color.white
        };

        public static readonly AbilityElement[] Elements =
            { AbilityElement.Fire, AbilityElement.Water, AbilityElement.Lightning, AbilityElement.Air,
              AbilityElement.Earth, AbilityElement.Ice, AbilityElement.Dark };

        public static readonly StatusType[] Statuses =
            { StatusType.Wet, StatusType.Static, StatusType.Grounded, StatusType.Frozen,
              StatusType.Asphyxia, StatusType.Burning, StatusType.Blind };

        /// <summary>Cor de cada elemento (números de dano, placas).</summary>
        public static Color ElementColor(AbilityElement e) => e switch
        {
            AbilityElement.Water => new Color(0.45f, 0.75f, 1f),
            AbilityElement.Lightning => new Color(0.75f, 0.7f, 1f),
            AbilityElement.Fire => new Color(1f, 0.58f, 0.25f),
            AbilityElement.Air => new Color(0.7f, 1f, 0.85f),
            AbilityElement.Earth => new Color(0.82f, 0.64f, 0.42f),
            AbilityElement.Ice => new Color(0.72f, 0.93f, 1f),
            AbilityElement.Dark => new Color(0.66f, 0.5f, 0.85f),
            _ => new Color(0.95f, 0.95f, 0.95f)
        };
    }
}
