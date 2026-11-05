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
            ("Cylinder.049", "Cube.082", "DOOR_LOCK.002", new[] { "FENCE_DOOR.004", "FENCE_DOOR.005" }),
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
            Install();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() => Report = null;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install();

        static void Install()
        {
            var named = new Dictionary<string, Transform>();
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (!named.ContainsKey(t.name)) named[t.name] = t;
            if (!named.TryGetValue(Platform, out var platform) && !named.ContainsKey(Gates[0].lockName)) return;

            foreach (var gate in Gates)
            {
                if (!named.TryGetValue(gate.button, out var button) || !named.TryGetValue(gate.lockName, out var lockPart)) continue;
                if (button.GetComponent<RobotLever>()) continue;
                var leaves = new List<Transform>();
                foreach (var leaf in gate.leaves) if (named.TryGetValue(leaf, out var t)) leaves.Add(t);
                var mechanism = lockPart.gameObject.AddComponent<LocationGate>();
                mechanism.Setup(lockPart, leaves.ToArray());
                Wire(button, Find(named, gate.post), "GATE", lever => mechanism.Press(Presser));
            }

            if (platform && !platform.GetComponent<LocationPlatform>() &&
                named.TryGetValue(TopButton, out var top) && named.TryGetValue(BottomButton, out var bottom))
            {
                var lift = platform.gameObject.AddComponent<LocationPlatform>();
                lift.Setup(top, bottom, Find(named, TopGround));
                Wire(top, Find(named, TopPost), "PLATFORM", lever => lift.Press());
                Wire(bottom, Find(named, BottomPost), "PLATFORM", lever => lift.Press());
            }
        }

        static Transform Find(Dictionary<string, Transform> named, string name) => named.TryGetValue(name, out var t) ? t : null;

        static void Wire(Transform button, Transform post, string label, Action<RobotLever> pressed)
        {
            var renderer = button.GetComponent<Renderer>();
            var size = renderer ? renderer.bounds.size : Vector3.one;
            var axis = post ? WorldBounds(post).center - WorldBounds(button).center : -button.forward;
            axis.y = 0;
            if (axis.sqrMagnitude < 1e-6f) axis = -button.forward;
            var lever = button.gameObject.AddComponent<RobotLever>();
            lever.reach = 4 + Mathf.Max(size.x, size.z);
            lever.MakeButton(label, axis, Mathf.Min(size.x, size.z) * 0.3f);
            lever.Pulled += pressed;
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
