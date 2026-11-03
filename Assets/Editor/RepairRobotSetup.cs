using System;
using System.Linq;
using MoonThemedGame.Repair;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Editor
{
    public static class RepairRobotSetup
    {
        const string PointName = "Repair Robot Drop Point";

        [MenuItem("Tools/Moon Game/Repair/Create Robot Drop Point")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode first.");
            var scene = SceneManager.GetActiveScene();
            var existing = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<RepairRobotDropper>(true)).FirstOrDefault();
            if (existing) { Selection.activeGameObject = existing.gameObject; return; }
            var robot = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/SCAV_BOT_REPAIR.prefab");
            if (!robot) throw new InvalidOperationException("SCAV_BOT_REPAIR prefab was not found.");
            var paths = new[] { "Wheel", "Headlights", "Battery", "Back_Body_Robot", "Window_Robot" };
            var labels = new[] { "Колёса", "Фары (комплект)", "Аккумуляторы", "Задняя панель корпуса", "Стекло" };
            var definitions = new RepairPartDefinition[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                var part = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Parts/{paths[i]}.prefab");
                if (!part) throw new InvalidOperationException("Part prefab missing: " + paths[i]);
                definitions[i] = new RepairPartDefinition { prefab = part, displayName = labels[i] };
            }
            var host = new GameObject(PointName);
            SceneManager.MoveGameObjectToScene(host, scene);
            Undo.RegisterCreatedObjectUndo(host, "Create robot drop point");
            var dropper = Undo.AddComponent<RepairRobotDropper>(host);
            dropper.robotPrefab = robot;
            dropper.parts = definitions;
            dropper.landingPoint = host.transform;
            var stand = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(t => t.name == "Mechanical_stand");
            if (stand)
            {
                var surface = stand.GetComponent<Collider>();
                var visual = stand.GetComponent<Renderer>();
                Bounds bounds = surface ? surface.bounds : visual ? visual.bounds : new Bounds(stand.position, Vector3.one);
                host.transform.position = new Vector3(bounds.center.x, bounds.max.y + 0.02f, bounds.center.z);
            }
            else
            {
                host.transform.position = robot.transform.position;
                Debug.LogWarning("Mechanical_stand was not found. Move the drop point to the desired landing surface.");
            }
            Selection.activeGameObject = host;
            EditorUtility.SetDirty(dropper);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Repair drop point created. Move it if needed, save the scene, then press F in Play Mode.", host);
        }
    }
}
