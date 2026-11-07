using System;
using System.Linq;
using MoonThemedGame.Scavenger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Additional fixed positions, sampled on the closed-gate navigation survey. Runtime
// chooses among them; it never sprays parts onto untested cliffs or collider bounds.
public static class SalvageVariationSetup
{
    public const string Notes="74 candidate positions, empty without an active repair order. Each distance tier has eight safe alternatives across roads, side paths and wrecks. Only unstocked required quantities spawn, randomized and spaced apart; previous order positions are discouraged. At most one target may use a manipulator cache, only after purchasing the arm and within the accepted order's battery budget. Six story distance tiers and existing scene position overrides are preserved.";
    static readonly Vector3[][] Positions=
    {
        new[]{new Vector3(41.6f,1.19f,-27.1f),new Vector3(-24.4f,-.01f,-76.1f),new Vector3(23.6f,.49f,-22.9f),new Vector3(3.5f,.69f,-73.6f)},
        new[]{new Vector3(36.1f,1.89f,-103.5f),new Vector3(-18.9f,1.29f,-88.4f),new Vector3(11.1f,1.09f,-95.2f),new Vector3(-8.5f,2.29f,-104)},
        new[]{new Vector3(-14,1.89f,-127.6f),new Vector3(47.7f,-.31f,-70),new Vector3(26.3f,1.99f,-153.1f),new Vector3(-5,1.89f,-156.5f)},
        new[]{new Vector3(-35.6f,1.09f,-194.7f),new Vector3(83.1f,1.74f,-147),new Vector3(-49.4f,3.89f,-127.4f),new Vector3(-72.7f,1.89f,-172.3f)},
        new[]{new Vector3(121.3f,-9.36f,-278.4f),new Vector3(-5.1f,1.29f,-172.5f),new Vector3(-107.6f,1.89f,-187.2f),new Vector3(-145.5f,1.89f,-225.6f)},
        new[]{new Vector3(143.6f,-9.21f,-329.1f),new Vector3(160.8f,-9.21f,-358.8f),new Vector3(183.4f,-2.81f,-368.5f),new Vector3(176.6f,-8.66f,-384.2f)}
    };
    public static void AddTo(GameObject root)
    {
        Physics.SyncTransforms();
        var names=new[]{"Wheel","Battery","Headlights","Window_Robot"};
        for(int tier=0;tier<Positions.Length;tier++)
        {
            string name=(14+tier)+" VARIED - tier "+tier;
            if(root.transform.Find(name))continue;
            var group=new GameObject(name).transform;group.SetParent(root.transform,false);
            for(int i=0;i<Positions[tier].Length;i++)
            {
                var position=Positions[tier][i];
                var hits=Physics.RaycastAll(position+Vector3.up*2,Vector3.down,5,~0,QueryTriggerInteraction.Ignore)
                    .Where(h=>h.normal.y>.7f && !h.collider.GetComponentInParent<ScavengerDrive>() && !h.collider.GetComponentInParent<FirstPersonController>())
                    .OrderBy(h=>h.distance).ToArray();
                // The survey stores the voxelized nav surface, which sits above uneven
                // regolith. Snap to its actual collision surface before authoring the point.
                if(hits.Length==0 || Mathf.Abs(hits[0].point.y-position.y)>2.5f)throw new InvalidOperationException("Check salvage floor at "+position+": "+(hits.Length==0?"no floor":hits[0].collider.name+" "+hits[0].point));
                var marker=new GameObject((51+tier*4+i)+" "+names[i]);marker.transform.SetParent(group,false);
                marker.transform.SetPositionAndRotation(hits[0].point,Quaternion.FromToRotation(Vector3.up,hits[0].normal));
                var point=marker.AddComponent<SalvageSpawnPoint>();
                point.part=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Parts/"+names[i]+".prefab");
                point.routeNotes="Alternative fixed ground location for distance tier "+tier+". Randomized order supplies; individual return trips may be used instead of collecting the entire spread on one battery. Do not place on movable geometry.";
            }
        }
        root.GetComponent<SalvageSpawnLayout>().balanceNotes=Notes;
    }
    [MenuItem("Tools/Moon Game/Salvage/Add Varied Order Spawns")]
    public static void Prepare()
    {
        var root=PrefabUtility.LoadPrefabContents(SalvageLayoutSetup.PrefabPath);
        try{AddTo(root);SalvageOrderProgressionSetup.Annotate(root);PrefabUtility.SaveAsPrefabAsset(root,SalvageLayoutSetup.PrefabPath);AssetDatabase.SaveAssets();}
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    public static void PrepareAndExit()
    {
        try
        {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/Operator.unity");
            MoonThemedGame.World.LandscapeCollisions.Ensure(scene,Resources.Load<MoonThemedGame.World.LandscapeCollisionSet>("LandscapeCollisionSet"));
            Prepare();EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
