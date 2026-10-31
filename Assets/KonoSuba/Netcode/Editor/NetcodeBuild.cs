using System;
using System.IO;
using System.Linq;
using AnimLab.Netcode.Unity;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AnimLab.Netcode.Editor
{
    public static class NetcodeBuild
    {
        [MenuItem("AnimLab/Netcode/Build Windows test client")]
        public static void Run()
        {
            const string resource="Assets/KonoSuba/Netcode/Resources/NetcodeShaders.asset";
            Directory.CreateDirectory("Assets/KonoSuba/Netcode/Resources");
            AssetDatabase.Refresh();
            var bank=AssetDatabase.LoadAssetAtPath<ShaderReferences>(resource);
            if(bank==null) { bank=ScriptableObject.CreateInstance<ShaderReferences>(); AssetDatabase.CreateAsset(bank,resource); }
            bank.Shaders=AssetDatabase.FindAssets("t:Shader",new[] {"Assets/KonoSuba/Shaders"})
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Shader>).Where(s=>s!=null).ToArray();
            EditorUtility.SetDirty(bank); AssetDatabase.SaveAssetIfDirty(bank);
            string output=OnlineConnection.Arg("-netBuildPath","Builds/Netcode/AnimLab.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            BuildReport report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[] {"Assets/Scenes/KonoSubaVoid.unity"},
                locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development });
            Debug.Log("Netcode build: "+report.summary.result+"; "+report.summary.totalSize+" bytes");
            if(report.summary.result!=BuildResult.Succeeded) throw new InvalidOperationException("Netcode build failed");
        }
    }
}

