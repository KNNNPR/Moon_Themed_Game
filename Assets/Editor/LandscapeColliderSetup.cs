using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MoonThemedGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace MoonThemedGame.Editor
{
    [InitializeOnLoad]
    public static class LandscapeColliderSetup
    {
        const string SetPath="Assets/Resources/LandscapeCollisionSet.asset";
        const string OperatorScene="Assets/Scenes/Operator.unity";
        static readonly string[] Models={"Assets/Models/LandScape.fbx","Assets/Models/second_lan.fbx"};
        static LandscapeColliderSetup()
        {
            if(Application.isBatchMode)return;
            EditorApplication.delayCall+=InstallWhenReady;
            EditorSceneManager.sceneOpened+=(scene,mode)=>EditorApplication.delayCall+=InstallWhenReady;
            EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.EnteredEditMode)EditorApplication.delayCall+=InstallWhenReady;};
        }

        static void InstallWhenReady()
        {
            if(EditorApplication.isCompiling || EditorApplication.isUpdating)
            {EditorApplication.delayCall+=InstallWhenReady;return;}
            var set=BuildManifest();
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            for(int i=0;i<SceneManager.sceneCount;i++)
            {
                var scene=SceneManager.GetSceneAt(i);
                if(scene.path!=OperatorScene || SessionState.GetBool("MoonGame.LandscapeColliders.v2."+scene.handle,false))continue;
                Apply(scene,set);SessionState.SetBool("MoonGame.LandscapeColliders.v2."+scene.handle,true);
            }
        }

        public static LandscapeCollisionSet BuildManifest()
        {
            var set=AssetDatabase.LoadAssetAtPath<LandscapeCollisionSet>(SetPath);
            if(!set){set=ScriptableObject.CreateInstance<LandscapeCollisionSet>();AssetDatabase.CreateAsset(set,SetPath);}
            var meshes=Models.SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<Mesh>()
                .Where(m=>!LandscapeCollisions.IsSurface(m.name)).OrderBy(m=>AssetDatabase.GetAssetPath(m)).ThenBy(m=>m.name).ToArray();
            if(!set.objectMeshes.SequenceEqual(meshes))
            {set.objectMeshes=meshes;EditorUtility.SetDirty(set);AssetDatabase.SaveAssetIfDirty(set);}
            return set;
        }

        [MenuItem("Tools/Moon Game/Level/Add Missing Landscape Colliders")]
        public static void ApplyCurrentScene()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            Apply(SceneManager.GetActiveScene(),BuildManifest());
        }

        public static int Apply(Scene scene,LandscapeCollisionSet set)
        {
            var created=new List<Collider>();
            int added=LandscapeCollisions.Ensure(scene,set,(go,type)=>
            {
                var collider=(Collider)Undo.AddComponent(go,type);
                created.Add(collider);
                return collider;
            },Undo.DestroyObjectImmediate);
            if(added>0)
            {
                foreach(var collider in created)PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
                EditorSceneManager.MarkSceneDirty(scene);
                Debug.Log($"[LandscapeColliders] Updated {added} props. Fences and lowercase cargo use BoxColliders; other existing colliders and terrain preserved.");
            }
            return added;
        }

        // Uses a disposable project copy; authored colliders are compared before saving/reloading.
        public static void RunValidation()
        {
            const string output="Logs/LandscapeColliders";
            Directory.CreateDirectory(output);var report=new StringBuilder();bool passed=false;
            try
            {
                var scene=EditorSceneManager.OpenScene(OperatorScene);
                var targets=Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                    .Where(f=>LandscapeCollisions.UsesBox(f) && !LandscapeCollisions.IsSurface(f.name) && !LandscapeCollisions.IsSurface(f.sharedMesh.name)).ToArray();
                int fences=targets.Count(f=>f.sharedMesh.name.StartsWith("fence",StringComparison.OrdinalIgnoreCase));
                int cargo=targets.Count(f=>f.GetComponent<Renderer>() && f.GetComponent<Renderer>().sharedMaterials.Any(m=>m && m.name=="cargo"));
                Require(fences>0 && cargo>0,"Expected both fence meshes and lowercase cargo materials in the scene.");
                var replaced=targets.SelectMany(f=>f.GetComponents<Collider>()).Where(c=>!(c is BoxCollider)).ToHashSet();
                var original=Object.FindObjectsByType<Collider>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                    .Where(c=>!replaced.Contains(c))
                    .ToDictionary(c=>c,c=>EditorJsonUtility.ToJson(c));
                int surfaceCount=original.Keys.Count(c=>LandscapeCollisions.IsSurface(c.name));
                var set=BuildManifest();Require(set.objectMeshes.Length>0,"No landscape prop meshes found.");
                int added=Apply(scene,set);
                foreach(var pair in original)Require(pair.Key && EditorJsonUtility.ToJson(pair.Key)==pair.Value,"Existing collider changed: "+pair.Key.name);
                foreach(var filter in targets)
                {
                    var colliders=filter.GetComponents<Collider>();
                    Require(colliders.Length>0 && colliders.All(c=>c is BoxCollider),"Non-box or missing collision: "+filter.name);
                    Require(filter.GetComponent<BoxCollider>().size.sqrMagnitude>0,"Empty fence/cargo box: "+filter.name);
                }
                Require(Object.FindObjectsByType<Collider>(FindObjectsInactive.Include,FindObjectsSortMode.None).Count(c=>LandscapeCollisions.IsSurface(c.name))==surfaceCount,"Terrain collider count changed.");
                Require(Apply(scene,set)==0,"Duplicate colliders on a repeated install.");
                ValidateBoxMigration(scene,set);
                int after=Object.FindObjectsByType<Collider>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length;
                EditorSceneManager.SaveScene(scene);EditorSceneManager.OpenScene(OperatorScene);
                Require(Object.FindObjectsByType<Collider>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==after,"Colliders were not serialized into the scene.");
                Require(LandscapeCollisions.Ensure(SceneManager.GetActiveScene(),set)==0,"Runtime fallback duplicated authored colliders.");
                report.AppendLine($"Updated {added} props; {fences} fences and {cargo} lowercase cargo objects have BoxColliders.");
                report.AppendLine($"Preserved {original.Count} existing boxes/other colliders unchanged; replaced {replaced.Count} non-box fence/cargo colliders.");
                report.AppendLine("Legacy MeshCollider conversion, local mesh bounds, collider settings and case-sensitive cargo/CARGO selection passed.");
                report.AppendLine("Repeated install, scene save/reload and runtime fallback are idempotent.");
                passed=true;
            }
            catch(Exception exception){report.AppendLine(exception.ToString());Debug.LogException(exception);}
            finally
            {
                File.WriteAllText(output+"/validation.txt",(passed?"PASS\n":"FAIL\n")+report);
                Debug.Log("[LandscapeColliders] "+report);
                if(Application.isBatchMode)EditorApplication.Exit(passed?0:1);
            }
        }

        static void ValidateBoxMigration(Scene scene,LandscapeCollisionSet set)
        {
            var fixture=new GameObject("Collider migration test");
            Material lower=null,upper=null;
            try
            {
                var fence=GameObject.CreatePrimitive(PrimitiveType.Cube);
                fence.name="fence.test";fence.transform.parent=fixture.transform;
                Object.DestroyImmediate(fence.GetComponent<Collider>());
                var mesh=fence.GetComponent<MeshFilter>().sharedMesh;
                var legacy=fence.AddComponent<MeshCollider>();legacy.sharedMesh=mesh;
                var lowerGo=Object.Instantiate(fence,fixture.transform);lowerGo.name="Lowercase cargo";
                lower=new Material(lowerGo.GetComponent<Renderer>().sharedMaterial){name="cargo"};
                lowerGo.GetComponent<Renderer>().sharedMaterial=lower;
                var old=lowerGo.GetComponent<MeshCollider>();old.convex=true;
                old.isTrigger=true;old.enabled=false;old.contactOffset=0.037f;
                old.includeLayers=1<<8;old.excludeLayers=1<<9;old.layerOverridePriority=4;
                string oldSettings=EditorJsonUtility.ToJson(old);
                float originalContactOffset=old.contactOffset;
                var upperGo=Object.Instantiate(fence,fixture.transform);upperGo.name="Uppercase cargo";
                upper=new Material(upperGo.GetComponent<Renderer>().sharedMaterial){name="CARGO"};
                upperGo.GetComponent<Renderer>().sharedMaterial=upper;
                var upperCollider=upperGo.GetComponent<Collider>();
                string upperSnapshot=EditorJsonUtility.ToJson(upperCollider);
                Require(LandscapeCollisions.Ensure(scene,set)==2,"Case-sensitive box conversion selected the wrong objects.");
                foreach(var go in new[]{fence,lowerGo})
                {
                    var box=go.GetComponent<BoxCollider>();
                    Require(box && !go.GetComponent<MeshCollider>(),"Legacy mesh collision was not replaced.");
                    Require(Vector3.Distance(box.center,mesh.bounds.center)<0.00001f && Vector3.Distance(box.size,mesh.bounds.size)<0.00001f,"Box does not fit local mesh bounds.");
                }
                var lowerBox=lowerGo.GetComponent<BoxCollider>();
                Require(lowerBox.isTrigger && !lowerBox.enabled && Mathf.Abs(lowerBox.contactOffset-originalContactOffset)<0.00001f &&
                    lowerBox.includeLayers==(1<<8) && lowerBox.excludeLayers==(1<<9) && lowerBox.layerOverridePriority==4,
                    "Converted collider settings changed. Before: "+oldSettings+" After: "+EditorJsonUtility.ToJson(lowerBox)+
                    $" Contact offset {originalContactOffset}/{lowerBox.contactOffset}.");
                Require(upperCollider && EditorJsonUtility.ToJson(upperCollider)==upperSnapshot && !upperGo.GetComponent<BoxCollider>(),"Uppercase CARGO was converted.");
                Require(LandscapeCollisions.Ensure(scene,set)==0,"Conversion added duplicate boxes.");
            }
            finally
            {
                Object.DestroyImmediate(fixture);
                if(lower)Object.DestroyImmediate(lower);
                if(upper)Object.DestroyImmediate(upper);
            }
        }
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
