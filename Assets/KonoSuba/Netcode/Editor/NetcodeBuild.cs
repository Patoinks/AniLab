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
            string folder=Path.GetDirectoryName(Path.GetFullPath(output));
            File.Copy("steam_appid.txt",Path.Combine(folder,"steam_appid.txt"),true);
            var steam=UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.rlabrecque.steamworks.net/package.json");
            if(steam==null) throw new InvalidOperationException("Steamworks.NET package missing");
            File.Copy(Path.Combine(steam.resolvedPath,"Plugins/steam_api64.dll"),Path.Combine(folder,"steam_api64.dll"),true);
            string data=Path.GetFileNameWithoutExtension(output)+"_Data";
            if(File.ReadAllText(Path.Combine(folder,"steam_appid.txt")).Trim()!="480" || !File.Exists(Path.Combine(folder,data+"/Managed/com.rlabrecque.steamworks.net.dll")))
                throw new InvalidOperationException("Steam build files missing or invalid");
            File.Copy(Path.Combine(folder,data+"/Managed/com.rlabrecque.steamworks.net.dll"),Path.Combine(folder,"com.rlabrecque.steamworks.net.dll"),true);
            Debug.Log("Steam build files verified: App 480, steam_api64.dll, managed Steamworks.NET");
        }
    }
}

