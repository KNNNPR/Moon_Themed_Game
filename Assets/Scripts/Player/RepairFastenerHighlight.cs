using MoonThemedGame.Interactions;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Repair
{
    public sealed class RepairFastenerHighlight : MonoBehaviour
    {
        public RepairBolt Target { get; private set; }
        static RepairFastenerHighlight instance;
        InteractionOutline persistent;
        int registryVersion = -1;
        readonly List<MeshRenderer> meshes = new List<MeshRenderer>();
        public static void Ensure(Scene scene)
        {
            if (instance) return;
            var root = new GameObject("Repair / fastener highlights");
            SceneManager.MoveGameObjectToScene(root, scene); instance = root.AddComponent<RepairFastenerHighlight>();
        }
        void LateUpdate()
        {
            var camera = Camera.main;
            var player = camera ? camera.GetComponentInParent<FirstPersonController>() : null;
            if (!persistent)
            {
                persistent = gameObject.AddComponent<InteractionOutline>();
                persistent.widthPixels = 2;
                persistent.outlineColor = new Color(.62f, .9f, .72f, .85f);
            }
            if (registryVersion != RepairBolt.RegistryVersion)
            {
                meshes.Clear();
                foreach (var bolt in RepairBolt.All) if (bolt) bolt.AddHighlightMeshes(meshes);
                persistent.targets = meshes.ToArray(); registryVersion = RepairBolt.RegistryVersion;
            }
            // One shared mask keeps every fastener readable without a full-screen
            // render texture per bolt. The meshes retain their live tightening poses.
            persistent.SetHighlighted(camera && (!player || player.isActiveAndEnabled) && meshes.Count > 0 && !GameSettingsMenu.IsOpen);
            RepairBolt target = null;
            if (camera && (!player || player.isActiveAndEnabled) && Cursor.lockState == CursorLockMode.Locked && !GameSettingsMenu.BlockGameplayInput && !PlayerBody.BeltOpen)
                target = RepairBolt.Aimed(new Ray(camera.transform.position, camera.transform.forward), 3.5f * PlayerBody.Scale, 7, true);
            if (target && !target.NeedsHighlight) target = null;
            if (Target == target) return;
            if (Target) Target.SetAimed(false);
            Target = target;
            if (Target) Target.SetAimed(true);
        }
        void OnDisable() { if (persistent) persistent.SetHighlighted(false); if (Target) Target.SetAimed(false); Target = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() => instance = null;
    }
}
