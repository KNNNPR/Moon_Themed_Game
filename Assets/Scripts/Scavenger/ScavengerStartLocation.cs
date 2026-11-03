using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    // Placeholder start area for the robot: ground level with the charge building's floor,
    // a ramped pad under the station buildings that stand slightly higher, and behind the
    // robot a test course of curbs, a ramp onto a platform and low rocks. Any scene object
    // named "Scavenger Start Location" replaces it; its child "Robot Start" (on the ground,
    // facing forward) is where the robot begins and where evacuation brings it back.
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

        public static GameObject BuildFor(Transform robot, Renderer chargeBuilding)
        {
            var renderers = robot.GetComponentsInChildren<Renderer>();
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(robot.position, Vector3.one);
            float wheel = 0;
            foreach (var r in renderers)
            {
                bounds.Encapsulate(r.bounds);
                if (r.name.StartsWith("Wheel")) wheel = Mathf.Max(wheel, r.bounds.extents.y);
            }
            float unit = Mathf.Max(bounds.size.x, bounds.size.z);
            if (wheel <= 0) wheel = bounds.size.y * 0.25f;
            var heading = robot.TransformDirection(ScavengerLink.DetectForward(robot, bounds.center));
            heading.y = 0;
            if (heading.sqrMagnitude < 1e-4f) heading = Vector3.forward;
            float groundY = chargeBuilding ? chargeBuilding.bounds.min.y : bounds.min.y;

            var buildings = new List<Bounds>();
            if (chargeBuilding)
                foreach (var r in chargeBuilding.transform.root.GetComponentsInChildren<Renderer>())
                    if (!r.transform.IsChildOf(robot)) buildings.Add(r.bounds);
            return Build(new Vector3(robot.position.x, groundY, robot.position.z), heading.normalized, unit, wheel, buildings, chargeBuilding ? chargeBuilding.bounds : (Bounds?)null);
        }

        public static GameObject Build(Vector3 ground, Vector3 heading, float unit, float wheelRadius, List<Bounds> buildings, Bounds? chargeBuilding)
        {
            var root = new GameObject(ObjectName).transform;
            root.SetPositionAndRotation(ground, Quaternion.LookRotation(heading));
            var floor = Load("ScavengerGround", new Color(0.32f, 0.32f, 0.33f));
            var paint = Load("ScavengerCurb", new Color(0.62f, 0.5f, 0.18f));

            Box(root, "Ground", new Vector3(0, -0.26f, 0), new Vector3(30 * unit, 0.5f, 30 * unit), Quaternion.identity, floor);
            BasePad(root, ground.y, buildings, chargeBuilding, floor);

            // Test course behind the robot, away from the buildings in front of it.
            bool Free(Vector3 local, float radius)
            {
                var world = root.TransformPoint(local);
                foreach (var b in buildings)
                {
                    var grown = b;
                    grown.Expand(new Vector3(radius * 2, 100, radius * 2));
                    if (grown.Contains(new Vector3(world.x, b.center.y, world.z))) return false;
                }
                return true;
            }

            float[] heights = { 0.3f, 0.6f, 0.9f };
            for (int i = 0; i < heights.Length; i++)
            {
                float h = wheelRadius * heights[i];
                var position = new Vector3((i - 1) * 1.9f * unit, h * 0.5f, -2.5f * unit);
                if (Free(position, unit)) Box(root, $"Curb {Mathf.RoundToInt(heights[i] * 100)}%", position, new Vector3(1.6f * unit, h, 0.6f * unit), Quaternion.identity, paint);
            }

            float rise = 0.5f * unit, run = 3 * unit, width = 1.8f * unit, thickness = 0.2f * unit;
            float slope = Mathf.Sqrt(rise * rise + run * run);
            float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            float startZ = -5 * unit;
            if (Free(new Vector3(0, 0, startZ - run - unit), run + 2 * unit))
            {
                Box(root, "Ramp Up", new Vector3(0, rise * 0.5f - thickness * 0.5f, startZ - run * 0.5f), new Vector3(width, thickness, slope), Quaternion.Euler(angle, 0, 0), paint);
                Box(root, "Platform", new Vector3(0, rise * 0.5f, startZ - run - unit), new Vector3(width, rise, 2 * unit), Quaternion.identity, floor);
                Box(root, "Ramp Down", new Vector3(0, rise * 0.5f - thickness * 0.5f, startZ - run * 1.5f - 2 * unit), new Vector3(width, thickness, slope), Quaternion.Euler(-angle, 0, 0), paint);
            }

            var random = new System.Random(7);
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
            for (int i = 0; i < 20; i++)
            {
                var position = new Vector3(Range(-10, 10) * unit, 0, Range(-15, -3) * unit);
                if (Mathf.Abs(position.x) < 2.5f * unit && position.z < -4 * unit) continue;
                float size = Range(0.3f, 0.8f) * unit;
                if (!Free(position, size)) continue;
                float h = wheelRadius * Range(0.25f, 0.8f);
                position.y = h * 0.4f;
                Box(root, "Rock", position, new Vector3(size, h, Range(0.3f, 0.8f) * unit), Quaternion.Euler(Range(-8, 8), Range(0, 360), Range(-8, 8)), floor);
            }

            var marker = new GameObject(MarkerName).transform;
            marker.SetParent(root, false);
            return root.gameObject;
        }

        // Station buildings that stand a little above the ground get a pad with gentle ramps on all sides.
        static void BasePad(Transform root, float groundY, List<Bounds> buildings, Bounds? chargeBuilding, Material material)
        {
            bool any = false;
            Bounds footprint = default;
            float top = float.MaxValue;
            foreach (var b in buildings)
            {
                float gap = b.min.y - groundY;
                if (gap < 0.4f || gap > 1.2f) continue;
                if (chargeBuilding is Bounds charge)
                {
                    charge.Expand(new Vector3(6, 0, 6));
                    if (charge.Intersects(b)) continue;
                }
                if (any) footprint.Encapsulate(b); else { footprint = b; any = true; }
                top = Mathf.Min(top, b.min.y);
            }
            if (!any) return;
            float rise = top - groundY;
            var center = new Vector3(footprint.center.x, groundY + rise * 0.5f, footprint.center.z);
            var size = new Vector3(footprint.size.x + 1, rise, footprint.size.z + 1);
            WorldBox(root, "Base Pad", center, size, Quaternion.identity, material);

            float run = rise * 4;
            float slope = Mathf.Sqrt(run * run + rise * rise);
            float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            float thickness = 0.2f;
            var sides = new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            foreach (var outward in sides)
            {
                float half = Mathf.Abs(Vector3.Dot(outward, size)) * 0.5f;
                float along = Mathf.Abs(Vector3.Dot(Vector3.Cross(Vector3.up, outward), size));
                var edge = new Vector3(center.x, groundY, center.z) + outward * half;
                var position = edge + outward * (run * 0.5f) + Vector3.up * (rise * 0.5f - thickness * 0.5f);
                WorldBox(root, "Base Ramp", position, new Vector3(along, thickness, slope), Quaternion.LookRotation(outward) * Quaternion.Euler(angle, 0, 0), material);
            }
        }

        static void Box(Transform root, string name, Vector3 position, Vector3 size, Quaternion rotation, Material material)
        {
            var box = Create(root, name, material);
            box.transform.localPosition = position;
            box.transform.localRotation = rotation;
            box.transform.localScale = size;
        }

        static void WorldBox(Transform root, string name, Vector3 position, Vector3 size, Quaternion rotation, Material material)
        {
            var box = Create(root, name, material);
            box.transform.SetPositionAndRotation(position, rotation);
            box.transform.localScale = size;
        }

        static GameObject Create(Transform root, string name, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(root, false);
            box.GetComponent<Renderer>().sharedMaterial = material;
            return box;
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
