using MoonThemedGame.Repair;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Interactions
{
    // Place debug parts on a real support near the operator, preserving their authored pose.
    public static class DeveloperPartSpawner
    {
        public static bool Spawn(GameObject prefab)
        {
            var player=Object.FindFirstObjectByType<FirstPersonController>();if(!player || !prefab)return false;
            var instance=Object.Instantiate(prefab);instance.name="Part / "+prefab.name;
            instance.transform.SetPositionAndRotation(Vector3.one*10000,Quaternion.Euler(0,player.transform.eulerAngles.y,0)*prefab.transform.rotation);
            var colliders=instance.GetComponentsInChildren<Collider>(true);var enabled=new bool[colliders.Length];
            for(int i=0;i<colliders.Length;i++){enabled[i]=colliders[i].enabled;colliders[i].enabled=false;}
            var renderers=instance.GetComponentsInChildren<Renderer>();
            if(renderers.Length==0){Object.Destroy(instance);return false;}
            var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            Physics.SyncTransforms();
            var capsule=player.GetComponent<CharacterController>();var origin=capsule?capsule.bounds.center:player.transform.position+Vector3.up;
            float playerRadius=capsule?Mathf.Max(capsule.bounds.extents.x,capsule.bounds.extents.z):0.6f;
            float reach=playerRadius+Mathf.Max(bounds.extents.x,bounds.extents.z)+0.5f;
            var forward=Vector3.ProjectOnPlane(player.transform.forward,Vector3.up).normalized;
            if(forward.sqrMagnitude<0.1f)forward=Vector3.forward;
            foreach(float ring in new[]{1f,1.5f,2f})
                foreach(float angle in new[]{0f,-35,35,-70,70,-110,110,180})
                {
                    var point=origin+Quaternion.AngleAxis(angle,Vector3.up)*forward*reach*ring;
                    var hits=Physics.RaycastAll(point+Vector3.up*2,Vector3.down,12,~0,QueryTriggerInteraction.Ignore);
                    float nearest=float.PositiveInfinity;Vector3 ground=default;
                    foreach(var hit in hits)
                        if(hit.normal.y>0.6f && !hit.transform.IsChildOf(player.transform) && hit.distance<nearest)
                        {nearest=hit.distance;ground=hit.point;}
                    if(float.IsPositiveInfinity(nearest))continue;
                    var center=ground+Vector3.up*(bounds.extents.y+0.08f);
                    bool blocked=false;
                    var line=center-origin;
                    foreach(var hit in Physics.RaycastAll(origin,line.normalized,line.magnitude,~0,QueryTriggerInteraction.Ignore))
                        if(!hit.transform.IsChildOf(player.transform) && !hit.transform.IsChildOf(instance.transform)){blocked=true;break;}
                    if(blocked)continue;
                    if(Physics.CheckBox(center,bounds.extents*.98f,Quaternion.identity,~0,QueryTriggerInteraction.Ignore))continue;
                    instance.transform.position+=center-bounds.center;
                    SceneManager.MoveGameObjectToScene(instance,player.gameObject.scene);
                    for(int i=0;i<colliders.Length;i++)colliders[i].enabled=enabled[i];
                    var part=instance.AddComponent<RepairPartProp>();part.prefab=prefab;part.ReleaseFromDelivery(Vector3.zero);
                    return true;
                }
            Object.Destroy(instance);return false;
        }
    }
}
