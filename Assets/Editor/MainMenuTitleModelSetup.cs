using System;
using System.Linq;
using MoonThemedGame.Computer;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class MainMenuTitleModelSetup
{
    [MenuItem("Tools/Moon Game/Main Menu/Use palehorsetext Model")]
    public static void Build()
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/palehorsetext.fbx");
        if(!source)throw new InvalidOperationException("palehorsetext.fbx not found.");
        var assets=AssetDatabase.LoadAssetAtPath<MainMenuAssets>("Assets/Resources/MainMenuAssets.asset");
        var instance=Object.Instantiate(source);var prepared=new GameObject("PALE HORSE / palehorsetext");
        try
        {
            // The supplied text runs along -X and faces +Z. Turn it toward the menu camera.
            instance.transform.rotation=Quaternion.Euler(0,180,0);
            var shapes=instance.GetComponentsInChildren<MeshRenderer>();
            float height=shapes.Max(r=>r.bounds.size.y);
            var rows=new[]{new[]{"P","A","L","E"},new[]{"H","O","R","S","E.001"}};
            for(int row=0;row<rows.Length;row++)
            {
                var first=shapes.First(r=>r.name==rows[row][0]);float start=first.bounds.center.x;
                foreach(var name in rows[row])
                {
                    var shape=shapes.First(r=>r.name==name);var center=shape.bounds.center;
                    var pivot=new GameObject(name+" / palehorsetext").transform;pivot.SetParent(prepared.transform,false);
                    pivot.localPosition=new Vector3((center.x-start)/height,-row*1.24f,0);
                    var geometry=new GameObject("Authored mesh").transform;geometry.SetParent(pivot,false);
                    geometry.localPosition=(shape.transform.position-center)/height;geometry.localRotation=shape.transform.rotation;geometry.localScale=shape.transform.lossyScale/height;
                    geometry.gameObject.AddComponent<MeshFilter>().sharedMesh=shape.GetComponent<MeshFilter>().sharedMesh;
                    geometry.gameObject.AddComponent<MeshRenderer>().sharedMaterial=assets.titleMaterial;
                }
            }
            assets.titleModel=PrefabUtility.SaveAsPrefabAsset(prepared,"Assets/Prefabs/PaleHorseTextMenu.prefab");
            EditorUtility.SetDirty(assets);AssetDatabase.SaveAssets();
        }
        finally{Object.DestroyImmediate(instance);Object.DestroyImmediate(prepared);}
    }
    public static void BuildAndExit()
    {try{Build();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
