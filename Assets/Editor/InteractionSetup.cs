using System;
using System.Collections.Generic;
using System.Linq;
using MoonThemedGame.Computer;
using MoonThemedGame.Interactions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Editor
{
    public static class InteractionSetup
    {
        const string Folder = "Assets/Interactions";

        [MenuItem("Tools/Moon Game/Interactions/Setup Current Scene")]
        public static void SetupCurrentScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before setting up interactions.");
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var player = roots.SelectMany(r => r.GetComponentsInChildren<FirstPersonController>(true)).FirstOrDefault();
            if (!player) throw new InvalidOperationException("No FirstPersonController in the current scene.");
            var camera = player.GetComponentInChildren<Camera>(true);
            if (!camera) throw new InvalidOperationException("No camera under the player.");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/InteractionOutline.shader");
            if (!shader || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Interaction outline shader is missing or has errors.");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "Interactions");
            var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/InteractionOutline.mat");
            if (!material)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, Folder + "/InteractionOutline.mat");
            }
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Setup interactions");
            var interactor = GetOrAdd<PlayerInteractor>(player.gameObject);
            Undo.RecordObject(interactor, "Configure interactor");
            interactor.viewCamera = camera;
            Record(interactor);

            foreach (var terminal in roots.SelectMany(r => r.GetComponentsInChildren<PcMonitorTerminal>(true)))
            {
                var adapter = GetOrAdd<TerminalInteractable>(terminal.gameObject);
                Undo.RecordObject(adapter, "Configure terminal interaction");
                adapter.interactionDistance = terminal.interactionDistance;
                Record(adapter);
            }

            var doors = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name.Equals("Door", StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var door in doors) ConfigureDoor(door, material);
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"Interactions configured: {doors.Length} door(s). Save the scene with Ctrl+S.");
        }

        [MenuItem("Tools/Moon Game/Interactions/Make Selected Object A Door")]
        public static void SetupSelectedDoor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !Selection.activeTransform) return;
            var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/InteractionOutline.mat");
            if (!material) throw new InvalidOperationException("Run Setup Current Scene first.");
            ConfigureDoor(Selection.activeTransform, material);
            EditorSceneManager.MarkSceneDirty(Selection.activeGameObject.scene);
            AssetDatabase.SaveAssets();
        }

        static void ConfigureDoor(Transform door, Material material)
        {
            var renderers = door.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0) return;
            bool newDoor = !door.GetComponent<DoorInteractable>();
            var interaction = GetOrAdd<DoorInteractable>(door.gameObject);
            var outline = GetOrAdd<InteractionOutline>(door.gameObject);
            Undo.RecordObjects(new UnityEngine.Object[] { interaction, outline }, "Configure door");
            outline.targets = renderers;
            outline.outlineMaterial = material;

            interaction.outline = outline;
            if (newDoor)
            {
                interaction.movingPart = door;
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                Vector3 worldOffset = Vector3.up * (bounds.size.y + 0.15f);
                interaction.openOffset = door.parent ? door.parent.InverseTransformVector(worldOffset) : worldOffset;
            }
            for (int i = 0; i < renderers.Length; i++)
            {
                var filter = renderers[i].GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh) continue;
                var collider = filter.GetComponent<Collider>();
                if (!collider)
                {
                    var meshCollider = Undo.AddComponent<MeshCollider>(filter.gameObject);
                    meshCollider.sharedMesh = filter.sharedMesh;
                }
                GameObjectUtility.SetStaticEditorFlags(filter.gameObject, 0);

            }
            Record(interaction);
            Record(outline);
        }

        static T GetOrAdd<T>(GameObject obj) where T : Component =>
            obj.GetComponent<T>() ? obj.GetComponent<T>() : Undo.AddComponent<T>(obj);

        static void Record(Component component)
        {
            EditorUtility.SetDirty(component);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
    }
}
