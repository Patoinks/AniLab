using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>Ações do jogador que se podem associar a teclas (menu Settings).</summary>
    public enum GameAction
    {
        MoveForward, MoveBack, MoveLeft, MoveRight, Run,
        Attack, Guard, Jump, Roll,
        Ability1, Ability2, Ability3, Ability4, Ability5, Ability6, Ability7, Ability8, Ability9,
        Character, ReleaseTarget, SkillTree
    }

    /// <summary>
    /// Teclas do jogo num só sítio: cada ação tem uma tecla (KeyCode, incluindo botões do rato Mouse0–Mouse6).
    /// O código do jogo lê sempre por aqui (GetDown / Get / GetUp) em vez de usar KeyCode diretamente,
    /// para o menu Settings poder mudar tudo. As escolhas ficam guardadas em PlayerPrefs.
    /// </summary>
    public static class GameInput
    {
        const string PrefsKey = "AnimLab.Bindings.";

        static readonly Dictionary<GameAction, KeyCode> Defaults = new Dictionary<GameAction, KeyCode>
        {
            { GameAction.MoveForward, KeyCode.W }, { GameAction.MoveBack, KeyCode.S },
            { GameAction.MoveLeft, KeyCode.A }, { GameAction.MoveRight, KeyCode.D },
            { GameAction.Run, KeyCode.LeftShift },
            { GameAction.Attack, KeyCode.Mouse0 }, { GameAction.Guard, KeyCode.F },
            { GameAction.Jump, KeyCode.Space }, { GameAction.Roll, KeyCode.Q },
            { GameAction.Ability1, KeyCode.Alpha1 }, { GameAction.Ability2, KeyCode.Alpha2 },
            { GameAction.Ability3, KeyCode.Alpha3 }, { GameAction.Ability4, KeyCode.Alpha4 },
            { GameAction.Ability5, KeyCode.Alpha5 }, { GameAction.Ability6, KeyCode.Alpha6 },
            { GameAction.Ability7, KeyCode.Alpha7 }, { GameAction.Ability8, KeyCode.Alpha8 },
            { GameAction.Ability9, KeyCode.Alpha9 },
            { GameAction.Character, KeyCode.C }, { GameAction.ReleaseTarget, KeyCode.Escape },
            { GameAction.SkillTree, KeyCode.V },
        };

        static readonly Dictionary<GameAction, KeyCode> current = new Dictionary<GameAction, KeyCode>();
        static bool loaded;

        /// <summary>Com um menu aberto (ex.: Settings a capturar uma tecla) o jogo não recebe teclas.</summary>
        public static bool Suspended;

        /// <summary>Uma tecla mudou (o HUD atualiza os rótulos).</summary>
        public static event Action<GameAction, KeyCode> Rebound;

        public static KeyCode Key(GameAction a)
        {
            Load();
            return current.TryGetValue(a, out KeyCode k) ? k : KeyCode.None;
        }

        public static KeyCode DefaultKey(GameAction a) => Defaults.TryGetValue(a, out KeyCode k) ? k : KeyCode.None;

        public static bool GetDown(GameAction a) => !Suspended && Input.GetKeyDown(Key(a));
        public static bool Get(GameAction a) => !Suspended && Input.GetKey(Key(a));
        public static bool GetUp(GameAction a) => !Suspended && Input.GetKeyUp(Key(a));

        /// <summary>Eixos de movimento (-1..1) a partir das quatro teclas.</summary>
        public static Vector2 MoveAxis() => new Vector2(
            (Get(GameAction.MoveRight) ? 1f : 0f) - (Get(GameAction.MoveLeft) ? 1f : 0f),
            (Get(GameAction.MoveForward) ? 1f : 0f) - (Get(GameAction.MoveBack) ? 1f : 0f));

        /// <summary>Muda a tecla de uma ação e guarda. Devolve a ação que tinha essa tecla (ficou sem nenhuma), se houver.</summary>
        public static GameAction? Rebind(GameAction a, KeyCode key)
        {
            Load();
            GameAction? displaced = null;
            foreach (var kv in current)
                if (kv.Key != a && kv.Value == key && key != KeyCode.None) { displaced = kv.Key; break; }
            if (displaced.HasValue) Set(displaced.Value, KeyCode.None);
            Set(a, key);
            PlayerPrefs.Save();
            return displaced;
        }

        public static void ResetAll()
        {
            Load();
            foreach (var kv in Defaults) Set(kv.Key, kv.Value);
            PlayerPrefs.Save();
        }

        static void Set(GameAction a, KeyCode key)
        {
            current[a] = key;
            PlayerPrefs.SetInt(PrefsKey + a, (int)key);
            Rebound?.Invoke(a, key);
        }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            foreach (var kv in Defaults)
                current[kv.Key] = (KeyCode)PlayerPrefs.GetInt(PrefsKey + kv.Key, (int)kv.Value);
        }

        /// <summary>Nome curto para mostrar (ex.: "LMB", "Space", "1").</summary>
        public static string Label(KeyCode k) => k switch
        {
            KeyCode.None => "—",
            KeyCode.Mouse0 => "LMB",
            KeyCode.Mouse1 => "RMB",
            KeyCode.Mouse2 => "MMB",
            KeyCode.LeftShift => "L-Shift",
            KeyCode.RightShift => "R-Shift",
            KeyCode.LeftControl => "L-Ctrl",
            KeyCode.LeftAlt => "L-Alt",
            KeyCode.Escape => "Esc",
            >= KeyCode.Alpha0 and <= KeyCode.Alpha9 => ((int)k - (int)KeyCode.Alpha0).ToString(),
            _ => k.ToString()
        };
    }
}
