using System;
using System.Collections.Generic;
using System.Linq;
using MoonThemedGame.Scavenger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class SalvageLayoutSetup
{
    public const string PrefabPath="Assets/Resources/SalvageSpawnLayout.prefab";
    static SalvageLayoutSetup()
    {
        if(Application.isBatchMode)return;
        EditorApplication.delayCall+=Install;
        EditorSceneManager.sceneOpened+=(scene,mode)=>EditorApplication.delayCall+=Install;
    }
    [MenuItem("Tools/Moon Game/Salvage/Install Balanced Spawn Layout")]
    public static void Install()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().name!="Operator")return;
        if(Object.FindObjectsByType<SalvageSpawnLayout>(FindObjectsInactive.Include,FindObjectsSortMode.None).Any(l=>l.gameObject.scene==SceneManager.GetActiveScene()))return;
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);if(!prefab)return;
        var root=(GameObject)PrefabUtility.InstantiatePrefab(prefab,SceneManager.GetActiveScene());root.name="Salvage Spawn Points";
        Undo.RegisterCreatedObjectUndo(root,"Install balanced salvage layout");EditorSceneManager.MarkSceneDirty(root.scene);
    }
    struct Spot
    {
        public string part;public Vector3 point;
        public Spot(string part,float x,float y,float z){this.part=part;point=new Vector3(x,y,z);}
    }
    static Spot P(string part,float x,float y,float z)=>new Spot(part,x,y,z);
    [MenuItem("Tools/Moon Game/Salvage/Rebuild Default Spawn Prefab")]
    public static void Build()
    {
        if(SceneManager.GetActiveScene().name!="Operator")throw new InvalidOperationException("Open Operator before rebuilding the layout.");
        var root=new GameObject("Salvage Spawn Points");
        try
        {
            root.AddComponent<SalvageSpawnLayout>().balanceNotes=SalvageVariationSetup.Notes;
            int sequence=0;var errors=new List<string>();
            void Group(string name,string notes,params Spot[] spots)
            {
                var group=new GameObject(name).transform;group.SetParent(root.transform,false);
                foreach(var spot in spots)
                {
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Parts/"+spot.part+".prefab");if(!prefab)throw new InvalidOperationException("Missing part "+spot.part);
                    var position=spot.point;var normal=Vector3.up;
                    var hits=Physics.RaycastAll(position+Vector3.up*2,Vector3.down,4,~0,QueryTriggerInteraction.Ignore)
                        .Where(h=>h.normal.y>.7f && !h.collider.GetComponentInParent<ScavengerDrive>() && !h.collider.GetComponentInParent<FirstPersonController>()).OrderBy(h=>h.distance).ToArray();
                    if(hits.Length==0 || Mathf.Abs(hits[0].point.y-position.y)>.8f){errors.Add("No stationary ground for "+name+" "+position);continue;}
                    position=hits[0].point;normal=hits[0].normal;
                    var marker=new GameObject((++sequence).ToString("00")+" "+spot.part);marker.transform.SetParent(group,false);marker.transform.SetPositionAndRotation(position,Quaternion.FromToRotation(Vector3.up,normal));
                    var point=marker.AddComponent<SalvageSpawnPoint>();point.part=prefab;point.routeNotes=notes;
                }
            }
            Group("01 STARTER - access road", "No shortcuts. Closed-gate base route 41-61m each way. First-order wheels and battery, plus headlights. First collection should retain a large battery reserve.",
                P("Wheel",-9.3f,.69f,-53.5f),P("Wheel",-.3f,.69f,-53),P("Battery",-1.4f,.69f,-57.9f),P("Headlights",-3.3f,.69f,-72.8f));
            Group("02 STARTER - roadside wrecks", "No shortcuts. Base route 93-115m each way. Window and body remain available before any unlock. Can split collection into shorter sorties.",
                P("Window_Robot",-1.1f,1.79f,-104.1f),P("Back_Body_Robot",10.5f,1.89f,-107.5f),P("Wheel",20.7f,1.79f,-101.4f),P("Battery",3.8f,1.89f,-124.9f));
            Group("03 STARTER - northern junction", "No shortcuts. Base route 126-143m each way. Extra wheels cover the third initial order. Normal weather; leave time to return, or collect only part of the cache.",
                P("Wheel",29.7f,1.89f,-128.8f),P("Wheel",43.2f,1.89f,-130.3f),P("Window_Robot",47.7f,1.89f,-127.3f),P("Back_Body_Robot",42.6f,2.19f,-140.3f));
            Group("04 WEST - relay detour", "Closed-gate base route 192-214m each way. Capacity or efficiency upgrade recommended for a full four-part detour. Avoid a full cache run during radiation on stock power.",
                P("Wheel",-42.5f,1.89f,-151.4f),P("Battery",-46.2f,1.89f,-156.4f),P("Headlights",-59.7f,2.19f,-144.8f),P("Back_Body_Robot",-42.8f,1.89f,-146.4f));
            Group("05 WEST - outpost approach", "Base route 269-324m. Collect while travelling toward the second charger, not as an unupgraded return trip. Open the outpost gate with the arm before relying on that charger.",
                P("Wheel",-73.7f,-2.61f,-250.5f),P("Battery",-85.6f,-3.61f,-267.6f),P("Window_Robot",-128, .99f,-266.2f),P("Headlights",-131.7f,1.39f,-272.3f));
            Group("06 OUTPOST - charging yard", "Behind the outpost gate. 17-38m from charge station 2. Recharge here; cargo still returns to the main base for unloading.",
                P("Wheel",-169.9f,1.89f,-313.3f),P("Battery",-166.7f,1.89f,-318.6f),P("Headlights",-167.4f,1.79f,-325.9f),P("Back_Body_Robot",-172.6f,.99f,-340.8f));
            Group("07 OUTPOST - southern rubble", "Behind the outpost gate. 46-54m from charge station 2. Four nearby pieces support short loops from that charger, rather than a single long trip from the base.",
                P("Wheel",-176.7f,-1.81f,-348),P("Battery",-163,-2.61f,-358.5f),P("Window_Robot",-128,-.11f,-354.3f),P("Back_Body_Robot",-119,-2.11f,-350));
            Group("08 BRIDGE - far-side cache", "Later route after the bridge is lowered. 164-184m from charge station 2. Take capacity/efficiency upgrades and recharge before crossing. No parts placed on the moving bridge deck.",
                P("Wheel",-77.5f,-1.61f,-448.3f),P("Battery",-71.9f,-2.61f,-452.4f),P("Window_Robot",-80.8f,.39f,-460.5f),P("Back_Body_Robot",-86.6f,.99f,-466.4f));
            Group("09 EAST - service road", "Long progression route: 384-440m from base. Capacity and efficiency upgrades are required for a comfortable return. The first three groups contain the same required part types earlier.",
                P("Wheel",161.9f,-8.91f,-347.9f),P("Battery",173.1f,-8.81f,-378.2f),P("Headlights",171,-8.81f,-384.2f),P("Back_Body_Robot",186.7f,-9.01f,-397.2f));
            Group("10 EAST - gated depot", "Later route behind the east depot gate. 459-490m from base. Larger battery and power-regulator upgrades turn this dense cache into a viable haul.",
                P("Wheel",174,-12.51f,-420.8f),P("Battery",180.6f,-13.21f,-424.5f),P("Headlights",174.8f,-13.71f,-442.8f),P("Back_Body_Robot",174,-12.61f,-451.3f));
            Group("11 FAR SOUTH - lunar basin", "Beyond the lowered bridge, 218-251m of driving from charge station 2 and 578-611m from the base. Upgrade battery and regulator, recharge before departing. Fixed reachable ground, never the moving bridge or cliff tops.",
                P("Wheel",-78.5f,3.7f,-502.6f),P("Battery",-65.5f,1.7f,-506.5f),P("Window_Robot",-54.5f,1.8f,-514.7f),P("Headlights",-22.8f,-.9f,-517.6f));
            Group("12 FAR EAST - perimeter haul", "Late-game perimeter route, 456-536m from the base. Arm/gate unlock and improved capacity/efficiency; gather on the outward route, not on stock battery. Spread between the depot exit and the far eastern road.",
                P("Back_Body_Robot",184.9f,-11.6f,-417.5f),P("Battery",174.4f,-9.4f,-409.8f),P("Wheel",142.6f,-7.6f,-507.6f),P("Headlights",147.6f,-5.2f,-488));
            if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
            SalvageShipSpawnSetup.AddTo(root);
            SalvageVariationSetup.AddTo(root);
            SalvageOrderProgressionSetup.Annotate(root);
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);AssetDatabase.SaveAssets();Debug.Log("Balanced salvage layout: "+root.GetComponentsInChildren<SalvageSpawnPoint>().Length+" fixed points.");
        }
        finally{Object.DestroyImmediate(root);}
    }
}
public sealed class SalvageLayoutImport : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported,string[] deleted,string[] moved,string[] oldPaths)
    {
        if(!Application.isBatchMode && Array.IndexOf(imported,SalvageLayoutSetup.PrefabPath)>=0)EditorApplication.delayCall+=SalvageLayoutSetup.Install;
    }
}
