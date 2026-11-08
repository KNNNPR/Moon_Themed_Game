using MoonThemedGame.Scavenger;
using UnityEngine;

namespace MoonThemedGame.Audio
{
    public static class RoverSurfaceAudio
    {
        public static Sound Kind(RaycastHit hit)
        {
            if(!hit.collider)return Sound.MoonRide;
            if(hit.collider.GetComponentInParent<LocationBridge>())return Sound.MetalRide;
            var selected=MaterialAt(hit);
            string name=selected?selected.name.ToLowerInvariant():"";
            if(name.Contains("concrete")||name.Contains("бетон"))return Sound.ConcreteRide;
            if(name.Contains("metal")||name.Contains("cargo")||name.Contains("steel")||name.Contains("iron"))return Sound.MetalRide;
            return Sound.MoonRide;
        }
        public static bool LooseGround(RaycastHit hit)
        {
            if(!hit.collider||Kind(hit)!=Sound.MoonRide)return false;
            if(hit.collider is TerrainCollider)return true;
            var material=MaterialAt(hit);
            string name=(hit.collider.name+" "+(material?material.name:"")).ToLowerInvariant();
            return name.Contains("land")||name.Contains("terrain")||name.Contains("ground")||name.Contains("dirt")||
                name.Contains("moon")||name.Contains("regolith")||name.Contains("rock")||name.Contains("sand")||name.Contains("грунт");
        }
        static Material MaterialAt(RaycastHit hit)
        {
            var renderer=hit.collider.GetComponent<Renderer>();if(!renderer)renderer=hit.collider.GetComponentInParent<Renderer>();
            if(!renderer)return null;
            var materials=renderer.sharedMaterials;if(materials.Length==0)return null;
            int material=0;
            // Physics triangle indices use the original mesh order. Index counts
            // remain available even when the imported LAND mesh is not readable.
            if(hit.collider is MeshCollider collider&&collider.sharedMesh&&hit.triangleIndex>=0)
            {
                long index=(long)hit.triangleIndex*3;
                for(int sub=0;sub<collider.sharedMesh.subMeshCount;sub++)
                {
                    long count=collider.sharedMesh.GetIndexCount(sub);
                    if(index<count){material=sub;break;}index-=count;
                }
            }
            return materials[Mathf.Min(material,materials.Length-1)];
        }
    }
}
