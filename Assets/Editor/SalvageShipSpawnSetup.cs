using System;
using System.Linq;
using MoonThemedGame.Scavenger;
using MoonThemedGame.World;
using UnityEditor;
using UnityEngine;

public static class SalvageShipSpawnSetup
{
    public const string GroupName="13 SHIP - interior hold";
    public static void AddTo(GameObject root)
    {
        if(root.transform.Find(GroupName))return;
        var ship=UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).FirstOrDefault(LandscapeCollisions.IsCargoShip);
        if(!ship)throw new InvalidOperationException("Open Operator: Cube.064 was not found.");
        var group=new GameObject(GroupName).transform;group.SetParent(root.transform,false);
        var positions=new[]{new Vector3(-168.191f,-70.246f,-443.496f),new Vector3(-162.843f,-71.716f,-449.223f)};
        var parts=new[]{"Battery","Wheel"};
        for(int i=0;i<positions.Length;i++)
        {
            var hits=Physics.RaycastAll(positions[i]+Vector3.up,Vector3.down,3,~0,QueryTriggerInteraction.Ignore)
                .Where(h=>h.collider.transform==ship.transform && h.normal.y>.7f).OrderBy(h=>h.distance).ToArray();
            if(hits.Length==0 || Vector3.Distance(hits[0].point,positions[i])>.2f)throw new InvalidOperationException("Ship floor changed near "+positions[i]);
            var hit=hits[0];var marker=new GameObject((49+i)+" "+parts[i]);marker.transform.SetParent(group,false);
            marker.transform.SetPositionAndRotation(hit.point,Quaternion.FromToRotation(Vector3.up,hit.normal));
            var point=marker.AddComponent<SalvageSpawnPoint>();point.part=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Parts/"+parts[i]+".prefab");
            point.progressionTier=6;point.estimatedLoopSeconds=300;point.orderRoute=false;
            point.routeNotes="Optional cache on the internal cargo floor of Cube.064. Open the ship gates with Button_Ship and the manipulator. Existing ship MeshCollider is used; no BoxCollider is added. Not a mandatory order route.";
        }
    }
    [MenuItem("Tools/Moon Game/Salvage/Add Ship Interior Spawns")]
    public static void Prepare()
    {
        var root=PrefabUtility.LoadPrefabContents(SalvageLayoutSetup.PrefabPath);
        try{AddTo(root);PrefabUtility.SaveAsPrefabAsset(root,SalvageLayoutSetup.PrefabPath);AssetDatabase.SaveAssets();}
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    public static void PrepareAndExit()
    {try{UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Operator.unity");Prepare();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
