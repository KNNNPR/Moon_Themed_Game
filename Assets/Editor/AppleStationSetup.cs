using System;
using System.Linq;
using MoonThemedGame.Interactions;
using MoonThemedGame.Rendering;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace MoonThemedGame.Editor
{
    [InitializeOnLoad]
    public static class AppleStationSetup
    {
        const string DefinitionPath="Assets/Resources/AppleStationAssets.asset";
        const string CollisionPath="Assets/Resources/AppleStationCollision.asset";
        static AppleStationSetup()
        {
            if(Application.isBatchMode)return;
            EditorApplication.delayCall+=WhenReady;
            EditorSceneManager.sceneOpened+=(scene,mode)=>EditorApplication.delayCall+=WhenReady;
        }
        static void WhenReady()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            if(EditorApplication.isCompiling || EditorApplication.isUpdating){EditorApplication.delayCall+=WhenReady;return;}
            Install();
        }
        [MenuItem("Tools/Moon Game/Apple Station/Install or Repair")]
        public static void Install()
        {
            var definition=AssetDatabase.LoadAssetAtPath<AppleStationAssets>(DefinitionPath);
            if(!definition)
            {
                var mesh=AssetDatabase.LoadAllAssetsAtPath("Assets/Models/Pale_Horse.fbx").OfType<Mesh>().First(m=>
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m,out string _,out long id);
                    return id==5512361600001258384;
                });
                definition=ScriptableObject.CreateInstance<AppleStationAssets>();
                definition.stationMesh=mesh;
                definition.applePrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/SuspiciousApple.prefab");
                definition.animatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Apple Station.controller");
                definition.localDropPosition=new Vector3(-0.000324f,0.0048f,-0.0135f);
                definition.localOutwardDirection=Vector3.up;
                var collision=AssetDatabase.LoadAssetAtPath<Mesh>(CollisionPath);
                if(!collision)
                {
                    using var data=MeshUtility.AcquireReadOnlyMeshData(mesh);
                    using var vertices=new NativeArray<Vector3>(mesh.vertexCount,Allocator.Temp);
                    data[0].GetVertices(vertices);
                    collision=new Mesh{name="Apple Station collision"};collision.SetVertices(vertices);
                    var triangles=new System.Collections.Generic.List<int>();
                    for(int sub=0;sub<data[0].subMeshCount;sub++)
                    {
                        using var indices=new NativeArray<int>(data[0].GetSubMesh(sub).indexCount,Allocator.Temp);
                        data[0].GetIndices(indices,sub);triangles.AddRange(indices.ToArray());
                    }
                    collision.SetTriangles(triangles,0);collision.RecalculateBounds();
                    AssetDatabase.CreateAsset(collision,CollisionPath);
                }
                definition.collisionMesh=collision;
                AssetDatabase.CreateAsset(definition,DefinitionPath);
            }
            foreach(var path in VisualStyleSetup.RendererPaths)
            {
                var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                var feature=renderer.rendererFeatures.OfType<OperatorIntoxicationFeature>().FirstOrDefault();
                if(feature)continue;
                feature=ScriptableObject.CreateInstance<OperatorIntoxicationFeature>();feature.name="Operator Intoxication";
                feature.passMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/OperatorIntoxication.mat");
                if(!feature.passMaterial)throw new InvalidOperationException("Intoxication material is missing.");
                feature.injectionPoint=FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
                feature.fetchColorBuffer=true;feature.requirements=ScriptableRenderPassInput.None;
                feature.bindDepthStencilAttachment=false;
                AssetDatabase.AddObjectToAsset(feature,renderer);renderer.rendererFeatures.Add(feature);
                var serialized=new SerializedObject(renderer);var map=serialized.FindProperty("m_RendererFeatureMap");
                map.arraySize=renderer.rendererFeatures.Count;
                for(int i=0;i<map.arraySize;i++)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i],out string _,out long id);
                    map.GetArrayElementAtIndex(i).longValue=id;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();renderer.SetDirty();EditorUtility.SetDirty(feature);EditorUtility.SetDirty(renderer);
                AssetDatabase.SaveAssetIfDirty(renderer);
            }
            foreach(var filter in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                if(filter.sharedMesh!=definition.stationMesh || !filter.gameObject.scene.IsValid())continue;
                var station=filter.GetComponent<AppleStation>();
                if(station && station.assets && station.dropPoint && station.GetComponent<MeshCollider>())continue;
                Undo.RegisterFullObjectHierarchyUndo(filter.gameObject,"Configure Apple Station");
                if(!station)station=Undo.AddComponent<AppleStation>(filter.gameObject);
                station.Configure(definition);
                EditorSceneManager.MarkSceneDirty(filter.gameObject.scene);
            }
        }
    }
}
