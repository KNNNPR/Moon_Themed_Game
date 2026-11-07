using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Scavenger
{
    // An editable, authored route layout. Runtime needs only the points, never a navigation bake.
    [DisallowMultipleComponent]
    public sealed class SalvageSpawnLayout : MonoBehaviour
    {
        [TextArea(3,10)] public string balanceNotes;
        public static SalvageSpawnLayout Ensure(Scene scene)
        {
            if(!scene.IsValid() || scene.name!="Operator")return null;
            foreach(var layout in FindObjectsByType<SalvageSpawnLayout>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(layout.gameObject.scene==scene)return layout;
            var prefab=Resources.Load<GameObject>("SalvageSpawnLayout");if(!prefab)return null;
            var root=Instantiate(prefab);root.name="Salvage Spawn Points";SceneManager.MoveGameObjectToScene(root,scene);
            return root.GetComponent<SalvageSpawnLayout>();
        }
    }
}
