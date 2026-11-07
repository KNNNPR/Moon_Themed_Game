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
        internal static IReadOnlyList<RepairBolt> All => all;
        internal static int RegistryVersion { get; private set; }
        static readonly Color RustyBolt = new Color(0.86f, 0.47f, 0.16f);
        static readonly Color BrassScrew = new Color(0.86f, 0.74f, 0.38f);
        static readonly Color Steel = new Color(0.5f, 0.53f, 0.56f);

        RepairRobotTarget robot;
        GameObject part;
        Vector3 outLocal, inLocal;
        Renderer[] renderers;
        float turns;
        float visualTurns;
        bool highlightNeeded = true;
        MoonThemedGame.Interactions.InteractionOutline hover;
        public bool Highlighted { get; private set; }
        Quaternion restRotation;
        public const float TurnDuration=.34f;
        public float VisualTurns=>visualTurns;
        public float Progress => Mathf.Clamp01(turns / Turns);
        public float VisualProgress => Mathf.Clamp01(visualTurns / Turns);
        public float VisualHeadAngle => AngleAt(VisualProgress);
        public const float SnugProgress = .6f;
        public const float FinalProgress = .88f;
        public float ProgressLimit => part && part.TryGetComponent<RepairPartMount>(out var mount) ? mount.AllowedProgress(this) : 1;
        public RepairBolt NextFastener => part && part.TryGetComponent<RepairPartMount>(out var mount) ? mount.RecommendedFastener : null;
        public bool IsTurning=>Mathf.Abs(visualTurns-turns)>.001f;

        public Fastener Kind { get; private set; }
        public int Turns => Kind == Fastener.Bolt ? 6 : 5;
        public int TurnsDone => Mathf.FloorToInt(turns + .0001f);
        public bool Tight => Progress >= .9999f;
        public bool Dismantling=>robot && robot.IsDismantling;
        public bool NeedsHighlight => (Dismantling?Progress>.0001f:!Tight) || IsTurning;
        internal void PrepareForDismantling(){turns=visualTurns=Turns;RefreshHighlightState();}
        public bool Seated => !robot || (robot.Accepts && robot.Seated(part));

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
        public static RepairBolt Aimed(Ray view, float reach, float maxAngle = 7, bool onlyUnfinished = false)
        {
            RepairBolt best = null;
            float bestAngle = maxAngle;
            foreach (var bolt in all)
            {
                if (onlyUnfinished && !bolt.NeedsHighlight) continue;
                var offset = bolt.transform.position - view.origin;
                if (offset.magnitude > reach) continue;
                float angle = Vector3.Angle(view.direction, offset);
                if (bolt.Tight) angle += 2;
                if (angle < bestAngle) { bestAngle = angle; best = bolt; }
            }
            return best;
        }

        public static Vector3 Outward(RepairRobotTarget robot, GameObject part, out float size)
        {
            var bounds = RepairPartMount.GeometryBounds(part.transform);
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
            var whole = RepairPartMount.GeometryBounds(part.transform);

            using(var surface=new PartSurface(part))
            {
                if (part.name.ToLowerInvariant().Contains("wheel") && !LongModule(part) && WheelAxis(part, robot, whole, out var axle))
                    return SpawnHub(robot, part, whole, axle, kind, bolts,surface);
                // One pair for the whole module; large panels retain their diagonal sequence.
                int count = part.name.ToLowerInvariant().Contains("body") && whole.size.magnitude > .8f ? 4 : 2;
                SpawnFace(robot, part, whole, viewer, kind, count, bolts,surface);
            }
            return bolts;
        }

        // Mounting temporarily disables the part's colliders. Query the actual imported
        // mesh through short-lived static proxies, including non-readable FBX meshes.
        // Never fall back to an empty corner of the module's bounding box.
        sealed class PartSurface:System.IDisposable
        {
            readonly List<MeshCollider> meshes=new List<MeshCollider>();
            public PartSurface(GameObject part)
            {
                foreach(var filter in part.GetComponentsInChildren<MeshFilter>(true))
                {
                    if(!filter.sharedMesh||filter.GetComponentInParent<RepairBolt>())continue;
                    var proxy=new GameObject("Repair / surface query"){hideFlags=HideFlags.HideAndDontSave};
                    proxy.layer=31;
                    proxy.transform.SetPositionAndRotation(filter.transform.position,filter.transform.rotation);
                    proxy.transform.localScale=filter.transform.lossyScale;
                    var collider=proxy.AddComponent<MeshCollider>();collider.sharedMesh=filter.sharedMesh;meshes.Add(collider);
                }
                Physics.SyncTransforms();
            }
            public bool Find(Vector3 wanted,Vector3 normal,Bounds bounds,float size,List<RepairBolt> placed,out Vector3 point,out Vector3 outward)
            {
                point=outward=default;float best=float.PositiveInfinity;
                var a=Vector3.Cross(normal,Mathf.Abs(normal.y)>.9f?Vector3.forward:Vector3.up).normalized;
                var b=Vector3.Cross(normal,a).normalized;
                float depth=Vector3.Dot(bounds.extents,Abs(normal));
                float e1=Vector3.Dot(bounds.extents,Abs(a)),e2=Vector3.Dot(bounds.extents,Abs(b));
                void Probe(Vector3 face)
                {
                    var ray=new Ray(face+normal*.3f,-normal);
                    foreach(var mesh in meshes)
                    {
                        if(!mesh.Raycast(ray,out var hit,depth*2+.6f))continue;
                        bool occupied=false;
                        foreach(var bolt in placed)
                            if(Vector3.Distance(hit.point,bolt.part.transform.TransformPoint(bolt.inLocal))<size*2.5f){occupied=true;break;}
                        if(occupied)continue;
                        float score=(hit.point-wanted).sqrMagnitude;
                        if(score>=best)continue;
                        best=score;foundPoint=hit.point;foundNormal=hit.normal;
                    }
                }
                foundPoint=foundNormal=default;Probe(wanted);
                // Concave and multi-mesh modules may have no surface under a nominal
                // corner. Search nearby real geometry on the same visible face.
                if(float.IsPositiveInfinity(best))
                    for(int x=-3;x<=3;x++)for(int y=-3;y<=3;y++)
                        Probe(bounds.center+normal*depth+a*(e1*x*.28f)+b*(e2*y*.28f));
                if(float.IsPositiveInfinity(best))
                    foreach(var mesh in meshes)
                    {
                        var center=mesh.bounds.center;
                        Probe(center+normal*Vector3.Dot(mesh.bounds.extents,Abs(normal)));
                    }
                // Some modules are open plates: the viewer-facing bounds plane may
                // have no triangles at all. Use another real face instead of air.
                if(float.IsPositiveInfinity(best))
                    foreach(var face in new[]{Vector3.up,Vector3.right,Vector3.left,Vector3.forward,Vector3.back,Vector3.down})
                    {
                        normal=face;depth=Vector3.Dot(bounds.extents,Abs(normal));
                        a=Vector3.Cross(normal,Mathf.Abs(normal.y)>.9f?Vector3.forward:Vector3.up).normalized;b=Vector3.Cross(normal,a).normalized;
                        e1=Vector3.Dot(bounds.extents,Abs(a));e2=Vector3.Dot(bounds.extents,Abs(b));
                        foreach(var mesh in meshes)Probe(mesh.bounds.center+normal*Vector3.Dot(mesh.bounds.extents,Abs(normal)));
                        if(float.IsPositiveInfinity(best))
                            for(int x=-3;x<=3;x++)for(int y=-3;y<=3;y++)
                                Probe(bounds.center+normal*depth+a*(e1*x*.28f)+b*(e2*y*.28f));
                        if(!float.IsPositiveInfinity(best))break;
                    }
                point=foundPoint;outward=foundNormal;
                return !float.IsPositiveInfinity(best);
            }
            Vector3 foundPoint,foundNormal;
            public void Dispose()
            {
                foreach(var mesh in meshes)if(mesh){mesh.gameObject.SetActive(false);Object.Destroy(mesh.gameObject);}
            }
        }

        static bool LongModule(GameObject part)
        {
            var mesh = part.GetComponentInChildren<MeshFilter>(); if (!mesh || !mesh.sharedMesh) return false;
            var size = mesh.sharedMesh.bounds.size;
            float min = Mathf.Min(size.x, Mathf.Min(size.y, size.z)), max = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            return max > (size.x + size.y + size.z - min - max) * 2.25f;
        }

        static void SpawnFace(RepairRobotTarget robot, GameObject part, Bounds bounds, Vector3 viewer, Fastener kind, int count, List<RepairBolt> bolts,PartSurface query)
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
                if(query.Find(point,normal,bounds,size,bolts,out var surface,out var surfaceNormal))
                    bolts.Add(Create(robot,part,surface,surfaceNormal,size,kind));
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

        static List<RepairBolt> SpawnHub(RepairRobotTarget robot, GameObject part, Bounds bounds, Vector3 axle, Fastener kind, List<RepairBolt> bolts,PartSurface query)
        {
            var t1 = Vector3.Cross(axle, Mathf.Abs(axle.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
            var t2 = Vector3.Cross(axle, t1).normalized;
            float depth = Vector3.Dot(bounds.extents, Abs(axle));
            float wheelRadius = Mathf.Min(Vector3.Dot(bounds.extents, Abs(t1)), Vector3.Dot(bounds.extents, Abs(t2)));
            float size = Mathf.Clamp(wheelRadius * 0.09f, 0.015f, 0.05f);
            for (int i = 0; i < 2; i++)
            {
                float angle = i * Mathf.PI + Mathf.PI * .25f;
                var ring = (t1 * Mathf.Cos(angle) + t2 * Mathf.Sin(angle)) * wheelRadius * 0.22f;
                if(query.Find(bounds.center+ring+axle*depth,axle,bounds,size,bolts,out var surface,out var normal))
                    bolts.Add(Create(robot,part,surface,normal,size,kind));
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
            bolt.restRotation = root.transform.localRotation;
            bolt.inLocal = part.transform.InverseTransformPoint(surface + normal * size * 0.2f);
            bolt.renderers = pieces.ToArray();
            RegistryVersion++;
            bolt.Paint(kind == Fastener.Bolt ? RustyBolt : BrassScrew);
            RepairFastenerHighlight.Ensure(root.scene);
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

        void OnEnable() { all.Add(this); RegistryVersion++; }
        void OnDisable() { all.Remove(this); RegistryVersion++; SetAimed(false); }

        internal void AddHighlightMeshes(List<MeshRenderer> meshes)
        {
            if (!NeedsHighlight) return;
            if (renderers != null) foreach (var renderer in renderers) if (renderer is MeshRenderer mesh) meshes.Add(mesh);
        }

        public void SetAimed(bool value)
        {
            value &= NeedsHighlight;
            Highlighted = value;
            if (value && !hover)
            {
                hover = gameObject.AddComponent<MoonThemedGame.Interactions.InteractionOutline>();
                var meshes = new List<MeshRenderer>();
                if (renderers != null) foreach (var renderer in renderers) if (renderer is MeshRenderer mesh) meshes.Add(mesh);
                hover.targets = meshes.ToArray(); hover.widthPixels = 3; hover.outlineColor = new Color(.8f, 1, .86f);
            }
            if (hover) hover.SetHighlighted(value);
        }

        public void Turn(int direction)
        {
            Drive(direction / (float)Turns);
        }

        // Normalized thread travel; tools use the same mechanical limits as debug turns.
        public float Drive(float progress, bool immediate = false)
        {
            if (!Seated || (Tight && progress > 0)) return 0;
            float before = Progress;
            float limit = progress > 0 ? Mathf.Max(before, ProgressLimit) : 1;
            float after = Mathf.Clamp(before + progress, 0, limit);
            if (Mathf.Abs(after - before) < .000001f) return 0;
            turns = after * Turns;
            if (immediate) visualTurns = turns;
            if (part && part.TryGetComponent<RepairPartMount>(out var mount)) mount.FastenerMoved(this, before);
            RefreshHighlightState();
            if (robot) robot.FastenerTurned(part);
            return after - before;
        }

        public float DriveAngle(float degrees, bool immediate = true)
        {
            float before = AngleAt(Progress);
            Drive(ProgressAt(before + degrees) - Progress, immediate);
            return AngleAt(Progress) - before;
        }

        static float AngleAt(float progress) => progress <= SnugProgress ? progress / SnugProgress * 70 :
            progress <= FinalProgress ? 70 + (progress - SnugProgress) / (FinalProgress - SnugProgress) * 32 :
            102 + (progress - FinalProgress) / (1 - FinalProgress) * 18;
        static float ProgressAt(float angle) => angle <= 70 ? Mathf.Clamp01(angle / 70) * SnugProgress :
            angle <= 102 ? SnugProgress + (angle - 70) / 32 * (FinalProgress - SnugProgress) :
            Mathf.Clamp01(FinalProgress + (angle - 102) / 18 * (1 - FinalProgress));

        void Update()
        {
            if (!part) return;
            visualTurns=Mathf.MoveTowards(visualTurns,turns,Time.deltaTime/TurnDuration);
            RefreshHighlightState();
            float t = VisualProgress;
            float depth = t <= FinalProgress ? t / FinalProgress * .94f : .94f + (t - FinalProgress) / (1 - FinalProgress) * .06f;
            transform.localPosition = Vector3.Lerp(outLocal, inLocal, depth);
            transform.localRotation=restRotation*Quaternion.AngleAxis(VisualHeadAngle,Vector3.up);
            var loose = Kind == Fastener.Bolt ? RustyBolt : BrassScrew;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] && !(Kind == Fastener.Screw && i == 2)) renderers[i].material.color = Color.Lerp(loose, Steel, t * t);
            if (Tight && !IsTurning && robot) robot.BoltTightened(part);
        }

        void RefreshHighlightState()
        {
            bool needed = NeedsHighlight;
            if (needed == highlightNeeded) return;
            highlightNeeded = needed; RegistryVersion++;
            if (!needed) SetAimed(false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() { all.Clear(); RegistryVersion = 0; }
    }
}
