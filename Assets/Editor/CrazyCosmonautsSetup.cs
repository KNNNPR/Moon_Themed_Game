using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MoonThemedGame.Computer;
using MoonThemedGame.Repair;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class CrazyCosmonautsSetup
{
    const string Folder = "Assets/Computer/CrazyCosmonauts";
    [MenuItem("Tools/Moon Game/Crazy Cosmonauts/Build Arena and Application")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder + "/Materials");
        AssetDatabase.Refresh();
        var content = AssetDatabase.LoadAssetAtPath<CrazyCosmonautsContent>("Assets/Resources/CrazyCosmonautsContent.asset");
        if (!content) { content = ScriptableObject.CreateInstance<CrazyCosmonautsContent>(); AssetDatabase.CreateAsset(content, "Assets/Resources/CrazyCosmonautsContent.asset"); }
        content.desktopIcon = Icon("Assets/Icons/zombie-svgrepo-com (1).svg", Folder + "/zombie.svg");
        content.healthIcon = Icon("Assets/Icons/health-svgrepo-com.svg", Folder + "/health.svg");
        content.zombiePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Zombie.prefab");
        content.pistolPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/PlayerPistol.prefab");
        var suit = Resources.Load<CosmonautBodyAssets>("CosmonautBodyAssets");
        content.zombieMaterial = Material(suit.material, "ZombieSuit");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/zombie shooter.fbx");
        if (!source) throw new InvalidOperationException("zombie shooter.fbx was not found.");
        var root = new GameObject("zombie shooter / Crazy Cosmonauts");
        try
        {
            var layout = root.AddComponent<CrazyCosmonautsArena>();
            var geometry = Object.Instantiate(source, root.transform); geometry.name = "zombie shooter";
            var floor = geometry.GetComponentsInChildren<MeshRenderer>().First(r => r.name == "Cube.081");
            var floorBounds = floor.bounds;
            float unit = 42 / floorBounds.size.x;
            var origin = new Vector3(floorBounds.center.x, floorBounds.max.y, floorBounds.center.z);
            geometry.transform.localScale = Vector3.one * unit; geometry.transform.localPosition = -origin * unit;
            foreach (var filter in geometry.GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.sharedMesh) continue;
                filter.gameObject.layer = 30;
                filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer) renderer.sharedMaterials = renderer.sharedMaterials.Select(m => Material(m, m ? m.name : "Concrete")).ToArray();
            }
            Physics.SyncTransforms();
            layout.playableBounds = new Bounds(new Vector3(0, 8, 0), new Vector3(44, 24, 46));
            var settings = NavMesh.GetSettingsByIndex(0); settings.agentRadius = .32f; settings.agentHeight = 1.8f; settings.agentClimb = .3f; settings.agentSlope = 45;
            settings.overrideVoxelSize = true; settings.voxelSize = .1f;
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(root.transform, 1 << 30, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, layout.playableBounds, Vector3.zero, Quaternion.identity);
            if (!data) throw new InvalidOperationException("Could not bake navigation for zombie shooter.");
            data.name = "Crazy Cosmonauts / navigation";
            string navPath = Folder + "/Navigation.asset";
            var previous = AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath);
            if (previous) { EditorUtility.CopySerialized(data, previous); Object.DestroyImmediate(data); data = previous; }
            else AssetDatabase.CreateAsset(data, navPath);
            layout.navigation = data;
            var nav = NavMesh.AddNavMeshData(data);
            try
            {
                var points = new List<Vector3>();
                for (float x = -17; x <= 17; x += 5.5f)
                    for (float z = -18; z <= 18; z += 5.5f)
                        if (NavMesh.SamplePosition(new Vector3(x, .1f, z), out var point, 1.2f, NavMesh.AllAreas) && point.position.y < .45f && point.position.y > -.1f &&
                            !Physics.CheckCapsule(point.position + Vector3.up * .4f, point.position + Vector3.up * 1.5f, .32f, 1 << 30, QueryTriggerInteraction.Ignore))
                            points.Add(point.position + Vector3.up * .06f);
                if (points.Count < 4) throw new InvalidOperationException("Not enough clear floor spawn positions in zombie shooter: " + points.Count);
                var start = points.OrderBy(p => Vector3.Distance(p, new Vector3(-14, 0, -13))).First();
                layout.playerSpawn = Point("Player start", start);
                layout.playerSpawn.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(-start, Vector3.up).normalized);
                var connected = new List<Transform>(); var path = new NavMeshPath();
                foreach (var point in points)
                    if (Vector3.Distance(point, start) > 8 && NavMesh.CalculatePath(start, point, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                        connected.Add(Point("Zombie spawn " + (connected.Count + 1).ToString("00"), point));
                if (connected.Count < 3) throw new InvalidOperationException("The player and enemy spawn areas are not connected.");
                layout.zombieSpawns = connected.ToArray();
                Debug.Log("Crazy Cosmonauts: " + connected.Count + " connected zombie spawns.");
            }
            finally { nav.Remove(); }
            var lights = new GameObject("Arena lighting"); lights.transform.SetParent(root.transform, false);
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
            {
                var light = new GameObject("Cold ceiling light").AddComponent<Light>(); light.transform.SetParent(lights.transform, false);
                light.transform.localPosition = new Vector3(x * 12, 5, z * 12); light.type = LightType.Point; light.range = 19; light.intensity = 6;
                light.color = new Color(.72f, .87f, 1); light.cullingMask = 1 << 30; light.shadows = LightShadows.None;
            }
            content.arenaPrefab = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/ZombieShooter.prefab");
            EditorUtility.SetDirty(content); AssetDatabase.SaveAssets();

            Transform Point(string name, Vector3 position)
            { var point = new GameObject(name).transform; point.SetParent(root.transform, false); point.localPosition = position; return point; }
        }
        finally { Object.DestroyImmediate(root); }
    }
    static VectorImage Icon(string source, string target)
    {
        string text = File.ReadAllText(source).Replace("#000000", "#e6efe9");
        // Unspecified SVG path fills default to black; give the root a light inherited fill.
        int start = text.IndexOf("<svg", StringComparison.Ordinal); text = text.Insert(start + 4, " fill=\"#e6efe9\"");
        // Health's root already defines a fill; remove the duplicate rather than changing the supplied icon.
        text = text.Replace("fill=\"#e6efe9\" fill=\"#e6efe9\"", "fill=\"#e6efe9\"");
        File.WriteAllText(target, text); AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadAssetAtPath<VectorImage>(target);
    }
    static Material Material(Material source, string name)
    {
        string path = Folder + "/Materials/" + name.Replace('/', '_') + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Crazy Cosmonauts / " + name };
        if (source)
        {
            material.SetTexture("_BaseMap", source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null);
            material.SetColor("_BaseColor", source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.HasProperty("_Color") ? source.color : Color.white);
        }
        material.SetFloat("_Smoothness", .2f); AssetDatabase.CreateAsset(material, path); return material;
    }
    public static void BuildAndExit() { try { Build(); EditorApplication.Exit(0); } catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); } }
}
