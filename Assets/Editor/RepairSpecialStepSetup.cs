using System.Collections.Generic;
using System.IO;
using MoonThemedGame.Repair;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoonThemedGame.Editor
{
    public static class RepairSpecialStepSetup
    {
        const string Folder="Assets/Resources/RepairSpecialStepMeshes";
        [MenuItem("Tools/Moon Game/Repair/Build special repair meshes")]
        public static void Build()
        {
            var dropper=Object.FindFirstObjectByType<RepairRobotDropper>();
            if(!dropper)throw new System.InvalidOperationException("Open the Operator scene first.");
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/Resources","RepairSpecialStepMeshes");
            var settings=AssetDatabase.LoadAssetAtPath<RepairSpecialStepSettings>("Assets/Resources/RepairSpecialStepSettings.asset");
            if(!settings){settings=ScriptableObject.CreateInstance<RepairSpecialStepSettings>();AssetDatabase.CreateAsset(settings,"Assets/Resources/RepairSpecialStepSettings.asset");}
            var pairs=new List<RepairSpecialStepSettings.DentMesh>();var seen=new HashSet<Mesh>();
            foreach(var family in dropper.RobotTypes)foreach(var part in family.parts)
            {
                if(!part.prefab||!part.prefab.name.ToLowerInvariant().Contains("body"))continue;
                foreach(var source in part.prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if(!source.sharedMesh||!seen.Add(source.sharedMesh))continue;
                    string path=Folder+"/"+family.DisplayName.Replace("/","_")+"_"+source.sharedMesh.name+".asset";
                    var dent=CreateDent(source.sharedMesh);var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if(existing){EditorUtility.CopySerialized(dent,existing);Object.DestroyImmediate(dent);dent=existing;EditorUtility.SetDirty(existing);}
                    else AssetDatabase.CreateAsset(dent,path);
                    pairs.Add(new RepairSpecialStepSettings.DentMesh{source=source.sharedMesh,blend=dent});
                }
            }
            settings.dentMeshes=pairs.ToArray();EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets();
            Debug.Log("Repair special steps: baked "+pairs.Count+" dent blend meshes.");
        }
        static Mesh CreateDent(Mesh source)
        {
            // Imported FBXs may forbid runtime CPU reads. Bake a readable blend mesh once in the editor.
            using(var read=MeshUtility.AcquireReadOnlyMeshData(source))
            {
                var sourceData=read[0];var write=Mesh.AllocateWritableMeshData(1);var target=write[0];
                target.SetVertexBufferParams(sourceData.vertexCount,source.GetVertexAttributes());
                for(int stream=0;stream<source.vertexBufferCount;stream++)target.GetVertexData<byte>(stream).CopyFrom(sourceData.GetVertexData<byte>(stream));
                int bytes=sourceData.GetIndexData<byte>().Length;
                target.SetIndexBufferParams(bytes/(source.indexFormat==IndexFormat.UInt16?2:4),source.indexFormat);
                target.GetIndexData<byte>().CopyFrom(sourceData.GetIndexData<byte>());target.subMeshCount=sourceData.subMeshCount;
                for(int i=0;i<sourceData.subMeshCount;i++)target.SetSubMesh(i,sourceData.GetSubMesh(i),MeshUpdateFlags.DontRecalculateBounds|MeshUpdateFlags.DontValidateIndices);
                var mesh=new Mesh{name=source.name+" [repair dent]"};Mesh.ApplyAndDisposeWritableMeshData(write,mesh);mesh.bounds=source.bounds;
                if(mesh.vertexCount<10000)Subdivide(mesh);
                var vertices=mesh.vertices;var normals=mesh.normals;
                if(normals.Length!=vertices.Length){mesh.RecalculateNormals();normals=mesh.normals;}
                var bounds=mesh.bounds;var size=bounds.size;
                int thin=size.x<=size.y&&size.x<=size.z?0:size.y<=size.z?1:2;
                int a=(thin+1)%3,b=(thin+2)%3;
                float depth=Mathf.Max(size[a],size[b])*.075f;
                var delta=new Vector3[vertices.Length];var deformed=(Vector3[])vertices.Clone();var expanded=bounds;
                for(int i=0;i<vertices.Length;i++)
                {
                    var offset=vertices[i]-bounds.center;
                    float u=offset[a]/Mathf.Max(.000001f,bounds.extents[a]),v=offset[b]/Mathf.Max(.000001f,bounds.extents[b]);
                    float falloff=Mathf.Exp(-(u*u+v*v)*3.4f);
                    delta[i][thin]=-depth*falloff;deformed[i]+=delta[i];expanded.Encapsulate(deformed[i]);
                }
                mesh.vertices=deformed;mesh.RecalculateNormals();var bentNormals=mesh.normals;var normalDelta=new Vector3[vertices.Length];
                for(int i=0;i<vertices.Length;i++)normalDelta[i]=bentNormals[i]-normals[i];
                mesh.vertices=vertices;mesh.normals=normals;mesh.AddBlendShapeFrame("Dent",100,delta,normalDelta,new Vector3[vertices.Length]);mesh.bounds=expanded;
                return mesh;
            }
        }
        static void Subdivide(Mesh mesh)
        {
            for(int sub=0;sub<mesh.subMeshCount;sub++)if(mesh.GetTopology(sub)!=MeshTopology.Triangles)return;
            var vertices=new List<Vector3>(mesh.vertices);var normals=new List<Vector3>(mesh.normals);
            var tangents=new List<Vector4>(mesh.tangents);var colors=new List<Color>(mesh.colors);
            bool hasNormals=normals.Count==vertices.Count,hasTangents=tangents.Count==vertices.Count,hasColors=colors.Count==vertices.Count;
            var uv=new List<Vector4>[8];
            for(int channel=0;channel<uv.Length;channel++){uv[channel]=new List<Vector4>();mesh.GetUVs(channel,uv[channel]);}
            var edges=new Dictionary<ulong,int>();
            int Mid(int a,int b)
            {
                ulong key=((ulong)(uint)Mathf.Min(a,b)<<32)|(uint)Mathf.Max(a,b);
                if(edges.TryGetValue(key,out int found))return found;
                int index=vertices.Count;edges.Add(key,index);vertices.Add((vertices[a]+vertices[b])*.5f);
                if(hasNormals)normals.Add((normals[a]+normals[b]).normalized);
                if(hasTangents){var tangent=(tangents[a]+tangents[b])*.5f;tangent.w=tangents[a].w;tangents.Add(tangent);}
                if(hasColors)colors.Add(Color.Lerp(colors[a],colors[b],.5f));
                for(int channel=0;channel<uv.Length;channel++)if(uv[channel].Count>0)uv[channel].Add((uv[channel][a]+uv[channel][b])*.5f);
                return index;
            }
            var indices=new List<int>[mesh.subMeshCount];
            for(int sub=0;sub<indices.Length;sub++)
            {
                indices[sub]=new List<int>();var old=mesh.GetTriangles(sub);
                for(int i=0;i<old.Length;i+=3)
                {
                    int a=old[i],b=old[i+1],c=old[i+2],ab=Mid(a,b),bc=Mid(b,c),ca=Mid(c,a);
                    indices[sub].AddRange(new[]{a,ab,ca,ab,b,bc,ca,bc,c,ab,bc,ca});
                }
            }
            mesh.Clear();mesh.indexFormat=vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;mesh.SetVertices(vertices);
            if(hasNormals)mesh.SetNormals(normals);if(hasTangents)mesh.SetTangents(tangents);if(hasColors)mesh.SetColors(colors);
            for(int channel=0;channel<uv.Length;channel++)if(uv[channel].Count>0)mesh.SetUVs(channel,uv[channel]);
            mesh.subMeshCount=indices.Length;for(int sub=0;sub<indices.Length;sub++)mesh.SetTriangles(indices[sub],sub);
            mesh.RecalculateBounds();
        }
    }
}
