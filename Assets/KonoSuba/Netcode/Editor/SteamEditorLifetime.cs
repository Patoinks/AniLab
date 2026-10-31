using AnimLab.Netcode.Unity;
using UnityEditor;

namespace AnimLab.Netcode.Editor
{
    [InitializeOnLoad]
    static class SteamEditorLifetime
    {
        static SteamEditorLifetime()
        {
            EditorApplication.playModeStateChanged+=state=> { if(state==PlayModeStateChange.ExitingPlayMode) SteamOnline.Shutdown(); };
            AssemblyReloadEvents.beforeAssemblyReload+=SteamOnline.Shutdown;
            EditorApplication.quitting+=SteamOnline.Shutdown;
        }
    }
}
