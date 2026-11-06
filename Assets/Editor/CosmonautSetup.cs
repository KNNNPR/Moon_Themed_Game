using System;
using System.Collections.Generic;
using System.Linq;
using MoonThemedGame.Repair;
using UnityEditor;
using UnityEngine;
public static class CosmonautSetup
{
    public static void BuildAndExit()
    {
        try
        {
            const string modelPath="Assets/Models/cosmonaut.fbx";
            var importer=(ModelImporter)AssetImporter.GetAtPath(modelPath);importer.isReadable=true;importer.SaveAndReimport();
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var instance=UnityEngine.Object.Instantiate(model);var skin=instance.GetComponentInChildren<SkinnedMeshRenderer>();
            var bones=skin.bones;var mesh=UnityEngine.Object.Instantiate(skin.sharedMesh);mesh.name="Cosmonaut / first person body";
            var weights=mesh.boneWeights;var head=new HashSet<int>();
            for(int i=0;i<bones.Length;i++)if(bones[i].name=="Bone.004")head.Add(i);
            bool Hidden(int i){var w=weights[i];return (head.Contains(w.boneIndex0)?w.weight0:0)+(head.Contains(w.boneIndex1)?w.weight1:0)+(head.Contains(w.boneIndex2)?w.weight2:0)+(head.Contains(w.boneIndex3)?w.weight3:0)>.3f;}
            int removed=0;
            for(int s=0;s<mesh.subMeshCount;s++)
            {
                var original=mesh.GetTriangles(s);var kept=new List<int>();
                for(int i=0;i<original.Length;i+=3)
                    if(Hidden(original[i]) || Hidden(original[i+1]) || Hidden(original[i+2]))removed++;
                    else{kept.Add(original[i]);kept.Add(original[i+1]);kept.Add(original[i+2]);}
                mesh.SetTriangles(kept,s);
            }
            SealNeck(mesh,skin.transform,bones.First(b=>b.name=="Bone.003").position.y-.5f,Array.FindIndex(bones,b=>b.name=="Bone.003"));
            const string meshPath="Assets/Computer/CosmonautFirstPerson.asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,meshPath);
            const string matPath="Assets/Computer/CosmonautSuit.mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if(!mat){mat=new Material(Shader.Find("Old Tech/Retro Cel Lit")){name="CosmonautSuit"};AssetDatabase.CreateAsset(mat,matPath);}
            var source=skin.sharedMaterial;var texture=source.HasProperty("_BaseMap")?source.GetTexture("_BaseMap"):source.HasProperty("_MainTex")?source.GetTexture("_MainTex"):null;
            var color=source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):source.color;
            if(mat.HasProperty("_BaseMap"))mat.SetTexture("_BaseMap",texture);mat.color=color;
            // The camera can look into the neck opening; render the inner suit too.
            if(mat.HasProperty("_Cull"))mat.SetFloat("_Cull",0);
            const string configPath="Assets/Resources/CosmonautBodyAssets.asset";
            var assets=AssetDatabase.LoadAssetAtPath<CosmonautBodyAssets>(configPath);
            if(!assets){assets=ScriptableObject.CreateInstance<CosmonautBodyAssets>();AssetDatabase.CreateAsset(assets,configPath);}
            assets.model=model;assets.firstPersonMesh=mesh;assets.material=mat;
            assets.sourceFeetY=skin.sharedMesh.vertices.Min(v=>skin.transform.TransformPoint(v).y);
            assets.sourceEyeHeight=instance.GetComponentsInChildren<Transform>().First(t=>t.name=="Bone.004").position.y-assets.sourceFeetY;
            EditorUtility.SetDirty(mesh);EditorUtility.SetDirty(mat);EditorUtility.SetDirty(assets);AssetDatabase.SaveAssets();
            Debug.Log("Cosmonaut built; hidden head triangles="+removed+" eye="+assets.sourceEyeHeight+" feet="+assets.sourceFeetY+" color="+color+" texture="+(texture?texture.name:"none"));
            UnityEngine.Object.DestroyImmediate(instance);EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void SealNeck(Mesh mesh,Transform surface,float minimumWorldY,int neckIndex)
    {
        var vertices=mesh.vertices.ToList();var normals=mesh.normals.ToList();var uv=mesh.uv.ToList();var weights=mesh.boneWeights.ToList();
        var edges=new Dictionary<string,(int a,int b,int count)>();
        string Point(int i){var p=vertices[i];return Mathf.RoundToInt(p.x*1000000)+","+Mathf.RoundToInt(p.y*1000000)+","+Mathf.RoundToInt(p.z*1000000);}
        for(int s=0;s<mesh.subMeshCount;s++)
        {
            var triangles=mesh.GetTriangles(s);
            for(int i=0;i<triangles.Length;i+=3)for(int j=0;j<3;j++)
            {
                int a=triangles[i+j],b=triangles[i+(j+1)%3];string ka=Point(a),kb=Point(b);
                string key=string.CompareOrdinal(ka,kb)<0?ka+"/"+kb:kb+"/"+ka;
                if(edges.TryGetValue(key,out var edge))edges[key]=(edge.a,edge.b,edge.count+1);else edges[key]=(a,b,1);
            }
        }
        var boundary=edges.Values.Where(e=>e.count==1 && surface.TransformPoint(vertices[e.a]).y>minimumWorldY && surface.TransformPoint(vertices[e.b]).y>minimumWorldY).ToArray();
        if(boundary.Length<3)return;
        var indices=boundary.SelectMany(e=>new[]{e.a,e.b}).Distinct().ToArray();
        var center=indices.Aggregate(Vector3.zero,(sum,i)=>sum+vertices[i])/indices.Length;
        var texcoord=indices.Aggregate(Vector2.zero,(sum,i)=>sum+uv[i])/indices.Length;
        int middle=vertices.Count;vertices.Add(center);uv.Add(texcoord);normals.Add(surface.InverseTransformDirection(Vector3.up));weights.Add(new BoneWeight{boneIndex0=neckIndex,weight0=1});
        var cap=mesh.GetTriangles(0).ToList();
        foreach(var edge in boundary){cap.Add(edge.b);cap.Add(edge.a);cap.Add(middle);}
        mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.boneWeights=weights.ToArray();mesh.SetTriangles(cap,0);mesh.RecalculateBounds();
    }
}
