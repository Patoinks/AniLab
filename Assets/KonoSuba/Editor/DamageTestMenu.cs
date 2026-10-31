using UnityEditor;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Menu de teste dos números de dano: em Play, põe na hotbar (teclas 2–8) uma habilidade de teste por elemento
    /// (fogo, água, eletricidade, ar, terra, gelo, trevas). Nada é gravado na cena.
    /// </summary>
    public static class DamageTestMenu
    {
        [MenuItem("AnimLab/Teste de dano: habilidades na hotbar")]
        static void Install()
        {
            var bar = Object.FindFirstObjectByType<AbilityBar>();
            int n = bar != null ? ElementTestAbility.Install(bar.gameObject) : 0;
            Debug.Log(n > 0
                ? $"Teste de dano: {n} habilidades na hotbar (teclas 2–8). Seleciona um alvo ou fica perto de um inimigo."
                : "Teste de dano: não encontrei o player com AbilityBar.");
        }

        [MenuItem("AnimLab/Teste de dano: habilidades na hotbar", true)]
        static bool CanInstall() => Application.isPlaying;
    }
}
