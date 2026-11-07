using System;
using System.Collections.Generic;
using MoonThemedGame.Repair;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class PlayerPistolSetup
{
    const string Folder="Assets/Weapons";
    [MenuItem("Tools/Moon Game/Weapons/Rebuild Pistol Prefab")]
    public static void Build()
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/makarov.fbx");
        if(!source)throw new InvalidOperationException("makarov.fbx is missing.");
        if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets","Weapons");
        var material=Asset<Material>(Folder+"/Pistol.mat",()=>new Material(Shader.Find("Old Tech/Retro Cel Lit")));
        material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/pistol.png"));
        material.SetColor("_BaseColor",Color.white);EditorUtility.SetDirty(material);
        var radial=Asset<Texture2D>(Folder+"/Particle.asset",()=>
        {
            var texture=new Texture2D(32,32,TextureFormat.RGBA32,false){name="Pistol / soft particles",wrapMode=TextureWrapMode.Clamp};
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)
            {float d=new Vector2((x+.5f)/16-1,(y+.5f)/16-1).magnitude;texture.SetPixel(x,y,new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-d),2)));}
            texture.Apply();return texture;
        });
        var fx=Asset<Material>(Folder+"/Particles.mat",()=>new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")));
        Transparent(fx,radial,Color.white);
        var impact=Asset<Material>(Folder+"/Impact.mat",()=>new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")));
        Transparent(impact,radial,new Color(.025f,.02f,.018f,1));
        var brass=Asset<Material>(Folder+"/Casing.mat",()=>new Material(Shader.Find("Old Tech/Retro Cel Lit")));
        brass.SetColor("_BaseColor",new Color(.6f,.43f,.17f));EditorUtility.SetDirty(brass);
        var instance=Object.Instantiate(source);var prepared=new GameObject("Pistol");
        try
        {
            var weapon=prepared.AddComponent<PlayerPistol>();
            var sourceGrip=new Vector3(-.7f,1.1f,-1.1f);const float scale=.0351f;
            var meshes=instance.GetComponentsInChildren<MeshFilter>();
            foreach(var filter in meshes)
            {
                if(filter.name.Equals("pistol",StringComparison.OrdinalIgnoreCase))
                {
                    SplitSlide(filter,out var frame,out var slide);
                    var frameMesh=SaveMesh(Folder+"/Frame.asset",frame);var slideMesh=SaveMesh(Folder+"/Slide.asset",slide);
                    Geometry("Frame",filter,frameMesh);
                    weapon.slide=Geometry("Slide",filter,slideMesh);
                }
                else
                {
                    var node=Geometry(filter.name,filter,filter.sharedMesh);
                    if(filter.name=="mag")weapon.magazine=node;
                }
            }
            Transform Geometry(string name,MeshFilter origin,Mesh mesh)
            {
                var go=new GameObject(name);go.transform.SetParent(prepared.transform,false);
                go.transform.localPosition=(origin.transform.position-sourceGrip)*scale;
                go.transform.localRotation=origin.transform.rotation;go.transform.localScale=origin.transform.lossyScale*scale;
                go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
                return go.transform;
            }
            Transform Socket(string name,Vector3 local)
            {var t=new GameObject(name).transform;t.SetParent(prepared.transform,false);t.localPosition=local;return t;}
            weapon.muzzle=Socket("Muzzle",(new Vector3(-.7f,3.13f,3.72f)-sourceGrip)*scale);
            weapon.rearSight=Socket("Rear sight",(new Vector3(-.7f,3.66f,-1.7f)-sourceGrip)*scale);
            weapon.frontSight=Socket("Front sight",(new Vector3(-.7f,3.66f,3.15f)-sourceGrip)*scale);
            weapon.rightGrip=Socket("Right grip",new Vector3(.008f,-.015f,-.012f));
            weapon.leftGrip=Socket("Support grip",new Vector3(-.023f,.005f,.015f));
            weapon.effectMaterial=fx;weapon.impactMaterial=impact;weapon.casingMaterial=brass;
            var body=prepared.GetComponent<Rigidbody>();body.mass=.8f;body.interpolation=RigidbodyInterpolation.Interpolate;
            PrefabUtility.SaveAsPrefabAsset(prepared,"Assets/Resources/PlayerPistol.prefab");
            PlayerPistolAnimationSetup.Prepare();
            AssetDatabase.SaveAssets();
        }
        finally{Object.DestroyImmediate(instance);Object.DestroyImmediate(prepared);}
    }
    static void Transparent(Material material,Texture texture,Color color)
    {
        material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",color);
        material.SetFloat("_Surface",1);material.SetFloat("_Blend",0);material.SetFloat("_ZWrite",0);
        material.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_Cull",0);material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.renderQueue=3000;
        EditorUtility.SetDirty(material);
    }
    static T Asset<T>(string path,Func<T> create) where T:Object
    {var value=AssetDatabase.LoadAssetAtPath<T>(path);if(value)return value;value=create();AssetDatabase.CreateAsset(value,path);return value;}
    static Mesh SaveMesh(string path,Mesh mesh)
    {var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(!existing){AssetDatabase.CreateAsset(mesh,path);return mesh;}EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);return existing;}
    static void SplitSlide(MeshFilter source,out Mesh frame,out Mesh slide)
    {
        // The supplied model has a separate magazine but one combined slide/frame mesh.
        // Read its imported data without enabling Read/Write on the original FBX.
        using var data=MeshUtility.AcquireReadOnlyMeshData(source.sharedMesh);
        var mesh=data[0];int count=mesh.vertexCount;
        using var v=new NativeArray<Vector3>(count,Allocator.Temp);mesh.GetVertices(v);
        using var n=new NativeArray<Vector3>(count,Allocator.Temp);mesh.GetNormals(n);
        using var uv=new NativeArray<Vector2>(count,Allocator.Temp);mesh.GetUVs(0,uv);
        var bottom=new List<int>();var top=new List<int>();
        for(int sub=0;sub<mesh.subMeshCount;sub++)
        {
            using var indices=new NativeArray<int>(mesh.GetSubMesh(sub).indexCount,Allocator.Temp);mesh.GetIndices(indices,sub);
            for(int i=0;i<indices.Length;i+=3)
            {
                var center=source.transform.TransformPoint((v[indices[i]]+v[indices[i+1]]+v[indices[i+2]])/3);
                var list=center.y>2.65f?top:bottom;
                list.Add(indices[i]);list.Add(indices[i+1]);list.Add(indices[i+2]);
            }
        }
        Mesh Make(string name,List<int> indices)
        {var result=new Mesh{name=name,vertices=v.ToArray(),normals=n.ToArray(),uv=uv.ToArray(),triangles=indices.ToArray()};result.RecalculateBounds();return result;}
        frame=Make("Makarov / frame",bottom);slide=Make("Makarov / animated slide",top);
    }
    public static void BuildAndExit()
    {try{Build();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
