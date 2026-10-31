using System.Collections;
using System.IO;
using AnimLab.Netcode.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimLab.Netcode.Unity
{
    /// <summary>Banco de teste do executável: dois processos reais, sem input do sistema.</summary>
    public sealed class NetBuildRun : MonoBehaviour
    {
        public NetMatch Match;
        public string PngPath;
        long finishedAt=-1;
        bool finishing;
        void Start()
        {
            Application.targetFrameRate=120;
            var camera=Camera.main;
            if(camera!=null) foreach(var component in camera.GetComponents<MonoBehaviour>())
                if(component.GetType().Name=="Metin2Camera") component.enabled=false;
        }
        void LateUpdate()
        {
            if(Match==null || !Match.Ready || Match.DirectPeer==null) return;
            var camera=Camera.main;
            if(camera!=null) {
                Vector3 a=Match.Actor(0).transform.position,b=Match.Actor(1).transform.position,mid=(a+b)*0.5f;
                Vector3 side=Vector3.Cross(Vector3.up,(b-a).normalized);
                if(side.z>0) side=-side;
                camera.transform.position=mid+side*7+Vector3.up*3;
                camera.transform.LookAt(mid+Vector3.up*0.8f); camera.fieldOfView=50;
            }
            if(Match.DirectPeer.Desync!=null || Match.DirectPeer.Session.Faulted) { Match.WriteReport(); Application.Quit(2); return; }
            if(Match.State.Tick>=Match.TickLimit && Match.DirectPeer.Session.ConfirmedTick>=Match.TickLimit &&
                Match.DirectPeer.ChecksumsVerified>=Match.TickLimit/30) {
                if(finishedAt<0) finishedAt=NetworkClock.Now;
                if(NetworkClock.Now-finishedAt>1500 && !finishing) { finishing=true; StartCoroutine(Finish()); }
            }
        }
        IEnumerator Finish()
        {
            // Um painel em textura precisa de um frame para desenhar o HUD.
            UIDocument document=FindFirstObjectByType<UIDocument>(); RenderTexture ui=null;
            if(PngPath!=null && document!=null) {
                ui=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);
                var settings=Instantiate(document.panelSettings); settings.targetTexture=ui; settings.clearColor=true;
                settings.colorClearValue=Color.clear; document.panelSettings=settings;
            }
            yield return null;
            if(PngPath!=null) Capture(PngPath,ui);
            Match.WriteReport(); Debug.Log("NETCODE BUILD TEST: "+Match.State.Tick+" ticks; "+Match.DirectPeer.ChecksumsVerified+" verified; desync=0");
            RenderTexture.active=null;
            Application.Quit(0);
        }
        void OnDestroy()
        {
            if(finishing || ReferenceEquals(Match,null)) return;
            bool success=Match.State.Tick>=Match.TickLimit && Match.DirectPeer!=null &&
                Match.DirectPeer.ChecksumsVerified>=Match.TickLimit/30 && Match.DirectPeer.Desync==null;
            Application.Quit(success?0:2);
        }
        public static void Capture(string path,RenderTexture ui)
        {
            var camera=Camera.main; if(camera==null) return;
            var hdr=new RenderTexture(1280,720,24,RenderTextureFormat.DefaultHDR);
            var target=new RenderTexture(1280,720,0); var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);
            camera.targetTexture=hdr; camera.Render(); Graphics.Blit(hdr,target); RenderTexture.active=target;
            tex.ReadPixels(new Rect(0,0,1280,720),0,0); tex.Apply();
            if(ui!=null) {
                var overlay=new Texture2D(1280,720,TextureFormat.RGBA32,false);
                RenderTexture.active=ui; overlay.ReadPixels(new Rect(0,0,1280,720),0,0); overlay.Apply();
                Color32[] a=tex.GetPixels32(),b=overlay.GetPixels32();
                for(int i=0;i<a.Length;i++) { int k=255-b[i].a; a[i].r=(byte)Mathf.Min(255,b[i].r+a[i].r*k/255); a[i].g=(byte)Mathf.Min(255,b[i].g+a[i].g*k/255); a[i].b=(byte)Mathf.Min(255,b[i].b+a[i].b*k/255); }
                tex.SetPixels32(a); tex.Apply(); Destroy(overlay);
            }
            RenderTexture.active=null; camera.targetTexture=null;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); File.WriteAllBytes(path,tex.EncodeToPNG());
            Destroy(tex); hdr.Release(); target.Release(); Destroy(hdr); Destroy(target);
        }
    }
}

