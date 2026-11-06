using System;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace MoonThemedGame.Computer
{
    [DisallowMultipleComponent,RequireComponent(typeof(MeshRenderer))]
    public sealed class ChargeMonitorDisplay : MonoBehaviour
    {
        MeshRenderer screen;
        Material instance,originalMaterial;
        Mesh originalMesh;
        ChargeDisplayAssets assets;
        public static bool IsScreen(string name)=>Normalize(name).StartsWith("CHARGEMONITOR",StringComparison.Ordinal);
        public static string Normalize(string name)=>name.Replace("_","").Replace(" ","").Replace(".","").ToUpperInvariant();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Install()
        {
            SceneManager.sceneLoaded-=Loaded;SceneManager.sceneLoaded+=Loaded;InstallScene();
        }
        static void Loaded(Scene scene,LoadSceneMode mode)=>InstallScene();
        public static void InstallScene()
        {
            foreach(var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if(IsScreen(renderer.name) && !renderer.GetComponent<ChargeMonitorDisplay>())renderer.gameObject.AddComponent<ChargeMonitorDisplay>();
        }
        void OnEnable()
        {
            assets=Resources.Load<ChargeDisplayAssets>("ChargeDisplayAssets");
            if(!assets || !assets.screenMaterial)return;
            screen=GetComponent<MeshRenderer>();originalMaterial=screen.sharedMaterial;
            var filter=GetComponent<MeshFilter>();
            if(filter && Normalize(name)!="CHARGEMONITOR" && assets.secondaryScreen){originalMesh=filter.sharedMesh;filter.sharedMesh=assets.secondaryScreen;}
            instance=new Material(assets.screenMaterial){name=name+" / localized CRT",hideFlags=HideFlags.DontSave};screen.sharedMaterial=instance;
            Loc.Changed+=Refresh;Refresh();
        }
        void Refresh()
        {
            if(!instance)return;
            instance.SetTexture("_BaseMap",Loc.Russian?assets.russian:assets.english);
            instance.SetFloat("_LetterCount",Loc.Russian?7:6);
        }
        void OnDisable()
        {
            Loc.Changed-=Refresh;
            if(screen && screen.sharedMaterial==instance)screen.sharedMaterial=originalMaterial;
            if(originalMesh)GetComponent<MeshFilter>().sharedMesh=originalMesh;
            if(instance)Destroy(instance);instance=null;
        }
    }
}
