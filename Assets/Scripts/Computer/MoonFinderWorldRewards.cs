using System;
using MoonThemedGame.Interactions;
using MoonThemedGame.Scavenger;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Computer
{
    // Its scene position is the giant apple's spawn marker; raycast down finds the ground.
    public sealed class MoonFinderWorldRewards : MonoBehaviour
    {
        public MoonFinderContent content;
        public string garageObjectName="Garage_Door";
        public DoorInteractable garage;
        [Min(1)] public float appleHeight=12;
        public bool HasGarageKeys {get;private set;}
        public GameObject GiantApple {get;private set;}
        public static MoonFinderWorldRewards Ensure(Scene scene)
        {
            foreach(var root in scene.GetRootGameObjects())
            {
                var existing=root.GetComponentInChildren<MoonFinderWorldRewards>(true);
                if(existing)return existing;
            }
            var host=new GameObject("MoonFinder Rewards / Apple Spawn");
            SceneManager.MoveGameObjectToScene(host,scene);host.transform.position=new Vector3(60,20,-95);
            return host.AddComponent<MoonFinderWorldRewards>();
        }
        void Awake()
        {
            if(!content)content=Resources.Load<MoonFinderContent>("MoonFinderContent");
            if(garage)garage.locked=!HasGarageKeys;
            BindGarage();
        }
        public void BindGarage()
        {
            if(garage)return;
            foreach(var root in gameObject.scene.GetRootGameObjects())
                foreach(var candidate in root.GetComponentsInChildren<Transform>(true))
                {
                    if(!string.Equals(candidate.name,garageObjectName,StringComparison.OrdinalIgnoreCase))continue;
                    bool exists=candidate.TryGetComponent(out DoorInteractable door);
                    garage=exists?door:candidate.gameObject.AddComponent<DoorInteractable>();
                    if(!exists)
                    {
                        var renderers=candidate.GetComponentsInChildren<Renderer>();
                        if(renderers.Length>0)
                        {
                            var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                            var offset=Vector3.up*(bounds.size.y+0.3f);
                            garage.openOffset=candidate.parent?candidate.parent.InverseTransformVector(offset):offset;
                        }
                        garage.useWorldSlideDirection=false;garage.slideLeftAlongDoor=false;garage.duration=1.4f;
                    }
                    garage.locked=!HasGarageKeys;garage.SetHighlighted(false);return;
                }
        }
        public void Grant(MoonFinderReward reward)
        {
            switch(reward)
            {
                case MoonFinderReward.GarageKeys: HasGarageKeys=true;BindGarage();if(garage)garage.locked=false;break;
                case MoonFinderReward.CerberusTruce: SatelliteNetwork.Instance.PacifyRival();break;
                case MoonFinderReward.GiantApple: SpawnApple();break;
            }
        }
        void SpawnApple()
        {
            if(GiantApple || !content || !content.appleModel)return;
            var source=content.appleModel.GetComponentInChildren<MeshFilter>();
            if(!source || !source.TryGetComponent(out MeshRenderer renderer))return;
            var position=ApplePosition();
            GiantApple=new GameObject("MoonFinder // Giant Apple");SceneManager.MoveGameObjectToScene(GiantApple,gameObject.scene);
            var visual=new GameObject("Apple model");visual.transform.SetParent(GiantApple.transform,false);
            visual.transform.localRotation=content.appleModel.transform.localRotation;
            visual.AddComponent<MeshFilter>().sharedMesh=source.sharedMesh;
            var display=visual.AddComponent<MeshRenderer>();display.sharedMaterials=renderer.sharedMaterials;
            visual.transform.localScale=Vector3.one*(appleHeight/Mathf.Max(0.001f,display.bounds.size.y));
            var bounds=display.bounds;
            GiantApple.transform.position=position-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
            bounds=display.bounds;
            var collider=GiantApple.AddComponent<BoxCollider>();collider.center=GiantApple.transform.InverseTransformPoint(bounds.center);collider.size=bounds.size;
        }
        Vector3 ApplePosition()
        {
            Vector3 fallback=transform.position;
            // Prefer open terrain near the marker, so the apple does not land on a building's roof.
            for(int ring=0;ring<5;ring++)
                for(int sample=0;sample<(ring==0?1:12);sample++)
                {
                    float angle=sample*Mathf.PI/6;
                    Vector3 candidate=transform.position+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*ring*appleHeight*1.5f;
                    var hits=Physics.RaycastAll(candidate+Vector3.up*500,Vector3.down,2000,~0,QueryTriggerInteraction.Ignore);
                    Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
                    foreach(var hit in hits)
                    {
                        if(hit.normal.y<0.6f)continue;
                        if(ring==0)fallback=hit.point;
                        if(!string.Equals(hit.collider.name,"LAND",StringComparison.OrdinalIgnoreCase))continue;
                        bool occupied=false;
                        foreach(var other in Physics.OverlapBox(hit.point+Vector3.up*(appleHeight*0.5f+0.15f),new Vector3(appleHeight*0.5f,appleHeight*0.49f,appleHeight*0.5f),Quaternion.identity,~0,QueryTriggerInteraction.Ignore))
                            if(other!=hit.collider){occupied=true;break;}
                        if(!occupied)return hit.point;
                    }
                }
            return fallback;
        }
    }
}
