using MoonThemedGame.Audio;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Repair
{
    [DisallowMultipleComponent]
    public sealed class ScrapTubeDelivery : MonoBehaviour
    {
        public Transform[] waypoints=Array.Empty<Transform>();
        [Min(0.1f)] public float transportSpeed=7;
        [Min(0.1f)] public float exitSpeed=0.8f;
        [Min(0)] public float partInterval=0.6f;
        readonly Queue<GameObject> pending=new Queue<GameObject>();
        readonly List<Vector3> samples=new List<Vector3>();
        readonly List<float> lengths=new List<float>();
        GameObject current,currentPrefab;
        Collider[] colliders;
        bool[] colliderStates;
        float distance,total,cooldown;
        public int PendingCount=>pending.Count+(current?1:0);
        public int DeliveredCount {get;private set;}
        internal int CountPrefab(GameObject prefab)
        {
            int count=current && currentPrefab==prefab?1:0;
            foreach(var queued in pending)if(queued==prefab)count++;
            return count;
        }
        public GameObject InTransit=>current;
        public Transform Outlet=>waypoints.Length>0?waypoints[waypoints.Length-1]:null;
        public bool HasRoute
        {
            get{if(waypoints.Length<2)return false;foreach(var point in waypoints)if(!point)return false;return true;}
        }
        public bool Enqueue(GameObject prefab)
        {
            if(!Application.isPlaying || !isActiveAndEnabled || !prefab || !HasRoute)return false;
            pending.Enqueue(prefab);return true;
        }
        void Update()=>Advance(Time.deltaTime);
        public void Advance(float dt)
        {
            if(!Application.isPlaying || !isActiveAndEnabled || dt<=0 || !HasRoute)return;
            if(!current)
            {
                cooldown=Mathf.Max(0,cooldown-dt);
                if(cooldown>0 || pending.Count==0)return;
                Begin(pending.Dequeue());
                return;
            }
            float speed=Mathf.Lerp(exitSpeed,transportSpeed,Mathf.SmoothStep(0,1,(total-distance)/2));
            distance=Mathf.Min(total,distance+speed*dt);
            Pose(distance);
            if(distance<total)return;
            var released=current;current=null;
            released.name="Part / "+currentPrefab.name;
            for(int i=0;i<colliders.Length;i++)if(colliders[i])colliders[i].enabled=colliderStates[i];
            var part=released.AddComponent<RepairPartProp>();part.prefab=currentPrefab;
            part.ReleaseFromDelivery(Outlet.forward*exitSpeed+Vector3.down*0.15f);
            GameAudio.At(Sound.Pipe,Outlet.position);DeliveredCount++;cooldown=partInterval;
        }
        void Begin(GameObject prefab)
        {
            if(!prefab || !HasRoute)return;
            currentPrefab=prefab;
            current=new GameObject("Tube delivery / "+prefab.name);
            SceneManager.MoveGameObjectToScene(current,gameObject.scene);
            var model=Instantiate(prefab,current.transform,false);
            model.name=prefab.name;model.transform.localPosition=Vector3.zero;
            foreach(var body in model.GetComponentsInChildren<Rigidbody>(true))Destroy(body);
            colliders=model.GetComponentsInChildren<Collider>(true);colliderStates=new bool[colliders.Length];
            for(int i=0;i<colliders.Length;i++){colliderStates[i]=colliders[i].enabled;colliders[i].enabled=false;}
            var bounds=VisualBounds(current);var size=bounds.size;
            var longAxis=size.x>=size.y && size.x>=size.z?Vector3.right:size.y>=size.z?Vector3.up:Vector3.forward;
            model.transform.localRotation=Quaternion.FromToRotation(longAxis,Vector3.forward)*model.transform.localRotation;
            bounds=VisualBounds(current);model.transform.localPosition-=bounds.center;
            bounds=VisualBounds(current);float halfLength=bounds.extents.z;
            BuildPath(halfLength+0.15f);
            // Start fully inside the hidden feed, with no visible pop at the external inlet.
            distance=Mathf.Min(halfLength+0.2f,total*0.25f);Pose(distance);
        }
        static Bounds VisualBounds(GameObject target)
        {
            var rs=target.GetComponentsInChildren<Renderer>();
            var bounds=rs.Length>0?rs[0].bounds:new Bounds(target.transform.position,Vector3.one*.1f);
            foreach(var renderer in rs)bounds.Encapsulate(renderer.bounds);return bounds;
        }
        void BuildPath(float clearance)
        {
            samples.Clear();lengths.Clear();total=0;
            for(int segment=0;segment<waypoints.Length-1;segment++)
            {
                var b=waypoints[segment].position;var c=waypoints[segment+1].position;
                var a=segment>0?waypoints[segment-1].position:b+(b-c);
                var d=segment+2<waypoints.Length?waypoints[segment+2].position:c+Outlet.forward*Vector3.Distance(b,c);
                for(int step=segment==0?0:1;step<=16;step++)
                {
                    float t=step/16f,t2=t*t,t3=t2*t;
                    AddSample(0.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t2+(-a+3*b-3*c+d)*t3));
                }
            }
            AddSample(Outlet.position+Outlet.forward*clearance);
        }
        void AddSample(Vector3 world)
        {
            if(samples.Count>0)total+=Vector3.Distance(transform.TransformPoint(samples[samples.Count-1]),world);
            samples.Add(transform.InverseTransformPoint(world));lengths.Add(total);
        }
        void Pose(float along)
        {
            int index=1;while(index<lengths.Count-1 && lengths[index]<along)index++;
            float blend=Mathf.InverseLerp(lengths[index-1],lengths[index],along);
            var a=transform.TransformPoint(samples[index-1]);var b=transform.TransformPoint(samples[index]);
            var forward=(b-a).normalized;
            current.transform.SetPositionAndRotation(Vector3.Lerp(a,b,blend),Quaternion.LookRotation(forward,transform.TransformDirection(Vector3.up)));
        }
        public void ConfigureDefaultRoute()
        {
            if(HasRoute)return;
            // Centreline of the authored Pale_Horse pipe, in its own mesh coordinates.
            var positions=new[]{
                new Vector3(.07009197f,.00052367f,.03887662f),new Vector3(.02980617f,.00052367f,.04141581f),
                new Vector3(.01923409f,.00052367f,.04039532f),new Vector3(.01140457f,.00052367f,.02838001f),
                new Vector3(.01140457f,.00052368f,-.00249739f),new Vector3(.01101658f,.00052368f,-.01162229f),
                new Vector3(.00547098f,.00052368f,-.01887909f),new Vector3(-.00323162f,.00052368f,-.02164999f),
                new Vector3(-.02365162f,.00406808f,-.02164999f),new Vector3(-.03370142f,-.00238892f,-.02164999f)};
            var route=new GameObject("Scrap tube route").transform;route.SetParent(transform,false);
            waypoints=new Transform[positions.Length];
            for(int i=0;i<positions.Length;i++)
            {
                var point=new GameObject(i==0?"Scrap inlet":i==positions.Length-1?"Scrap outlet":"Pipe bend "+i).transform;
                point.SetParent(route,false);point.localPosition=positions[i];waypoints[i]=point;
            }
            Outlet.localRotation=Quaternion.LookRotation(new Vector3(-.766044f,-.6427879f,0),Vector3.forward);
        }
        public static ScrapTubeDelivery EnsureInScene(Scene scene)
        {
            if(!scene.IsValid() || !scene.isLoaded)return null;
            foreach(var root in scene.GetRootGameObjects())
            {
                var existing=root.GetComponentInChildren<ScrapTubeDelivery>(true);
                if(existing){existing.ConfigureDefaultRoute();return existing;}
            }
            foreach(var root in scene.GetRootGameObjects())
                foreach(var t in root.GetComponentsInChildren<Transform>(true))
                    if(t.name.Equals("Tube_For_Scrap",StringComparison.OrdinalIgnoreCase))
                    {
                        var delivery=t.gameObject.AddComponent<ScrapTubeDelivery>();delivery.ConfigureDefaultRoute();return delivery;
                    }
            return null;
        }
        void OnDrawGizmosSelected()
        {
            if(!HasRoute)return;Gizmos.color=new Color(.8f,.7f,.2f);
            for(int i=0;i<waypoints.Length;i++)
            {Gizmos.DrawWireSphere(waypoints[i].position,.1f);if(i>0)Gizmos.DrawLine(waypoints[i-1].position,waypoints[i].position);}
            Gizmos.DrawRay(Outlet.position,Outlet.forward);
        }
        void OnDestroy(){if(current)Destroy(current);}
    }
}
