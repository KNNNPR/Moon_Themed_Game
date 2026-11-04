using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    // A bolt holding a freshly attached part; the wrench screws it in turn by turn.
    [DisallowMultipleComponent]
    public sealed class RepairBolt : MonoBehaviour
    {
        public const int Turns = 6;
        static readonly Color LooseColor = new Color(0.86f, 0.47f, 0.16f);
        static readonly Color TightColor = new Color(0.5f, 0.53f, 0.56f);

        RepairRobotTarget robot;
        GameObject part;
        Vector3 outLocal, inLocal;
        Renderer[] renderers;
        int turns;

        public int TurnsDone => turns;
        public bool Tight => turns >= Turns;

        public static List<RepairBolt> Spawn(RepairRobotTarget robot, GameObject part, Vector3 viewer)
        {
            var bolts = new List<RepairBolt>();
            var renderers = part.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return bolts;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            Physics.SyncTransforms();

            if (part.name.ToLowerInvariant().Contains("wheel") && WheelAxis(part, robot, bounds, out var axle))
                return SpawnHub(robot, part, bounds, axle, bolts);

            var toViewer = (viewer - bounds.center).normalized;
            var normal = Vector3.up;
            float best = -2;
            foreach (var axis in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back, Vector3.up })
            {
                float dot = Vector3.Dot(axis, toViewer);
                if (dot > best) { best = dot; normal = axis; }
            }
            var t1 = Vector3.Cross(normal, normal == Vector3.up ? Vector3.forward : Vector3.up).normalized;
            var t2 = Vector3.Cross(normal, t1).normalized;
            float depth = Vector3.Dot(bounds.extents, Abs(normal));
            float e1 = Vector3.Dot(bounds.extents, Abs(t1)), e2 = Vector3.Dot(bounds.extents, Abs(t2));
            float size = Mathf.Clamp(bounds.size.magnitude * 0.05f, 0.022f, 0.06f);
            var layout = bounds.size.magnitude > 0.8f
                ? new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) }
                : new[] { new Vector2(-1, 0), new Vector2(1, 0) };

            foreach (var spot in layout)
            {
                var point = bounds.center + normal * depth + t1 * (spot.x * e1 * 0.55f) + t2 * (spot.y * e2 * 0.55f);
                var surface = point;
                var surfaceNormal = normal;
                foreach (var hit in Physics.RaycastAll(point + normal * 0.3f, -normal, depth * 2 + 0.3f, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.collider.transform.IsChildOf(part.transform) && Vector3.Distance(hit.point, point + normal * 0.3f) < Vector3.Distance(surface, point + normal * 0.3f) + 1e-3f)
                    { surface = hit.point; surfaceNormal = hit.normal; }
                bolts.Add(Create(robot, part, surface, surfaceNormal, size));
            }
            return bolts;
        }

        // The axle runs along the wheel's thinnest side, pointing away from the robot.
        static bool WheelAxis(GameObject part, RepairRobotTarget robot, Bounds bounds, out Vector3 axle)
        {
            axle = Vector3.zero;
            var filter = part.GetComponentInChildren<MeshFilter>();
            if (!filter || !filter.sharedMesh) return false;
            var size = filter.sharedMesh.bounds.size;
            var local = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
            axle = filter.transform.TransformDirection(local).normalized;
            var robotCenter = robot ? robot.transform.position : bounds.center;
            var outward = bounds.center - robotCenter;
            outward.y = 0;
            if (Vector3.Dot(axle, outward) < 0) axle = -axle;
            return true;
        }

        static List<RepairBolt> SpawnHub(RepairRobotTarget robot, GameObject part, Bounds bounds, Vector3 axle, List<RepairBolt> bolts)
        {
            var t1 = Vector3.Cross(axle, Mathf.Abs(axle.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
            var t2 = Vector3.Cross(axle, t1).normalized;
            float depth = Vector3.Dot(bounds.extents, Abs(axle));
            float wheelRadius = Mathf.Min(Vector3.Dot(bounds.extents, Abs(t1)), Vector3.Dot(bounds.extents, Abs(t2)));
            float size = Mathf.Clamp(wheelRadius * 0.09f, 0.015f, 0.05f);
            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                var ring = (t1 * Mathf.Cos(angle) + t2 * Mathf.Sin(angle)) * wheelRadius * 0.22f;
                var start = bounds.center + ring + axle * (depth + 0.3f);
                var surface = bounds.center + ring + axle * depth;
                var normal = axle;
                float nearest = float.MaxValue;
                foreach (var hit in Physics.RaycastAll(start, -axle, depth * 2 + 0.3f, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.collider.transform.IsChildOf(part.transform) && hit.distance < nearest)
                    { nearest = hit.distance; surface = hit.point; normal = Vector3.Dot(hit.normal, axle) > 0.5f ? hit.normal : axle; }
                bolts.Add(Create(robot, part, surface, normal, size));
            }
            return bolts;
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        static RepairBolt Create(RepairRobotTarget robot, GameObject part, Vector3 surface, Vector3 normal, float size)
        {
            var root = new GameObject("Bolt");
            root.transform.SetPositionAndRotation(surface + normal * size * 1.8f, Quaternion.FromToRotation(Vector3.up, normal));
            var head = Piece(root.transform, new Vector3(size * 1.6f, size * 0.35f, size * 1.6f), 0);
            var shaft = Piece(root.transform, new Vector3(size * 0.7f, size * 1.0f, size * 0.7f), -size * 1.2f);
            var trigger = root.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = size * 2.2f;
            root.transform.SetParent(part.transform, true);

            var bolt = root.AddComponent<RepairBolt>();
            bolt.robot = robot;
            bolt.part = part;
            bolt.outLocal = root.transform.localPosition;
            bolt.inLocal = part.transform.InverseTransformPoint(surface + normal * size * 0.2f);
            bolt.renderers = new[] { head, shaft };
            bolt.Paint(LooseColor);
            return bolt;
        }

        static Renderer Piece(Transform parent, Vector3 scale, float height)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(piece.GetComponent<Collider>());
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = Vector3.up * height;
            piece.transform.localScale = scale;
            var renderer = piece.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return renderer;
        }

        void Paint(Color color)
        {
            foreach (var r in renderers) if (r) r.material.color = color;
        }

        public void Turn(int direction)
        {
            if (Tight && direction > 0) return;
            turns = Mathf.Clamp(turns + direction, 0, Turns);
            float t = turns / (float)Turns;
            transform.localPosition = Vector3.Lerp(outLocal, inLocal, t);
            transform.Rotate(Vector3.up, 60 * direction, Space.Self);
            Paint(Color.Lerp(LooseColor, TightColor, t * t));
            if (Tight && robot) robot.BoltTightened(part);
        }
    }
}
