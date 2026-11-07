using System;
using System.Linq;
using MoonThemedGame.Repair;
using UnityEditor;
using UnityEngine;

public static class PlayerPistolAnimationSetup
{
    public const string PrefabPath="Assets/Resources/PlayerPistol.prefab";
    const string Folder="Assets/Weapons/Animations";
    [MenuItem("Tools/Moon Game/Weapons/Prepare Editable Pistol Reload")]
    public static void Prepare()
    {
        if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/Weapons","Animations");
        var root=PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var pistol=root.GetComponent<PlayerPistol>();
            var motion=root.transform.Find("Reload Motion");
            if(!motion)
            {
                var children=root.transform.Cast<Transform>().ToArray();
                motion=new GameObject("Reload Motion").transform;motion.SetParent(root.transform,false);
                foreach(var child in children)child.SetParent(motion,false);
            }
            pistol.reloadMotion=motion;
            if(pistol.leftGrip && pistol.leftGrip.parent!=pistol.magazine)
            {
                pistol.leftGrip.name="Magazine hand target";
                pistol.leftGrip.position=pistol.magazine.position-root.transform.right*.015f;
                pistol.leftGrip.SetParent(pistol.magazine,true);
            }
            var tactical=GetClip("PistolReload",pistol,false,2.8f);
            var empty=GetClip("PistolReloadEmpty",pistol,true,3.05f);
            if(!pistol.reloadClip)pistol.reloadClip=tactical;
            if(!pistol.emptyReloadClip)pistol.emptyReloadClip=empty;
            var animation=root.GetComponent<Animation>();if(!animation)animation=root.AddComponent<Animation>();
            animation.playAutomatically=false;animation.clip=pistol.reloadClip;
            animation.AddClip(pistol.reloadClip,pistol.reloadClip.name);
            animation.AddClip(pistol.emptyReloadClip,pistol.emptyReloadClip.name);
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);AssetDatabase.SaveAssets();
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    static AnimationClip GetClip(string name,PlayerPistol pistol,bool empty,float length)
    {
        string path=Folder+"/"+name+".anim";
        var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);if(existing)return existing;
        var clip=new AnimationClip{name=name,legacy=true,frameRate=60,wrapMode=WrapMode.Once};
        string motion=AnimationUtility.CalculateTransformPath(pistol.reloadMotion,pistol.transform);
        string mag=AnimationUtility.CalculateTransformPath(pistol.magazine,pistol.transform);
        string slide=AnimationUtility.CalculateTransformPath(pistol.slide,pistol.transform);
        Curves(clip,motion,"m_LocalPosition",length,Vector3.zero,new[]{
            K(0,0,0,0),K(.12f,-.012f,.015f,-.008f),K(.28f,-.025f,.042f,-.030f),
            K(.48f,-.027f,.045f,-.032f),K(.70f,-.022f,.040f,-.030f),K(.85f,-.012f,.020f,-.012f),K(1,0,0,0)});
        Curves(clip,motion,"localEulerAnglesRaw",length,Vector3.zero,new[]{
            K(0,0,0,0),K(.12f,-5,-8,5),K(.28f,-12,-22,18),K(.48f,-10,-20,20),K(.70f,-10,-18,17),K(.85f,-5,-5,5),K(1,0,0,0)});
        Curves(clip,mag,"m_LocalPosition",length,pistol.magazine.localPosition,new[]{
            K(0,0,0,0),K(.13f,0,0,0),K(.23f,0,-.04f,.008f),K(.34f,-.025f,-.15f,-.005f),
            K(.48f,-.045f,-.16f,-.02f),K(.59f,-.025f,-.12f,.01f),K(.72f,0,-.018f,0),K(.77f,0,0,0),K(1,0,0,0)});
        var euler=pistol.magazine.localEulerAngles;
        euler=new Vector3(Mathf.DeltaAngle(0,euler.x),Mathf.DeltaAngle(0,euler.y),Mathf.DeltaAngle(0,euler.z));
        Curves(clip,mag,"localEulerAnglesRaw",length,euler,new[]{
            K(0,0,0,0),K(.13f,0,0,0),K(.34f,-8,0,-12),K(.48f,5,-12,-20),K(.59f,2,-5,-8),K(.77f,0,0,0),K(1,0,0,0)});
        Curves(clip,slide,"m_LocalPosition",length,pistol.slide.localPosition,empty?new[]{
            K(0,0,0,-.024f),K(.82f,0,0,-.024f),K(.86f,0,0,-.03f),K(.92f,0,0,0),K(1,0,0,0)}:new[]{K(0,0,0,0),K(1,0,0,0)});
        AssetDatabase.CreateAsset(clip,path);return clip;
    }
    struct PoseKey {public float t;public Vector3 v;}
    static PoseKey K(float time,float x,float y,float z)=>new PoseKey{t=time,v=new Vector3(x,y,z)};
    static void Curves(AnimationClip clip,string path,string property,float length,Vector3 rest,PoseKey[] poses)
    {
        for(int axis=0;axis<3;axis++)
        {
            var keys=poses.Select(p=>new Keyframe(Mathf.Round(p.t*length*60)/60,rest[axis]+p.v[axis],0,0)).ToArray();
            var curve=new AnimationCurve(keys){preWrapMode=WrapMode.ClampForever,postWrapMode=WrapMode.ClampForever};
            AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),property+"."+"xyz"[axis]),curve);
        }
    }
    public static void PrepareAndExit()
    {try{Prepare();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
