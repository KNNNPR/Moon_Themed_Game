using MoonThemedGame.Repair;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MoonThemedGame.Editor
{
    [InitializeOnLoad]
    public static class ScrapTubeSetup
    {
        static ScrapTubeSetup()
        {
            if(Application.isBatchMode)return;
            EditorApplication.delayCall+=Install;
            EditorSceneManager.sceneOpened+=(scene,mode)=>Install();
            EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.EnteredEditMode)Install();};
        }
        [MenuItem("Tools/Moon Game/Repair/Set Up Scrap Tube")]
        public static void Install()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            foreach(var dropper in Object.FindObjectsByType<RepairRobotDropper>(FindObjectsSortMode.None))
            {
                ScrapTubeDelivery prior=null;
                foreach(var root in dropper.gameObject.scene.GetRootGameObjects())
                    if(root.GetComponentInChildren<ScrapTubeDelivery>(true) is ScrapTubeDelivery found){prior=found;break;}
                if(prior && prior.HasRoute)continue;
                var tube=prior;
                if(!tube)
                    foreach(var root in dropper.gameObject.scene.GetRootGameObjects())
                        foreach(var t in root.GetComponentsInChildren<Transform>(true))
                            if(t.name.Equals("Tube_For_Scrap",System.StringComparison.OrdinalIgnoreCase))
                            {tube=Undo.AddComponent<ScrapTubeDelivery>(t.gameObject);break;}
                if(!tube)continue;
                Undo.RecordObject(tube,"Configure scrap pipe delivery");
                tube.ConfigureDefaultRoute();
                Undo.RegisterCreatedObjectUndo(tube.waypoints[0].parent.gameObject,"Create pipe route");
                EditorUtility.SetDirty(tube);
                EditorSceneManager.MarkSceneDirty(dropper.gameObject.scene);
            }
        }
    }
}
