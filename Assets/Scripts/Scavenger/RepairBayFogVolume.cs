using MoonThemedGame.Repair;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Scavenger
{
    [ExecuteAlways,DisallowMultipleComponent,AddComponentMenu("Moon Game/Repair Bay Fog Volume")]
    public sealed class RepairBayFogVolume : MonoBehaviour
    {
        public const string ObjectName="Repair Bay Fog";
        [Min(0)] public float fogDensity=4;
        public Color fogColor=new Color(0.16f,0.17f,0.16f,1);
        [Min(0.01f)] public float edgeFade=0.45f;

        void OnEnable()=>Refresh();
        void Update()=>Refresh();
        void OnValidate()=>Refresh();
        public Bounds WorldBounds
        {
            get
            {
                var m=transform.localToWorldMatrix;
                var extent=Abs(m.MultiplyVector(Vector3.right*0.5f))+
                    Abs(m.MultiplyVector(Vector3.up*0.5f))+Abs(m.MultiplyVector(Vector3.forward*0.5f));
                return new Bounds(transform.position,extent*2);
            }
        }
        static Vector3 Abs(Vector3 v)=>new Vector3(Mathf.Abs(v.x),Mathf.Abs(v.y),Mathf.Abs(v.z));
        public void Refresh()
        {
            if(!isActiveAndEnabled)return;
            var size=Abs(transform.lossyScale);
            if(Mathf.Min(size.x,Mathf.Min(size.y,size.z))<0.0001f){Clear();return;}
            var bounds=WorldBounds;var min=bounds.min;var max=bounds.max;
            Shader.SetGlobalVector("_RepairBayFogMin",new Vector4(min.x,min.y,min.z,fogDensity));
            Shader.SetGlobalVector("_RepairBayFogMax",new Vector4(max.x,max.y,max.z,Mathf.Max(0.01f,edgeFade)));
            Shader.SetGlobalMatrix("_RepairBayFogWorldToLocal",transform.worldToLocalMatrix);
            Shader.SetGlobalVector("_RepairBayFogSize",size);
            Shader.SetGlobalColor("_RepairBayFogTint",fogColor);
        }
        static void Clear()=>Shader.SetGlobalVector("_RepairBayFogMin",Vector4.zero);
        void OnDisable()=>Clear();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetGlobals()=>Clear();
        void OnDrawGizmosSelected()
        {
            var previous=Gizmos.matrix;Gizmos.matrix=transform.localToWorldMatrix;
            Gizmos.color=new Color(0.5f,0.8f,0.85f,0.8f);Gizmos.DrawWireCube(Vector3.zero,Vector3.one);
            Gizmos.matrix=previous;
        }
        public static RepairBayFogVolume EnsureFor(RepairRobotDropper delivery)
        {
            if(!delivery || !delivery.gameObject.scene.isLoaded)return null;
            // Retain the artist's position, size and disabled state on every subsequent setup.
            foreach(var root in delivery.gameObject.scene.GetRootGameObjects())
            {
                var existing=root.GetComponentInChildren<RepairBayFogVolume>(true);
                if(existing)return existing;
            }
            Renderer enclosure=null;
            foreach(var root in delivery.gameObject.scene.GetRootGameObjects())
                foreach(var r in root.GetComponentsInChildren<Renderer>(true))
                    if(r.name=="Tower"){enclosure=r;break;}
            var anchor=delivery.landingPoint?delivery.landingPoint:delivery.transform;
            var bounds=enclosure?enclosure.bounds:new Bounds(anchor.position+Vector3.up*(delivery.dropHeight+3),new Vector3(8,6,8));
            var inset=new Vector3(Mathf.Min(2,bounds.size.x*0.15f),0,Mathf.Min(2,bounds.size.z*0.15f));
            var min=bounds.min+inset-Vector3.up*0.75f;var max=bounds.max-inset;
            var host=new GameObject(ObjectName);
            SceneManager.MoveGameObjectToScene(host,delivery.gameObject.scene);
            host.transform.position=(min+max)*0.5f;host.transform.localScale=max-min;
            var volume=host.AddComponent<RepairBayFogVolume>();
            var legacy=delivery.GetComponent<ScavengerBayBlackout>();
            if(legacy){volume.fogDensity=legacy.fogDensity;volume.fogColor=legacy.fogColor;volume.edgeFade=legacy.edgeFade;}
            volume.Refresh();return volume;
        }
    }
}
