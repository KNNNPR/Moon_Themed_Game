using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MoonThemedGame.Computer;
using MoonThemedGame.Interactions;
using MoonThemedGame.Scavenger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;
namespace MoonThemedGame.Editor
{
    [InitializeOnLoad]
    public static class ChargeDisplaySetup
    {
        const string AssetPath="Assets/Resources/ChargeDisplayAssets.asset";
        static ChargeDisplaySetup()
        {
            if(Application.isBatchMode)return;
            EditorApplication.delayCall+=InstallScene;
            EditorSceneManager.sceneOpened+=(scene,mode)=>InstallScene();
            EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.EnteredEditMode)InstallScene();};
        }
        public static void InstallScene()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            var assets=AssetDatabase.LoadAssetAtPath<ChargeDisplayAssets>(AssetPath);if(!assets)return;
            foreach(var screen in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if(!ChargeMonitorDisplay.IsScreen(screen.name))continue;
                var filter=screen.GetComponent<MeshFilter>();bool second=ChargeMonitorDisplay.Normalize(screen.name)!="CHARGEMONITOR";
                bool changed=screen.sharedMaterial!=assets.screenMaterial || (second && filter && filter.sharedMesh!=assets.secondaryScreen);
                if(changed)
                {
                    Undo.RecordObject(screen,"Configure localized charge display");screen.sharedMaterial=assets.screenMaterial;
                    if(second && filter && assets.secondaryScreen){Undo.RecordObject(filter,"Map secondary charge screen");filter.sharedMesh=assets.secondaryScreen;PrefabUtility.RecordPrefabInstancePropertyModifications(filter);}
                    PrefabUtility.RecordPrefabInstancePropertyModifications(screen);EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
                }
            }
        }
        [MenuItem("Tools/Moon Game/Charge Monitor/Build Bilingual Displays")]
        public static async void Build()
        {
            try{await BuildAssets();InstallScene();}catch(Exception e){Debug.LogException(e);}
        }
        public static async Task BuildAssets()
        {
            const string texturePath="Assets/Computer/ChargeMonitorTextRU.png";
            if(!File.Exists(texturePath))await BakeRussian(texturePath);
            var second=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).FirstOrDefault(r=>ChargeMonitorDisplay.IsScreen(r.name) && ChargeMonitorDisplay.Normalize(r.name)!="CHARGEMONITOR");
            if(!second)throw new InvalidOperationException("Secondary charge monitor not found.");
            const string meshPath="Assets/Computer/ChargeMonitor2Screen.asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(!mesh){mesh=ChargeMonitorSetup.MakeScreenMesh(second.GetComponent<MeshFilter>().sharedMesh,second.transform,out _,out _,out _,out _);mesh.name="ChargeMonitor2Screen";AssetDatabase.CreateAsset(mesh,meshPath);}
            var assets=AssetDatabase.LoadAssetAtPath<ChargeDisplayAssets>(AssetPath);
            if(!assets){assets=ScriptableObject.CreateInstance<ChargeDisplayAssets>();AssetDatabase.CreateAsset(assets,AssetPath);}
            assets.screenMaterial=AssetDatabase.LoadAssetAtPath<Material>(ChargeMonitorSetup.MaterialPath);
            assets.english=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Computer/ChargeMonitorText.png");
            assets.russian=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);assets.secondaryScreen=mesh;
            EditorUtility.SetDirty(assets);AssetDatabase.SaveAssets();
        }
        static async Task BakeRussian(string path)
        {
            var settings=Resources.Load<SettingsMenuAssets>("SettingsMenuAssets");
            if(!settings || !settings.font || !settings.theme)throw new InvalidOperationException("PT Mono / UI theme missing.");
            var rt=new RenderTexture(512,128,0,RenderTextureFormat.ARGB32);rt.Create();
            var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.targetTexture=rt;panel.themeStyleSheet=settings.theme;panel.scaleMode=PanelScaleMode.ConstantPixelSize;panel.scale=1;panel.clearColor=true;panel.colorClearValue=Color.black;
            var host=new GameObject("Charge Russian text bake");var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;
            var root=doc.rootVisualElement;root.style.width=512;root.style.height=128;root.style.backgroundColor=Color.black;root.style.unityFontDefinition=FontDefinition.FromFont(settings.font);
            var row=new VisualElement();row.style.position=Position.Absolute;row.style.left=64;row.style.top=0;row.style.width=384;row.style.height=128;row.style.flexDirection=FlexDirection.Row;root.Add(row);
            foreach(char letter in "ЗАРЯДКА")
            {
                var label=new Label(letter.ToString());label.style.width=384f/7;label.style.height=128;label.style.marginLeft=label.style.marginRight=label.style.marginTop=label.style.marginBottom=0;label.style.paddingLeft=label.style.paddingRight=0;label.style.fontSize=72;label.style.color=Color.white;label.style.unityTextAlign=TextAnchor.MiddleCenter;row.Add(label);
            }
            await Task.Delay(700);
            var previous=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(512,128,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,512,128),0,0);image.Apply();
            if(image.GetPixels32().Count(c=>c.r>180)<800)throw new InvalidOperationException("Russian charge texture is blank.");
            File.WriteAllBytes(path,image.EncodeToPNG());RenderTexture.active=previous;Object.DestroyImmediate(image);Object.DestroyImmediate(host);Object.DestroyImmediate(panel);rt.Release();Object.DestroyImmediate(rt);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
        }
        public static async void BuildAndExit()
        {
            try{EditorSceneManager.OpenScene("Assets/Scenes/Operator.unity");await BuildAssets();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
    public sealed class ChargeDisplayAssetsImport : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported,string[] deleted,string[] moved,string[] oldPaths)
        {
            if(!Application.isBatchMode && Array.IndexOf(imported,"Assets/Resources/ChargeDisplayAssets.asset")>=0)EditorApplication.delayCall+=ChargeDisplaySetup.InstallScene;
        }
    }
}
