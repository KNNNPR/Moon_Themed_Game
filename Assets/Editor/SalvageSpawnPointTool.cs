using MoonThemedGame.Scavenger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MoonThemedGame.Editor
{
    // Scene view mode: each left click on a surface drops a salvage spawn point there.
    public static class SalvageSpawnPointTool
    {
        const string MenuPath = "Tools/Salvage Spawn Points/Place With Mouse";
        const string ParentName = "Salvage Spawn Points";

        static bool active;

        [MenuItem(MenuPath)]
        static void Toggle() => SetActive(!active);

        [MenuItem(MenuPath, true)]
        static bool ToggleCheck()
        {
            Menu.SetChecked(MenuPath, active);
            return !Application.isPlaying;
        }

        static void SetActive(bool value)
        {
            active = value;
            SceneView.duringSceneGui -= OnSceneGui;
            if (active)
            {
                SceneView.duringSceneGui += OnSceneGui;
                if (SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.Focus();
            }
            SceneView.RepaintAll();
        }

        static void OnSceneGui(SceneView view)
        {
            if (Application.isPlaying) { SetActive(false); return; }
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                SetActive(false);
                e.Use();
                return;
            }

            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(control);
            if (e.type == EventType.MouseMove) view.Repaint();

            bool hit = HandleUtility.PlaceObject(e.mousePosition, out var position, out var normal);
            if (hit && e.type == EventType.Repaint)
            {
                Handles.color = new Color(1, 0.62f, 0.2f);
                Handles.DrawWireDisc(position, normal, 1);
                Handles.DrawLine(position, position + Vector3.up * 3);
            }
            if (hit && e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                Place(position, normal);
                e.Use();
            }

            Handles.BeginGUI();
            GUI.Label(new Rect(10, 10, 420, 22), "Spawn points: click to place, Esc to finish", EditorStyles.whiteLargeLabel);
            Handles.EndGUI();
        }

        static void Place(Vector3 position, Vector3 normal)
        {
            var parent = GameObject.Find(ParentName);
            if (!parent)
            {
                parent = new GameObject(ParentName);
                Undo.RegisterCreatedObjectUndo(parent, "Place Salvage Spawn Point");
            }
            var point = new GameObject("Salvage Spawn Point " + (parent.transform.childCount + 1));
            point.transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, normal));
            point.transform.SetParent(parent.transform, true);
            point.AddComponent<SalvageSpawnPoint>();
            Undo.RegisterCreatedObjectUndo(point, "Place Salvage Spawn Point");
            EditorSceneManager.MarkSceneDirty(point.scene);
        }
    }
}
