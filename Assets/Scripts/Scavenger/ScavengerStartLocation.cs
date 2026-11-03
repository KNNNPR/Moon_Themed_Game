using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    // Placeholder start area for the robot: ground, curbs of rising height, a ramp onto a
    // platform and scattered low rocks. Any scene object named "Scavenger Start Location"
    // replaces it; its child "Robot Start" (on the ground, facing forward) is where the
    // robot begins and where evacuation brings it back.
    public static class ScavengerStartLocation
    {
        public const string ObjectName = "Scavenger Start Location";
        public const string MarkerName = "Robot Start";

        public static Transform Find()
        {
            var found = GameObject.Find(ObjectName);
            return found ? found.transform : null;
        }

        public static Transform Marker(Transform location) => location ? location.Find(MarkerName) : null;

        public static GameObject BuildFor(Transform robot)
        {
            var renderers = robot.GetComponentsInChildren<Renderer>();
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(robot.position, Vector3.one);
            float wheel = 0;
            foreach (var r in renderers)
            {
                bounds.Encapsulate(r.bounds);
                if (r.name.StartsWith("Wheel") || r.transform.parent && r.transform.parent.name.StartsWith("Wheel"))
                    wheel = Mathf.Max(wheel, r.bounds.extents.y);
            }
            float unit = Mathf.Max(bounds.size.x, bounds.size.z);
            if (wheel <= 0) wheel = bounds.size.y * 0.25f;
            var heading = robot.TransformDirection(ScavengerLink.DetectForward(robot, bounds.center));
            heading.y = 0;
            if (heading.sqrMagnitude < 1e-4f) heading = Vector3.forward;
            var ground = new Vector3(robot.position.x, bounds.min.y, robot.position.z);
            return Build(ground, heading.normalized, unit, wheel);
        }

        public static GameObject Build(Vector3 ground, Vector3 heading, float unit, float wheelRadius)
        {
            var root = new GameObject(ObjectName).transform;
            root.SetPositionAndRotation(ground, Quaternion.LookRotation(heading));
            var floor = Load("ScavengerGround", new Color(0.32f, 0.32f, 0.33f));
            var paint = Load("ScavengerCurb", new Color(0.62f, 0.5f, 0.18f));

            Box(root, "Ground", new Vector3(0, -0.25f, 6 * unit), new Vector3(24 * unit, 0.5f, 24 * unit), Quaternion.identity, floor);

            // Curbs to test climbing: 30%, 60% and 90% of the wheel radius.
            float[] heights = { 0.3f, 0.6f, 0.9f };
            for (int i = 0; i < heights.Length; i++)
            {
                float h = wheelRadius * heights[i];
                Box(root, $"Curb {Mathf.RoundToInt(heights[i] * 100)}%", new Vector3((i - 1) * 1.9f * unit, h * 0.5f, 2.5f * unit), new Vector3(1.6f * unit, h, 0.6f * unit), Quaternion.identity, paint);
            }

            // Ramp up onto a platform and down the other side.
            float rise = 0.5f * unit, run = 3 * unit, width = 1.8f * unit, thickness = 0.2f * unit;
            float slope = Mathf.Sqrt(rise * rise + run * run);
            float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            float startZ = 6 * unit;
            Box(root, "Ramp Up", new Vector3(0, rise * 0.5f - thickness * 0.5f, startZ + run * 0.5f), new Vector3(width, thickness, slope), Quaternion.Euler(-angle, 0, 0), paint);
            Box(root, "Platform", new Vector3(0, rise * 0.5f, startZ + run + unit), new Vector3(width, rise, 2 * unit), Quaternion.identity, floor);
            Box(root, "Ramp Down", new Vector3(0, rise * 0.5f - thickness * 0.5f, startZ + run * 1.5f + 2 * unit), new Vector3(width, thickness, slope), Quaternion.Euler(angle, 0, 0), paint);

            // Low rocks the wheels can roll over.
            var random = new System.Random(7);
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
            for (int i = 0; i < 16; i++)
            {
                var position = new Vector3(Range(-10, 10) * unit, 0, Range(4, 17) * unit);
                if (Mathf.Abs(position.x) < 2.5f * unit && position.z > 5 * unit) continue;
                float h = wheelRadius * Range(0.25f, 0.8f);
                position.y = h * 0.4f;
                Box(root, "Rock", position, new Vector3(Range(0.3f, 0.8f) * unit, h, Range(0.3f, 0.8f) * unit), Quaternion.Euler(Range(-8, 8), Range(0, 360), Range(-8, 8)), floor);
            }

            var marker = new GameObject(MarkerName).transform;
            marker.SetParent(root, false);
            return root.gameObject;
        }

        static void Box(Transform root, string name, Vector3 position, Vector3 size, Quaternion rotation, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(root, false);
            box.transform.localPosition = position;
            box.transform.localRotation = rotation;
            box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = material;
        }

        static Material Load(string name, Color fallbackColor)
        {
            var material = Resources.Load<Material>(name);
            if (material) return material;
            var shader = Shader.Find("Old Tech/Retro Cel Lit");
            material = new Material(shader ? shader : Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            material.SetColor("_BaseColor", fallbackColor);
            return material;
        }
    }
}
