using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;

namespace AnimLab.Netcode.Editor
{
    /// <summary>
    /// Mantém Sim/RulesFingerprint.cs igual ao hash do código das regras (Sim, Rollback, Transport).
    /// Dois jogadores com versões diferentes do projeto deixam de se ver no lobby e o handshake recusa-os.
    /// </summary>
    [InitializeOnLoad]
    static class RulesFingerprintGenerator
    {
        const string Root = "Assets/KonoSuba/Netcode";
        const string Output = Root + "/Sim/RulesFingerprint.cs";
        static readonly string[] Folders = { "Sim", "Rollback", "Transport" };

        static RulesFingerprintGenerator() { EditorApplication.delayCall += Refresh; }

        static void Refresh()
        {
            ulong value = Compute();
            if (value == AnimLab.Netcode.Sim.RulesFingerprint.Value) return;
            File.WriteAllText(Output, Source(value), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(Output);
        }

        // FNV-1a 64 sobre "caminho\nconteúdo\n" de cada ficheiro, por ordem ordinal; fins de linha normalizados (LF),
        // para dar o mesmo valor em todos os PCs, qualquer que seja o autocrlf do git.
        internal static ulong Compute()
        {
            var files = Folders.SelectMany(f => Directory.GetFiles(Path.Combine(Root, f), "*.cs", SearchOption.AllDirectories))
                .Select(p => p.Replace('\\', '/').Substring(Root.Length + 1))
                .Where(p => p != "Sim/RulesFingerprint.cs")
                .OrderBy(p => p, StringComparer.Ordinal);
            ulong hash = 14695981039346656037UL;
            foreach (string relative in files)
            {
                string text = File.ReadAllText(Path.Combine(Root, relative)).Replace("\r\n", "\n");
                foreach (byte b in Encoding.UTF8.GetBytes(relative + "\n" + text + "\n")) { hash ^= b; hash *= 1099511628211UL; }
            }
            return hash;
        }

        internal static string Source(ulong value) =>
            "namespace AnimLab.Netcode.Sim\n{\n" +
            "    /// <summary>Gerado por RulesFingerprintGenerator (Editor): hash do código das regras. Não editar à mão.</summary>\n" +
            "    public static class RulesFingerprint\n    {\n" +
            $"        public const ulong Value = 0x{value:X16}UL;\n" +
            "    }\n}\n";
    }
}
