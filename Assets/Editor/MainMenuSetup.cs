using System;
using System.Linq;
using MoonThemedGame.Computer;
using MoonThemedGame.Weather;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;

namespace MoonThemedGame.Editor
{
    [InitializeOnLoad]
    public static class MainMenuSetup
    {
        const string AssetPath="Assets/Resources/MainMenuAssets.asset";
        static MainMenuSetup()
        {
            if(Application.isBatchMode)return;
            EditorApplication.delayCall+=Install;
            EditorSceneManager.sceneOpened+=(scene,mode)=>Install();
        }
        [MenuItem("Tools/Moon Game/Main Menu/Configure MENU Scene")]
        public static void Install()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            var assets=AssetDatabase.LoadAssetAtPath<MainMenuAssets>(AssetPath);
            if(!assets)return;
            ConfigureBuildScenes();
            if(!MainMenuController.IsMenuScene)return;
            var monitor=Object.FindFirstObjectByType<WeatherMonitorDisplay>();if(!monitor)return;
            var menu=Object.FindFirstObjectByType<MainMenuController>();
            bool created=!menu;
            if(!menu)
            {
                var host=new GameObject("PALE HORSE / Main Menu");Undo.RegisterCreatedObjectUndo(host,"Add main menu");menu=Undo.AddComponent<MainMenuController>(host);
            }
            Undo.RecordObject(menu,"Configure main menu");menu.assets=assets;menu.monitor=monitor;
            if(!menu.viewCamera)menu.viewCamera=Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).OrderBy(c=>Vector3.Distance(c.transform.position,monitor.transform.position)).FirstOrDefault();
            var center=monitor.transform.TransformPoint(monitor.screenCenter);
            if(!menu.powerButton)menu.powerButton=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Where(r=>Vector3.Distance(r.bounds.center,center)<1.4f && r.sharedMaterials.Any(m=>m && m.name.Equals("RED",StringComparison.OrdinalIgnoreCase)))
                .OrderBy(r=>Vector3.Distance(r.bounds.center,center)).Select(r=>r.transform).FirstOrDefault();
            menu.floatingMonitorParts=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Where(r=>(r.name=="Cube" || r.name=="Cylinder.001") && Vector3.Distance(r.bounds.center,center)<1.5f).ToArray();
            if(assets.previewMaterial && monitor.screenRenderer.sharedMaterial!=assets.previewMaterial)
            {
                Undo.RecordObject(monitor.screenRenderer,"Preview menu on smaller monitor");monitor.screenRenderer.sharedMaterial=assets.previewMaterial;
                PrefabUtility.RecordPrefabInstancePropertyModifications(monitor.screenRenderer);
            }
            if(created && menu.viewCamera)
            {
                var normal=monitor.transform.TransformDirection(monitor.screenNormal).normalized;
                var up=monitor.transform.TransformDirection(monitor.screenUp).normalized;var right=Vector3.Cross(normal,up).normalized;
                Undo.RecordObjects(new Object[]{menu.viewCamera,menu.viewCamera.transform},"Frame menu monitor");
                menu.viewCamera.transform.SetPositionAndRotation(center+normal*menu.overviewDistance+right*(menu.overviewDistance*Mathf.Tan(menu.overviewFov*.5f*Mathf.Deg2Rad)*menu.viewCamera.aspect*.47f),Quaternion.LookRotation(-normal,up));
                menu.viewCamera.fieldOfView=menu.overviewFov;
                foreach(var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))if(camera!=menu.viewCamera){Undo.RecordObject(camera,"Use menu camera");camera.enabled=false;}
            }
            EditorSceneManager.MarkSceneDirty(menu.gameObject.scene);
        }
        public static void ConfigureBuildScenes()
        {
            var scenes=EditorBuildSettings.scenes;
            var updated=new[]{new EditorBuildSettingsScene("Assets/Scenes/MENU.unity",true),new EditorBuildSettingsScene("Assets/Scenes/Operator.unity",true)}
                .Concat(scenes.Where(s=>s.path!="Assets/Scenes/MENU.unity" && s.path!="Assets/Scenes/Operator.unity")).ToArray();
            if(!scenes.Select(s=>s.path+":"+s.enabled).SequenceEqual(updated.Select(s=>s.path+":"+s.enabled)))EditorBuildSettings.scenes=updated;
        }
        public static void BuildAndExit()
        {
            try
            {
                var assets=AssetDatabase.LoadAssetAtPath<MainMenuAssets>(AssetPath);
                if(!assets){assets=ScriptableObject.CreateInstance<MainMenuAssets>();AssetDatabase.CreateAsset(assets,AssetPath);}
                assets.screenStyle=AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Resources/StationMainMenu.uss");
                assets.applePrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Apple.prefab");
                const string letterPath="Assets/Computer/MenuLetterMetal.mat";
                var letter=AssetDatabase.LoadAssetAtPath<Material>(letterPath);
                if(!letter){letter=new Material(Shader.Find("Moon Game/Bevelled Menu Letter")){name="MenuLetterMetal"};AssetDatabase.CreateAsset(letter,letterPath);}
                assets.titleMaterial=letter;
                EditorUtility.SetDirty(assets);AssetDatabase.SaveAssets();ConfigureBuildScenes();
                EditorSceneManager.OpenScene("Assets/Scenes/MENU.unity");Install();
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
    public sealed class MainMenuAssetsImport : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported,string[] deleted,string[] moved,string[] oldPaths)
        {
            if(!Application.isBatchMode && (Array.IndexOf(imported,"Assets/Resources/MainMenuAssets.asset")>=0 || Array.IndexOf(imported,"Assets/Computer/MainMenuPreview.mat")>=0))EditorApplication.delayCall+=MainMenuSetup.Install;
        }
    }
}
