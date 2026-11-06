using MoonThemedGame.Repair;
using System.Collections.Generic;
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

        const int MaxVolumes=16;
        static readonly List<RepairBayFogVolume> volumes=new List<RepairBayFogVolume>();
        static readonly Vector4[] mins=new Vector4[MaxVolumes],maxs=new Vector4[MaxVolumes],sizes=new Vector4[MaxVolumes],tints=new Vector4[MaxVolumes];
        static readonly Matrix4x4[] matrices=new Matrix4x4[MaxVolumes];

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
            if(!volumes.Contains(this))volumes.Add(this);
            PublishGlobals();
        }
        public static void PublishGlobals()
        {
            int count=0;
            for(int i=volumes.Count-1;i>=0;i--)if(!volumes[i])volumes.RemoveAt(i);
            foreach(var volume in volumes)
            {
                if(!volume.isActiveAndEnabled || volume.fogDensity<=0)continue;
                var size=Abs(volume.transform.lossyScale);
                if(Mathf.Min(size.x,Mathf.Min(size.y,size.z))<0.0001f)continue;
                var bounds=volume.WorldBounds;var min=bounds.min;var max=bounds.max;
                mins[count]=new Vector4(min.x,min.y,min.z,volume.fogDensity);
                maxs[count]=new Vector4(max.x,max.y,max.z,Mathf.Max(0.01f,volume.edgeFade));
                matrices[count]=volume.transform.worldToLocalMatrix;sizes[count]=size;tints[count]=volume.fogColor;
                if(++count==MaxVolumes)break;
            }
            Shader.SetGlobalInt("_RepairBayFogCount",count);
            Shader.SetGlobalVectorArray("_RepairBayFogMins",mins);
            Shader.SetGlobalVectorArray("_RepairBayFogMaxs",maxs);
            Shader.SetGlobalVectorArray("_RepairBayFogSizes",sizes);
            Shader.SetGlobalVectorArray("_RepairBayFogTints",tints);
            Shader.SetGlobalMatrixArray("_RepairBayFogMatrices",matrices);
            // Keep the legacy inspection values valid for existing editor tools.
            Shader.SetGlobalVector("_RepairBayFogMin",count>0?mins[0]:Vector4.zero);
            if(count>0)
            {
                Shader.SetGlobalVector("_RepairBayFogMax",maxs[0]);Shader.SetGlobalVector("_RepairBayFogSize",sizes[0]);
                Shader.SetGlobalMatrix("_RepairBayFogWorldToLocal",matrices[0]);Shader.SetGlobalVector("_RepairBayFogTint",tints[0]);
            }
        }
        void OnDisable(){volumes.Remove(this);PublishGlobals();}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetGlobals(){volumes.Clear();Shader.SetGlobalInt("_RepairBayFogCount",0);Shader.SetGlobalVector("_RepairBayFogMin",Vector4.zero);}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RestoreGlobals()
        {
            foreach(var volume in FindObjectsByType<RepairBayFogVolume>(FindObjectsSortMode.None))
                if(volume.isActiveAndEnabled && !volumes.Contains(volume))volumes.Add(volume);
            PublishGlobals();
        }
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
