using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    // A lever the robot can pull with its manipulator arm ([E] in the feed when close).
    // Put it on any object; `handle` swings between the two angles around its local X axis.
    // With `button` set it is a push button instead: the arm presses it and the cap dips in.
    public sealed class RobotLever : MonoBehaviour
    {
        static readonly List<RobotLever> all = new List<RobotLever>();

        public Transform handle;
        [Tooltip("The point the robot's claw grabs; defaults to the end of the handle.")]
        public Transform grip;
        public Renderer lamp;
        [Min(0.1f)] public float reach = 2.5f;
        public float offAngle = -35, onAngle = 35;
        [Min(0.05f)] public float pullTime = 0.6f;
        public bool on;

        [Header("Push button")]
        public bool button;
        public string label = "LEVER";
        public Vector3 pressAxis;
        public float pressDepth;

        [SerializeField] LocationGate gate;
        [SerializeField] LocationPlatform platform;
        [SerializeField] LocationBridge bridge;

        public event Action<RobotLever> Pulled;
        public void Bind(LocationGate target) => gate = target;
        public void Bind(LocationPlatform target) => platform = target;
        public void Bind(LocationBridge target) => bridge = target;

        float angle, pressed = 1;
        Vector3 rest;
        Material lampMaterial;

        public static IReadOnlyList<RobotLever> All => all;
        public Vector3 GripPosition => grip ? grip.position : handle ? handle.position + handle.up * 0.3f : transform.position;

        void OnEnable()
        {
            all.Add(this);
            angle = on ? onAngle : offAngle;
            if (button) rest = transform.position;
            Apply();
        }

        void OnDisable() => all.Remove(this);

        public static RobotLever Nearest(Vector3 point, float extraReach)
        {
            RobotLever best = null;
            float bestDistance = float.MaxValue;
            foreach (var lever in all)
            {
                var offset = lever.transform.position - point;
                offset.y = 0;
                float distance = offset.magnitude;
                if (distance <= lever.reach + extraReach && distance < bestDistance) { best = lever; bestDistance = distance; }
            }
            return best;
        }

        public void MakeButton(string name, Vector3 axis, float depth)
        {
            button = true;
            label = name;
            pressAxis = axis.normalized;
            pressDepth = depth;
            pullTime = 0.3f;
            rest = transform.position;
        }

        public void Pull()
        {
            if (button) pressed = 0;
            on = !on;
            if (gate) gate.Press(LocationMechanisms.Presser);
            if (platform) platform.Press();
            if (bridge) bridge.Press();
            Pulled?.Invoke(this);
        }

        void Update()
        {
            if (button)
            {
                if (pressed >= 1) return;
                pressed = Mathf.MoveTowards(pressed, 1, Time.deltaTime / 0.35f);
                transform.position = rest + pressAxis * (pressDepth * Mathf.Sin(pressed * Mathf.PI));
                return;
            }
            float target = on ? onAngle : offAngle;
            angle = Mathf.MoveTowards(angle, target, Mathf.Abs(onAngle - offAngle) / pullTime * Time.deltaTime);
            Apply();
        }

        void Apply()
        {
            if (handle) handle.localRotation = Quaternion.Euler(angle, 0, 0);
            if (!lamp) return;
            if (!lampMaterial) lampMaterial = lamp.material;
            var color = on ? new Color(0.4f, 1, 0.45f) : new Color(1, 0.3f, 0.2f);
            lampMaterial.color = color;
            if (lampMaterial.HasProperty("_BaseColor")) lampMaterial.SetColor("_BaseColor", color);
            if (lampMaterial.HasProperty("_EmissionColor")) lampMaterial.SetColor("_EmissionColor", color * 2);
        }

        // Placeholder lever made of primitives, sized for a robot of the given length.
        public static RobotLever CreateTest(Vector3 ground, Quaternion facing, float size)
        {
            var root = new GameObject("Robot Lever (test)").transform;
            root.SetPositionAndRotation(ground, facing);
            var paint = new Color(0.62f, 0.5f, 0.18f);
            Part(root, PrimitiveType.Cube, "Base", new Vector3(0, size * 0.1f, 0), new Vector3(size * 0.5f, size * 0.2f, size * 0.35f), new Color(0.3f, 0.3f, 0.32f), true);
            Part(root, PrimitiveType.Cube, "Post", new Vector3(0, size * 0.45f, 0), new Vector3(size * 0.14f, size * 0.5f, size * 0.14f), paint, false);
            var pivot = new GameObject("Handle").transform;
            pivot.SetParent(root, false);
            pivot.localPosition = new Vector3(0, size * 0.7f, 0);
            Part(pivot, PrimitiveType.Cylinder, "Shaft", new Vector3(0, size * 0.22f, 0), new Vector3(size * 0.05f, size * 0.22f, size * 0.05f), new Color(0.75f, 0.75f, 0.78f), false);
            var grip = Part(pivot, PrimitiveType.Sphere, "Grip", new Vector3(0, size * 0.46f, 0), Vector3.one * size * 0.12f, new Color(0.8f, 0.15f, 0.12f), false);
            var lamp = Part(root, PrimitiveType.Sphere, "Lamp", new Vector3(size * 0.18f, size * 0.24f, 0), Vector3.one * size * 0.09f, Color.red, false);
            var lever = root.gameObject.AddComponent<RobotLever>();
            lever.handle = pivot;
            lever.grip = grip.transform;
            lever.lamp = lamp.GetComponent<Renderer>();
            lever.reach = size * 1.6f;
            lever.Apply();
            return lever;
        }

        static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Color color, bool solid)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            if (!solid) Destroy(part.GetComponent<Collider>());
            var renderer = part.GetComponent<Renderer>();
            var shader = Shader.Find("Old Tech/Retro Cel Lit");
            var material = new Material(shader ? shader : Shader.Find("Universal Render Pipeline/Lit"));
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            renderer.sharedMaterial = material;
            return part;
        }
    }
}
