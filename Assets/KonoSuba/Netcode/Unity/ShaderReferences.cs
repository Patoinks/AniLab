using UnityEngine;

namespace AnimLab.Netcode.Unity
{
    // As referências conservam no build os shaders criados pelos efeitos em runtime.
    public sealed class ShaderReferences : ScriptableObject
    {
        public Shader[] Shaders;
        static ShaderReferences loaded;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void KeepShaders() { loaded=Resources.Load<ShaderReferences>("NetcodeShaders"); }
    }
}

