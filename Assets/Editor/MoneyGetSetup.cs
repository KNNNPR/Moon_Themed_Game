using System.Collections.Generic;
using MoonThemedGame.Computer;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

public static class MoneyGetSetup
{
    [MenuItem("Tools/Moon Game/MoneyGetApp/Build Apple Preview")]
    public static void BuildPreview()
    {
        const string meshPath="Assets/Computer/MoneyGetApplePreview.asset";
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Apple.prefab");
        var content=AssetDatabase.LoadAssetAtPath<MoneyGetContent>("Assets/Resources/MoneyGetContent.asset");
        var filter=source.GetComponent<MeshFilter>();var preview=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        var mesh=new Mesh{name="MoneyGet / Apple preview"};
        using(var data=MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
        {
            var shape=data[0];using(var vertices=new NativeArray<Vector3>(shape.vertexCount,Allocator.Temp))
            {shape.GetVertices(vertices);mesh.vertices=vertices.ToArray();}
            var triangles=new List<int>();
            for(int s=0;s<shape.subMeshCount;s++)using(var indices=new NativeArray<int>(shape.GetSubMesh(s).indexCount,Allocator.Temp))
            {shape.GetIndices(indices,s);triangles.AddRange(indices.ToArray());}
            mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();mesh.RecalculateNormals();
        }
        if(preview){EditorUtility.CopySerialized(mesh,preview);Object.DestroyImmediate(mesh);}else{preview=mesh;AssetDatabase.CreateAsset(preview,meshPath);}
        var model=new GameObject("Apple / MoneyGet store preview");
        try
        {
            // Bake the original apple's rotation into a unit-scale preview child.
            var child=new GameObject("Apple");child.transform.SetParent(model.transform,false);child.transform.localRotation=source.transform.localRotation;
            child.AddComponent<MeshFilter>().sharedMesh=preview;child.AddComponent<MeshRenderer>().sharedMaterials=source.GetComponent<MeshRenderer>().sharedMaterials;
            content.shopModel=PrefabUtility.SaveAsPrefabAsset(model,"Assets/Prefabs/MoneyGetApplePreview.prefab");
            EditorUtility.SetDirty(content);AssetDatabase.SaveAssets();
        }
        finally{Object.DestroyImmediate(model);}
    }
}
