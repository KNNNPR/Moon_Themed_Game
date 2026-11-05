using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    public enum Fastener { Bolt, Screw }

    // A bolt (wrench) or screw (screwdriver) holding a freshly fitted part, driven in turn by turn.
    [DisallowMultipleComponent]
    public sealed class RepairBolt : MonoBehaviour
    {
        static readonly List<RepairBolt> all = new List<RepairBolt>();
        static readonly Color RustyBolt = new Color(0.86f, 0.47f, 0.16f);
        static readonly Color BrassScrew = new Color(0.86f, 0.74f, 0.38f);
        static readonly Color Steel = new Color(0.5f, 0.53f, 0.56f);

        RepairRobotTarget robot;
        GameObject part;
        Vector3 outLocal, inLocal;
        Renderer[] renderers;
        int turns;

        public Fastener Kind { get; private set; }
        public int Turns => Kind == Fastener.Bolt ? 6 : 5;
        public int TurnsDone => turns;
        public bool Tight => turns >= Turns;
        public bool Seated => !robot || robot.Seated(part);

        public static Fastener KindFor(GameObject part)
        {
            string name = part.name.ToLowerInvariant();
            return name.Contains("wheel") || name.Contains("battery") ? Fastener.Bolt : Fastener.Screw;
        }

        public static bool NeedsHammer(GameObject part)
        {
            string name = part.name.ToLowerInvariant();
            return name.Contains("wheel") || name.Contains("body");
        }

        // The loose fastener closest to where the player is looking; robot panels never hide it.
        public static RepairBolt Aimed(Ray view, float reach, float maxAngle = 7)
        {
            RepairBolt best = null;
            float bestAngle = maxAngle;
            foreach (var bolt in all)
            {
                var offset = bolt.transform.position - view.origin;
                if (offset.magnitude > reach) continue;
                float angle = Vector3.Angle(view.direction, offset);
                if (bolt.Tight) angle += 2;
                if (angle < bestAngle) { bestAngle = angle; best = bolt; }
            }
            return best;
        }

        static Bounds BoundsOf(IEnumerable<Renderer> renderers)
        {
            bool first = true;
            var bounds = new Bounds();
            foreach (var r in renderers)
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            return bounds;
        }

        public static Vector3 Outward(RepairRobotTarget robot, GameObject part, out float size)
        {
            var bounds = BoundsOf(part.GetComponentsInChildren<Renderer>());
            size = bounds.size.magnitude;
            var outward = bounds.center - (robot ? robot.transform.position : bounds.center - Vector3.forward);
            outward.y = 0;
            return outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.forward;
        }

        public static List<RepairBolt> Spawn(RepairRobotTarget robot, GameObject part, Vector3 viewer, Fastener kind)
        {
            var bolts = new List<RepairBolt>();
            var renderers = new List<Renderer>(part.GetComponentsInChildren<Renderer>());
            if (renderers.Count == 0) return bolts;
            Physics.SyncTransforms();
            var whole = BoundsOf(renderers);

            if (part.name.ToLowerInvariant().Contains("wheel") && WheelAxis(part, robot, whole, out var axle))
                return SpawnHub(robot, part, whole, axle, kind, bolts);

            // A set of separate pieces (both headlights) gets its own pair of fasteners on each piece.
            if (renderers.Count > 1)
            {
                foreach (var piece in renderers.GetRange(0, Mathf.Min(3, renderers.Count)))
                    SpawnFace(robot, part, piece.bounds, viewer, kind, 2, bolts);
                return bolts;
            }
            SpawnFace(robot, part, whole, viewer, kind, whole.size.magnitude > 0.8f ? 4 : 2, bolts);
            return bolts;
        }

        static void SpawnFace(RepairRobotTarget robot, GameObject part, Bounds bounds, Vector3 viewer, Fastener kind, int count, List<RepairBolt> bolts)
        {
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
            float size = Mathf.Clamp(bounds.size.magnitude * 0.05f, 0.018f, 0.06f);
            var layout = count >= 4
                ? new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) }
                : e1 >= e2 ? new[] { new Vector2(-1, 0), new Vector2(1, 0) } : new[] { new Vector2(0, -1), new Vector2(0, 1) };

            foreach (var spot in layout)
            {
                var point = bounds.center + normal * depth + t1 * (spot.x * e1 * 0.55f) + t2 * (spot.y * e2 * 0.55f);
                var start = point + normal * 0.3f;
                var surface = point;
                var surfaceNormal = normal;
                float nearest = float.MaxValue;
                foreach (var hit in Physics.RaycastAll(start, -normal, depth * 2 + 0.3f, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.collider.transform.IsChildOf(part.transform) && hit.distance < nearest)
                    { nearest = hit.distance; surface = hit.point; surfaceNormal = Vector3.Dot(hit.normal, normal) > 0.3f ? hit.normal : normal; }
                bolts.Add(Create(robot, part, surface, surfaceNormal, size, kind));
            }
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        // The axle runs along the wheel's thinnest side, pointing away from the robot.
        static bool WheelAxis(GameObject part, RepairRobotTarget robot, Bounds bounds, out Vector3 axle)
        {
            axle = Vector3.zero;
            var filter = part.GetComponentInChildren<MeshFilter>();
            if (!filter || !filter.sharedMesh) return false;
            var size = filter.sharedMesh.bounds.size;
            var local = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
            axle = filter.transform.TransformDirection(local).normalized;
            var outward = bounds.center - (robot ? robot.transform.position : bounds.center);
            outward.y = 0;
            if (Vector3.Dot(axle, outward) < 0) axle = -axle;
            return true;
        }

        static List<RepairBolt> SpawnHub(RepairRobotTarget robot, GameObject part, Bounds bounds, Vector3 axle, Fastener kind, List<RepairBolt> bolts)
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
                bolts.Add(Create(robot, part, surface, normal, size, kind));
            }
            return bolts;
        }

        static RepairBolt Create(RepairRobotTarget robot, GameObject part, Vector3 surface, Vector3 normal, float size, Fastener kind)
        {
            var root = new GameObject(kind == Fastener.Bolt ? "Bolt" : "Screw");
            var pieces = new List<Renderer>();
            var catalog=Resources.Load<MoonThemedGame.Scavenger.ShopCatalog>("ShopCatalog");
            var prefab=catalog?(kind==Fastener.Bolt?catalog.bolt:catalog.screw):null;
            if(prefab)
            {
                var visual=Instantiate(prefab,root.transform,false);
                visual.name=prefab.name+" visual";visual.transform.localPosition=Vector3.zero;
                foreach(var collider in visual.GetComponentsInChildren<Collider>())collider.enabled=false;
                pieces.AddRange(visual.GetComponentsInChildren<Renderer>());
                if(pieces.Count>0)
                {
                    var bounds=pieces[0].bounds;foreach(var r in pieces)bounds.Encapsulate(r.bounds);
                    visual.transform.localScale*=size*2.2f/Mathf.Max(0.0001f,bounds.size.y);
                    bounds=pieces[0].bounds;foreach(var r in pieces)bounds.Encapsulate(r.bounds);
                    visual.transform.localPosition-=new Vector3(bounds.center.x,bounds.max.y,bounds.center.z);
                }
            }
            else if (kind == Fastener.Bolt)
            {
                pieces.Add(Piece(PrimitiveType.Cylinder, root.transform, new Vector3(size * 1.6f, size * 0.35f, size * 1.6f), 0));
                pieces.Add(Piece(PrimitiveType.Cylinder, root.transform, new Vector3(size * 0.7f, size * 1.0f, size * 0.7f), -size * 1.2f));
            }
            else
            {
                pieces.Add(Piece(PrimitiveType.Cylinder, root.transform, new Vector3(size * 1.3f, size * 0.16f, size * 1.3f), 0));
                pieces.Add(Piece(PrimitiveType.Cylinder, root.transform, new Vector3(size * 0.45f, size * 1.0f, size * 0.45f), -size * 1.1f));
                var slot = Piece(PrimitiveType.Cylinder, root.transform, new Vector3(size * 1.2f, size * 0.05f, size * 0.22f), size * 0.13f);
                slot.material.color = new Color(0.12f, 0.1f, 0.08f);
            }
            root.transform.SetPositionAndRotation(surface + normal * size * 1.8f, Quaternion.FromToRotation(Vector3.up, normal));
            var trigger = root.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = size * 2.2f;
            root.transform.SetParent(part.transform, true);

            var bolt = root.AddComponent<RepairBolt>();
            bolt.Kind = kind;
            bolt.robot = robot;
            bolt.part = part;
            bolt.outLocal = root.transform.localPosition;
            bolt.inLocal = part.transform.InverseTransformPoint(surface + normal * size * 0.2f);
            bolt.renderers = pieces.ToArray();
            bolt.Paint(kind == Fastener.Bolt ? RustyBolt : BrassScrew);
            return bolt;
        }

        static Renderer Piece(PrimitiveType type, Transform parent, Vector3 scale, float height)
        {
            var piece = GameObject.CreatePrimitive(type);
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
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] && !(Kind == Fastener.Screw && i == 2)) renderers[i].material.color = color;
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public void Turn(int direction)
        {
            if (!Seated || (Tight && direction > 0)) return;
            turns = Mathf.Clamp(turns + direction, 0, Turns);
            float t = turns / (float)Turns;
            transform.localPosition = Vector3.Lerp(outLocal, inLocal, t);
            transform.Rotate(Vector3.up, 60 * direction, Space.Self);
            var loose = Kind == Fastener.Bolt ? RustyBolt : BrassScrew;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] && !(Kind == Fastener.Screw && i == 2)) renderers[i].material.color = Color.Lerp(loose, Steel, t * t);
            if (Tight && robot) robot.BoltTightened(part);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() => all.Clear();
    }
}
