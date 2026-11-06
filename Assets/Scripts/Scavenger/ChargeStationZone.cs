using System.Collections.Generic;
using MoonThemedGame.Computer;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace MoonThemedGame.Scavenger
{
    [DisallowMultipleComponent]
    public sealed class ChargeStationZone : MonoBehaviour
    {
        public Vector3 localCenter;
        public Vector3 localSize;
        static readonly List<ChargeStationZone> active=new List<ChargeStationZone>();
        public bool Contains(Vector3 point)
        {
            var p=transform.InverseTransformPoint(point)-localCenter;
            return Mathf.Abs(p.x)<=localSize.x*.5f && Mathf.Abs(p.y)<=localSize.y*.5f && Mathf.Abs(p.z)<=localSize.z*.5f;
        }
        public static bool AtStation(Vector3 point)
        {
            foreach(var zone in active)if(zone && zone.isActiveAndEnabled && zone.Contains(point))return true;
            return false;
        }
        void OnEnable(){if(!active.Contains(this))active.Add(this);}
        void OnDisable()=>active.Remove(this);
        public void Configure(Bounds worldBounds)
        {
            localCenter=transform.InverseTransformPoint(worldBounds.center);
            var matrix=transform.worldToLocalMatrix;
            localSize=Abs(matrix.MultiplyVector(Vector3.right*worldBounds.size.x))+Abs(matrix.MultiplyVector(Vector3.up*worldBounds.size.y))+Abs(matrix.MultiplyVector(Vector3.forward*worldBounds.size.z));
        }
        static Vector3 Abs(Vector3 v)=>new Vector3(Mathf.Abs(v.x),Mathf.Abs(v.y),Mathf.Abs(v.z));
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset()=>active.Clear();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Install()
        {
            SceneManager.sceneLoaded-=Loaded;SceneManager.sceneLoaded+=Loaded;InstallScene();
        }
        static void Loaded(Scene scene,LoadSceneMode mode)=>InstallScene();
        public static void InstallScene()
        {
            foreach(var zone in FindObjectsByType<ChargeStationZone>(FindObjectsSortMode.None))if(!active.Contains(zone))active.Add(zone);
            foreach(var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if(ChargeMonitorDisplay.Normalize(renderer.name)!="CHARGESTATION2" || renderer.GetComponent<ChargeStationZone>())continue;
                var zone=renderer.gameObject.AddComponent<ChargeStationZone>();
                var bounds=renderer.bounds;
                BoxCollider pad=null;
                foreach(var box in renderer.GetComponents<BoxCollider>())
                    if(box.bounds.size.y<Mathf.Max(box.bounds.size.x,box.bounds.size.z) && (!pad || box.bounds.center.y<pad.bounds.center.y))pad=box;
                float surface=bounds.min.y+.3f;
                if(pad){bounds=pad.bounds;surface=bounds.max.y;}
                zone.Configure(new Bounds(new Vector3(bounds.center.x,surface+1.8f,bounds.center.z),new Vector3(bounds.size.x*.92f,4f,bounds.size.z*.92f)));
            }
        }
        void OnDrawGizmosSelected()
        {
            var previous=Gizmos.matrix;Gizmos.matrix=transform.localToWorldMatrix;Gizmos.color=Color.green;Gizmos.DrawWireCube(localCenter,localSize);Gizmos.matrix=previous;
        }
    }
}
