using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Scavenger
{
    // Wires the push buttons modelled in LandScape.fbx (material "Buttons") to the gates and the
    // shaft platform. The robot presses them with the manipulator arm, like a lever.
    public static class LocationMechanisms
    {
        // Button, post it sits on, gate lock, gate leaves.
        static readonly (string button, string post, string lockName, string[] leaves)[] Gates =
        {
            ("Cylinder.045", "Cube.075", "DOOR_LOCK", new[] { "FENCE_DOOR", "FENCE_DOOR.001" }),
            ("Cylinder.046", "Cube.076", "DOOR_LOCK.001", new[] { "FENCE_DOOR.002", "FENCE_DOOR.003" }),
            ("button_ship", null, "DOOR_LOCK.002", new[] { "FENCE_DOOR.004", "FENCE_DOOR.005" }),
        };
        const string Platform = "Platform", TopButton = "Cylinder.043", TopPost = "Cube.073";
        const string BottomButton = "Cylinder.042", BottomPost = "Cube.071", TopGround = "Cube.072";

        public static Vector3 Presser;
        public static Action<string> Report;

        public static void Say(string key) => Report?.Invoke(Loc.T(key));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallOnLoad()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() => Report = null;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "MENU") ConfigureScene(scene);
        }

        public static void ConfigureScene(Scene scene)
        {
            var named = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>())
                    if (!named.ContainsKey(t.name)) named[t.name] = t;
            named.TryGetValue(Platform,out var platform);
            var bridge = Find(named, "bridge") ?? Find(named, "Cube.080");
            var bridgeButton = Find(named, "Bridge Button") ?? Find(named, "Cylinder.049") ?? Find(named, "Cyllinder.049");

            foreach (var gate in Gates)
            {
                var button=Find(named,gate.button);var lockPart=Find(named,gate.lockName);
                if(!button || !lockPart)continue;
                if (button == bridgeButton && bridge) continue;
                if (button.GetComponent<RobotLever>()) continue;
                var leaves = new List<Transform>();
                foreach (var leaf in gate.leaves){var t=Find(named,leaf);if(t)leaves.Add(t);}
                if(leaves.Count!=gate.leaves.Length)continue;
                var mechanism = lockPart.gameObject.AddComponent<LocationGate>();
                mechanism.Setup(lockPart, leaves.ToArray());
                MakeLever(button, Find(named, gate.post), "GATE").Bind(mechanism);
            }

            if (platform && !platform.GetComponent<LocationPlatform>() &&
                named.TryGetValue(TopButton, out var top) && named.TryGetValue(BottomButton, out var bottom))
            {
                var lift = platform.gameObject.AddComponent<LocationPlatform>();
                lift.Setup(top, bottom, Find(named, TopGround));
                MakeLever(top, Find(named, TopPost), "PLATFORM").Bind(lift);
                MakeLever(bottom, Find(named, BottomPost), "PLATFORM").Bind(lift);
            }
            if(bridge && bridgeButton)
            {
                var mechanism=bridge.GetComponent<LocationBridge>();
                if(mechanism && mechanism.IsInitialized)return;
                if(!mechanism)mechanism=bridge.gameObject.AddComponent<LocationBridge>();
                mechanism.Setup(bridgeButton);
                if (!bridgeButton.TryGetComponent<RobotLever>(out var lever))
                    lever = MakeLever(bridgeButton, null, Loc.Russian ? "МОСТ" : "BRIDGE");
                lever.Bind(mechanism);
                if(!bridgeButton.GetComponent<BridgeButtonInteractable>())bridgeButton.gameObject.AddComponent<BridgeButtonInteractable>();
                if(!bridgeButton.GetComponent<Collider>())
                {
                    var mesh=bridgeButton.GetComponent<MeshFilter>();
                    if(mesh && mesh.sharedMesh)bridgeButton.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh.sharedMesh;
                }
            }
        }

        static Transform Find(Dictionary<string, Transform> named,string name)
        {
            if(string.IsNullOrEmpty(name))return null;
            if(named.TryGetValue(name,out var exact))return exact;
            string key=NameKey(name);
            foreach(var pair in named)if(NameKey(pair.Key)==key)return pair.Value;
            return null;
        }
        static string NameKey(string name)
        {
            string key=name.Replace("_","").Replace(".","").Replace(" ","").ToUpperInvariant();
            const string prefix="FENCEDOOR";
            if(key.StartsWith(prefix) && int.TryParse(key.Substring(prefix.Length),out int number))return prefix+number;
            return key;
        }

        static RobotLever MakeLever(Transform button, Transform post, string label)
        {
            var renderer = button.GetComponent<Renderer>();
            var size = renderer ? renderer.bounds.size : Vector3.one;
            var axis = post ? WorldBounds(post).center - WorldBounds(button).center : -button.forward;
            axis.y = 0;
            if (axis.sqrMagnitude < 1e-6f) axis = -button.forward;
            var lever = button.gameObject.AddComponent<RobotLever>();
            lever.reach = 4 + Mathf.Max(size.x, size.z);
            lever.MakeButton(label, axis, Mathf.Min(size.x, size.z) * 0.3f);
            return lever;
        }

        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3 - 2 * t);
        }

        public static Bounds WorldBounds(Transform target)
        {
            var renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(target.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        public static Rigidbody Kinematic(GameObject target)
        {
            if (!target.TryGetComponent(out Rigidbody body)) body = target.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            return body;
        }
    }
}
