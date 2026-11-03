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
            var location = ScavengerStartLocation.BuildFor(robot.transform);
            Undo.RegisterCreatedObjectUndo(location, "Create Scavenger Start Location");
            Selection.activeGameObject = location;
            EditorSceneManager.MarkSceneDirty(location.scene);
        }
    }
}
