using System;
using MoonThemedGame.Scavenger;
using UnityEditor;
using UnityEngine;

public static class SalvageOrderProgressionSetup
{
    public static void Annotate(GameObject root)
    {
        foreach(var point in root.GetComponentsInChildren<SalvageSpawnPoint>(true))
        {
            var group=point.transform.parent;
            if(!group || group.name.Length<2 || !int.TryParse(group.name.Substring(0,2),out int number))continue;
            if(number>=14 && number<=19)
            {
                point.progressionTier=number-14;point.estimatedLoopSeconds=new[]{48,68,80,112,160,210}[number-14];
                point.orderRoute=true;point.requiresManipulator=false;continue;
            }
            point.progressionTier=number switch{1=>0,2=>1,3=>2,4=>3,5=>4,6 or 7 or 9=>5,8 or 10=>6,_=>7};
            point.estimatedLoopSeconds=number switch{1=>42,2=>68,3=>79,4=>108,5=>159,6=>199,7=>211,8=>254,9=>208,10=>230,11=>290,13=>300,_=>260};
            point.orderRoute=number==1 || number==2 || number==3 || number==4 || number==5 || number==9;
            point.requiresManipulator=!point.orderRoute;
            point.minimumOrderTier=number==6 || number==7?3:5;
            point.accessButton=number==6 || number==7?"Cylinder.046":number==8 || number==11?"Cylinder.049":number==10 || number==12?"Cylinder.045":number==13?"button_ship":"";
            point.shipInterior=number==13;
            point.chargerRouteSeconds=number==6 || number==7?114:0;
            if(point.shipInterior)point.minimumOrderTier=0;
        }
    }
    [MenuItem("Tools/Moon Game/Salvage/Prepare Order Progression")]
    public static void Prepare()
    {
        var root=PrefabUtility.LoadPrefabContents(SalvageLayoutSetup.PrefabPath);
        try{Annotate(root);PrefabUtility.SaveAsPrefabAsset(root,SalvageLayoutSetup.PrefabPath);AssetDatabase.SaveAssets();}
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    public static void PrepareAndExit()
    {try{Prepare();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
