using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.World
{
    public sealed class LandscapeCollisionSet : ScriptableObject
    {
        public Mesh[] objectMeshes = Array.Empty<Mesh>();
    }

    // The manifest identifies the imported scenery even when its scene roots are renamed.
    public static class LandscapeCollisions
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded-=OnSceneLoaded;
            SceneManager.sceneLoaded+=OnSceneLoaded;
            for(int i=0;i<SceneManager.sceneCount;i++)OnSceneLoaded(SceneManager.GetSceneAt(i),LoadSceneMode.Additive);
        }

        static void OnSceneLoaded(Scene scene,LoadSceneMode mode)
        {
            var set=Resources.Load<LandscapeCollisionSet>("LandscapeCollisionSet");
            if(set)Ensure(scene,set);
        }

        public static bool IsSurface(string name) => name.Equals("LAND",StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("LAND.",StringComparison.OrdinalIgnoreCase) || name.Equals("Terrain",StringComparison.OrdinalIgnoreCase);

        public static bool IsChargeBuilding(GameObject target) =>
            target.name.Equals("Cube.026", StringComparison.OrdinalIgnoreCase);

        public static bool UsesBox(MeshFilter filter)
        {
            if(!filter || !filter.sharedMesh || IsCargoShip(filter))return false;
            if(filter.sharedMesh.name.StartsWith("fence",StringComparison.OrdinalIgnoreCase))return true;
            for(var node=filter.transform;node;node=node.parent)
                if(node.name.StartsWith("fence",StringComparison.OrdinalIgnoreCase))return true;
            var renderer=filter.GetComponent<Renderer>();
            if(renderer)
                foreach(var material in renderer.sharedMaterials)
                    // Unity may append this suffix to a runtime material instance; case is significant.
                    if(material && (material.name=="cargo" || material.name=="cargo (Instance)"))return true;
            return false;
        }
        public static bool IsCargoShip(MeshFilter filter)
        {
            if(!filter)return false;
            bool Match(string name)
            {
                string key=name.Split('(')[0].Trim().Replace("_","").Replace(" ","");
                return key.Equals("Cube.064",StringComparison.OrdinalIgnoreCase) || key.Equals("CARGOSHIP",StringComparison.OrdinalIgnoreCase);
            }
            return Match(filter.name) || (filter.sharedMesh && Match(filter.sharedMesh.name));
        }

        public static int Ensure(Scene scene,LandscapeCollisionSet set,Func<GameObject,Type,Collider> add=null,Action<Collider> remove=null)
        {
            if(!scene.IsValid() || !scene.isLoaded || !set)return 0;
            var meshes=new HashSet<Mesh>(set.objectMeshes);
            int count=0;
            foreach(var root in scene.GetRootGameObjects())
                foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if(!filter.sharedMesh || IsSurface(filter.name) || IsSurface(filter.sharedMesh.name))continue;
                    if(IsCargoShip(filter))
                    {
                        foreach(var box in filter.GetComponents<BoxCollider>())
                        {
                            if(remove!=null)remove(box);
                            else if(Application.isPlaying){box.enabled=false;UnityEngine.Object.Destroy(box);}
                            else UnityEngine.Object.DestroyImmediate(box);
                            count++;
                        }
                        continue;
                    }
                    if(UsesBox(filter))
                    {
                        if(EnsureBox(filter,add,remove))count++;
                        continue;
                    }
                    if(IsChargeBuilding(filter.gameObject) || !meshes.Contains(filter.sharedMesh))continue;
                    // Respect existing colliders, including disabled/trigger and compound setups.
                    if(filter.GetComponentsInChildren<Collider>(true).Length>0 || HasParentCollision(filter.transform))continue;
                    var body=filter.GetComponentInParent<Rigidbody>();
                    if(body && !body.isKinematic)continue;
                    var collider=(MeshCollider)(add!=null?add(filter.gameObject,typeof(MeshCollider)):filter.gameObject.AddComponent<MeshCollider>());
                    collider.sharedMesh=filter.sharedMesh;
                    collider.convex=false;
                    count++;
                }
            return count;
        }

        static bool EnsureBox(MeshFilter filter,Func<GameObject,Type,Collider> add,Action<Collider> remove)
        {
            var colliders=filter.GetComponents<Collider>();
            var box=filter.GetComponent<BoxCollider>();
            bool changed=false;
            if(!box)
            {
                box=(BoxCollider)(add!=null?add(filter.gameObject,typeof(BoxCollider)):filter.gameObject.AddComponent<BoxCollider>());
                var bounds=filter.sharedMesh.bounds;
                box.center=bounds.center;
                box.size=Vector3.Max(bounds.size,Vector3.one*0.0001f);
                if(colliders.Length>0)
                {
                    var previous=colliders[0];
                    box.enabled=previous.enabled;
                    box.isTrigger=previous.isTrigger;
                    box.sharedMaterial=previous.sharedMaterial;
                    box.contactOffset=previous.contactOffset;
                    box.includeLayers=previous.includeLayers;
                    box.excludeLayers=previous.excludeLayers;
                    box.layerOverridePriority=previous.layerOverridePriority;
                    box.providesContacts=previous.providesContacts;
                    box.hasModifiableContacts=previous.hasModifiableContacts;
                }
                changed=true;
            }
            // The requested box replaces other shapes on this object; authored boxes keep their settings.
            foreach(var collider in colliders)
            {
                if(collider is BoxCollider)continue;
                if(remove!=null)remove(collider);
                else if(Application.isPlaying){collider.enabled=false;UnityEngine.Object.Destroy(collider);}
                else UnityEngine.Object.DestroyImmediate(collider);
                changed=true;
            }
            return changed;
        }

        static bool HasParentCollision(Transform target)
        {
            for(var parent=target.parent;parent;parent=parent.parent)
                foreach(var collider in parent.GetComponents<Collider>())
                {
                    // A terrain collider on a parent does not represent its separate props.
                    if(collider is MeshCollider mesh && mesh.sharedMesh && IsSurface(mesh.sharedMesh.name))continue;
                    return true;
                }
            return false;
        }
    }
}
