using MoonThemedGame.Scavenger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MoonThemedGame.Editor
{
    public static class ScavengerStartLocationMenu
    {
        // Bakes the placeholder start area into the open scene so it can be moved, edited or replaced.
        [MenuItem("Tools/Moon Game/Scavenger/Create Start Location")]
        public static void Create()
        {
            var existing = ScavengerStartLocation.Find();
            if (existing)
            {
                Selection.activeTransform = existing;
                Debug.Log($"[Scavenger] '{ScavengerStartLocation.ObjectName}' already exists in the scene.", existing);
                return;
            }
            var robot = GameObject.Find(ScavengerLink.RobotName);
            if (!robot)
            {
                Debug.LogError($"[Scavenger] '{ScavengerLink.RobotName}' was not found in the open scene.");
                return;
            }
            Renderer chargeBuilding = null;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.name == "Cube.026") chargeBuilding = r;
            var location = ScavengerStartLocation.BuildFor(robot.transform, chargeBuilding);
            Undo.RegisterCreatedObjectUndo(location, "Create Scavenger Start Location");
            Selection.activeGameObject = location;
            EditorSceneManager.MarkSceneDirty(location.scene);
        }
    }
}
