using System;
using System.Collections.Generic;
using System.Linq;
using MoonThemedGame.Repair;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MoonThemedGame.Editor
{
    public static class RepairVariantsAndZombieSetup
    {
        const string Folder = "Assets/Prefabs/RepairRobots";
        [MenuItem("Tools/Moon Game/Repair/Rebuild FinderBot, SkateBot and Zombie")]
        public static void Build()
        {
            Directory(Folder);
            var original = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/SCAV_BOT_REPAIR.prefab");
            var reference = Object.Instantiate(original);
            float size = Bounds(reference).size.z;
            Object.DestroyImmediate(reference);
            var finder = BuildRobot("FinderBot", size);
            var skate = BuildRobot("SkateBot", size);
            var catalog = Asset<RepairRobotCatalog>("Assets/Resources/RepairRobotCatalog.asset");
            catalog.robots = new[] { finder, skate }; EditorUtility.SetDirty(catalog);
            BuildZombie();
            AssetDatabase.SaveAssets();
        }

        static RepairRobotDefinition BuildRobot(string name, float referenceLength)
        {
            string folder = Folder + "/" + name;
            Directory(folder); Directory(folder + "/Parts"); Directory(folder + "/Meshes"); Directory(folder + "/Materials");
            var source = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/" + name + ".fbx"));
            var root = new GameObject(name);
            try
            {
                var whole = Bounds(source);
                float unit = referenceLength / Mathf.Max(whole.size.z, .01f);
                var offset = new Vector3(whole.center.x, whole.min.y, whole.center.z);
                var definitions = new Dictionary<string, GameObject>();
                var materials = new Dictionary<Material, Material>();
                foreach (var filter in source.GetComponentsInChildren<MeshFilter>())
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (!renderer || !filter.sharedMesh) continue;
                    string label = Label(filter.name);
                    bool removable = label != "Main_Body";
                    var center = (renderer.bounds.center - offset) * unit;
                    var mesh = Bake(filter, unit, offset, center);
                    string meshPath = folder + "/Meshes/" + filter.name + ".asset";
                    var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if (existing) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; }
                    else AssetDatabase.CreateAsset(mesh, meshPath);
                    var node = new GameObject(label + (filter.name.Contains(".") ? "." + filter.name.Split('.')[1] : ""));
                    node.transform.SetParent(root.transform, false); node.transform.localPosition = center;
                    node.AddComponent<MeshFilter>().sharedMesh = mesh;
                    node.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials.Select(Material).ToArray();
                    node.AddComponent<BoxCollider>().center = mesh.bounds.center;
                    node.GetComponent<BoxCollider>().size = mesh.bounds.size;
                    if (!removable || definitions.ContainsKey(label)) continue;
                    var part = Object.Instantiate(node); part.name = label; part.transform.position = Vector3.zero;
                    part.AddComponent<RepairPartVariant>().robotName = name;
                    definitions[label] = PrefabUtility.SaveAsPrefabAsset(part, folder + "/Parts/" + label + ".prefab");
                    Object.DestroyImmediate(part);
                }
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/" + name + ".prefab");
                var definition = Asset<RepairRobotDefinition>(folder + "/" + name + ".asset");
                definition.displayName = name; definition.prefab = prefab; definition.deliveryScale = .5f;
                definition.parts = definitions.Select(pair => new RepairPartDefinition { prefab = pair.Value, displayName = pair.Key }).ToArray();
                EditorUtility.SetDirty(definition);
                return definition;

                Material Material(Material imported)
                {
                    if (!imported) return null;
                    if (materials.TryGetValue(imported, out var saved)) return saved;
                    string path = folder + "/Materials/" + imported.name + ".mat";
                    saved = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!saved)
                    {
                        saved = new Material(Shader.Find("Old Tech/Retro Cel Lit")) { name = name + " / " + imported.name };
                        var texture = imported.HasProperty("_BaseMap") ? imported.GetTexture("_BaseMap") : imported.HasProperty("_MainTex") ? imported.GetTexture("_MainTex") : null;
                        var color = imported.HasProperty("_BaseColor") ? imported.GetColor("_BaseColor") : imported.color;
                        saved.SetTexture("_BaseMap", texture); saved.SetColor("_BaseColor", color);
                        AssetDatabase.CreateAsset(saved, path);
                    }
                    materials[imported] = saved; return saved;
                }
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(root); }
        }

        static string Label(string name)
        {
            name = name.Split('.')[0].ToLowerInvariant();
            if (name.Contains("wheel")) return "Wheel";
            if (name.Contains("battery")) return "Battery";
            if (name == "light" || name.Contains("headlight")) return "Headlights";
            if (name.Contains("window")) return "Window_Robot";
            if (name.Contains("back")) return "Back_Body_Robot";
            return "Main_Body";
        }

        static Mesh Bake(MeshFilter filter, float unit, Vector3 offset, Vector3 center)
        {
            using var data = MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh);
            var input = data[0]; int count = input.vertexCount;
            using var vertices = new NativeArray<Vector3>(count, Allocator.Temp); input.GetVertices(vertices);
            using var uv = new NativeArray<Vector2>(count, Allocator.Temp); input.GetUVs(0, uv);
            var points = new Vector3[count];
            for (int i = 0; i < count; i++) points[i] = (filter.transform.TransformPoint(vertices[i]) - offset) * unit - center;
            var mesh = new Mesh { name = filter.name, indexFormat = filter.sharedMesh.indexFormat, vertices = points, uv = uv.ToArray(), subMeshCount = input.subMeshCount };
            for (int sub = 0; sub < input.subMeshCount; sub++)
            {
                using var indices = new NativeArray<int>(input.GetSubMesh(sub).indexCount, Allocator.Temp); input.GetIndices(indices, sub);
                mesh.SetIndices(indices.ToArray(), input.GetSubMesh(sub).topology, sub);
            }
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        static void BuildZombie()
        {
            var assets = Resources.Load<CosmonautBodyAssets>("CosmonautBodyAssets");
            var root = new GameObject("Zombie");
            try
            {
                var actor = root.AddComponent<ZombieActor>();
                var controller = root.GetComponent<CharacterController>();
                controller.height = 1.8f; controller.radius = .27f; controller.center = Vector3.up * .9f; controller.stepOffset = .25f; controller.skinWidth = .025f;
                var model = Object.Instantiate(assets.model, root.transform); model.name = "Cosmonaut / zombie";
                float scale = 1.65f / assets.sourceEyeHeight;
                model.transform.localScale = Vector3.one * scale;
                model.transform.localPosition = Vector3.down * (assets.sourceFeetY * scale);
                actor.model = model.transform;
                foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>()) { skin.sharedMaterial = assets.material; skin.updateWhenOffscreen = true; }
                foreach (var animator in model.GetComponentsInChildren<Animator>()) Object.DestroyImmediate(animator);
                foreach (var collider in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
                var bones = model.GetComponentsInChildren<Transform>().ToDictionary(t => t.name, t => t);
                actor.spine = bones["Bone.001"]; actor.head = bones["Bone.004"];
                actor.leftUpperArm = bones["Bone.006.L"]; actor.rightUpperArm = bones["Bone.006.R"];
                actor.leftForearm = bones["Bone.007.L"]; actor.rightForearm = bones["Bone.007.R"];
                actor.leftThigh = bones["Bone.009.L"]; actor.rightThigh = bones["Bone.009.R"];
                actor.leftShin = bones["Bone.010.L"]; actor.rightShin = bones["Bone.010.R"];
                actor.impactMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Weapons/Particles.mat");
                Zone("Head", actor.head, bones["Bone.004_end"], ZombieBodyPart.Head, 3, .19f);
                Zone("Torso", bones["Bone"], bones["Bone.003"], ZombieBodyPart.Torso, 1, .27f);
                foreach (var side in new[] { "L", "R" })
                {
                    Zone("Upper arm " + side, bones["Bone.006." + side], bones["Bone.007." + side], ZombieBodyPart.Arm, .55f, .105f);
                    Zone("Forearm " + side, bones["Bone.007." + side], bones["Bone.008." + side + "_end"], ZombieBodyPart.Arm, .55f, .10f);
                    Zone("Thigh " + side, bones["Bone.009." + side], bones["Bone.010." + side], ZombieBodyPart.Leg, .7f, .135f);
                    Zone("Shin " + side, bones["Bone.010." + side], bones["Bone.011." + side + "_end"], ZombieBodyPart.Leg, .7f, .13f);
                }
                // Save the recognizable outstretched pose in the prefab as well as animating it at runtime.
                foreach (var arm in new[] { actor.leftUpperArm, actor.rightUpperArm })
                    arm.rotation = Quaternion.FromToRotation(arm.GetChild(0).position - arm.position, root.transform.forward) * arm.rotation;
                actor.ragdoll = ZombieRagdoll.Configure(actor);
                PrefabUtility.SaveAsPrefabAsset(root, "Assets/Resources/Zombie.prefab");

                void Zone(string name, Transform start, Transform end, ZombieBodyPart part, float multiplier, float radius)
                {
                    var node = new GameObject(name + " hit zone");
                    node.transform.SetPositionAndRotation((start.position + end.position) * .5f, Quaternion.FromToRotation(Vector3.up, end.position - start.position));
                    node.transform.SetParent(start, true);
                    // Counter the imported 129x skeleton scale so these values stay in metres.
                    var s = node.transform.lossyScale; node.transform.localScale = new Vector3(node.transform.localScale.x / s.x, node.transform.localScale.y / s.y, node.transform.localScale.z / s.z);
                    var collider = node.AddComponent<CapsuleCollider>(); collider.radius = radius; collider.height = Vector3.Distance(start.position, end.position) + radius * 1.2f;
                    var hit = node.AddComponent<ZombieHitZone>(); hit.zombie = actor; hit.bodyPart = part; hit.damageMultiplier = multiplier;
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Bounds Bounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds); return bounds;
        }
        static void Directory(string folder)
        { if (!AssetDatabase.IsValidFolder(folder)) { int slash = folder.LastIndexOf('/'); Directory(folder.Substring(0, slash)); AssetDatabase.CreateFolder(folder.Substring(0, slash), folder.Substring(slash + 1)); } }
        static T Asset<T>(string path) where T : ScriptableObject
        { var asset = AssetDatabase.LoadAssetAtPath<T>(path); if (!asset) { asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); } return asset; }
        public static void BuildAndExit()
        { try { Build(); EditorApplication.Exit(0); } catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); } }

        [MenuItem("Tools/Moon Game/Repair/Update Zombie Ragdoll")]
        public static void UpdateZombieRagdoll()
        {
            const string path = "Assets/Resources/Zombie.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var actor = root.GetComponent<ZombieActor>();
                actor.ragdoll = ZombieRagdoll.Configure(actor);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        public static void UpdateRagdollAndExit()
        { try { UpdateZombieRagdoll(); EditorApplication.Exit(0); } catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); } }
    }
}
