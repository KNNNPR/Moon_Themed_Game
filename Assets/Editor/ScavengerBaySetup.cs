using MoonThemedGame.Scavenger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MoonThemedGame.Editor
{
    [InitializeOnLoad]
    public static class ScavengerBaySetup
    {
        static ScavengerBaySetup()
        {
            if(Application.isBatchMode)return;
            EditorApplication.delayCall+=Install;
            EditorSceneManager.sceneOpened+=(scene,mode)=>Install();
            EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.EnteredEditMode)Install();};
        }
        public static void Install()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            foreach(var delivery in Object.FindObjectsByType<MoonThemedGame.Repair.RepairRobotDropper>(FindObjectsSortMode.None))
            {
                bool existed=false;
                foreach(var root in delivery.gameObject.scene.GetRootGameObjects())
                    if(root.GetComponentInChildren<RepairBayFogVolume>(true)){existed=true;break;}
                var volume=RepairBayFogVolume.EnsureFor(delivery);
                if(!volume)continue;
                if(!existed)
                {
                    Undo.RegisterCreatedObjectUndo(volume.gameObject,"Create editable repair fog");
                    EditorSceneManager.MarkSceneDirty(delivery.gameObject.scene);
                }
                volume.Refresh();
            }
            foreach(var old in Object.FindObjectsByType<ScavengerBayBlackout>(FindObjectsSortMode.None))
                Undo.DestroyObjectImmediate(old);
        }
    }
}
