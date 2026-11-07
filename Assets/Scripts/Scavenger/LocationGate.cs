using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    // A fence gate held shut by a lock: the first press knocks the lock off and swings both leaves
    // open away from the robot; later presses close and reopen it.
    public sealed class LocationGate : MonoBehaviour
    {
        sealed class Leaf
        {
            public Transform transform;
            public Rigidbody body;
            public Vector3 hinge, position;
            public Quaternion rotation;
            public float sign = 1;
        }

        const float OpenAngle = 95, SwingTime = 2.4f, LockDropTime = 0.7f;

        [SerializeField] Transform[] gateLeaves;
        Transform lockPart;

        void Start()
        {
            if (!lockPart && gateLeaves != null && gateLeaves.Length > 0)
                Setup(transform, gateLeaves);
        }
        Leaf[] leaves = new Leaf[0];
        Vector3 lockStart, lockCenter;
        Quaternion lockRotation;
        float lockFall, lockDrop, lockTime = -1, swing, delay;
        bool unlocked, open;
        public bool IsOpen=>open&&swing>=.999f;

        public void Setup(Transform lockTransform, Transform[] leafTransforms)
        {
            gateLeaves = leafTransforms;
            lockPart = lockTransform;
            lockStart = lockPart.position;
            lockRotation = lockPart.rotation;
            var lockBounds = LocationMechanisms.WorldBounds(lockPart);
            lockCenter = lockBounds.center;
            lockDrop = lockBounds.extents.y * 3;
            if (Physics.Raycast(lockBounds.center + Vector3.down * (lockBounds.extents.y + 0.05f), Vector3.down, out var hit, 50, ~0, QueryTriggerInteraction.Ignore))
                lockDrop = lockBounds.min.y - hit.point.y;

            leaves = new Leaf[leafTransforms.Length];
            for (int i = 0; i < leafTransforms.Length; i++)
            {
                var t = leafTransforms[i];
                var outward = LocationMechanisms.WorldBounds(t).center - lockCenter;
                outward.y = 0;
                outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : t.right;
                // The hinge is the leaf's far edge from the lock.
                float far = 0;
                var filter = t.GetComponent<MeshFilter>();
                if (filter && filter.sharedMesh)
                {
                    var b = filter.sharedMesh.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = t.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                        far = Mathf.Max(far, Vector3.Dot(corner - lockCenter, outward));
                    }
                }
                var hinge = lockCenter + outward * far;
                hinge.y = t.position.y;
                leaves[i] = new Leaf { transform = t, hinge = hinge, position = t.position, rotation = t.rotation, body = LocationMechanisms.Kinematic(t.gameObject) };
            }
        }

        public void Press(Vector3 presser)
        {
            if (!unlocked)
            {
                unlocked = true;
                lockTime = 0;
                delay = LockDropTime;
                foreach (var collider in lockPart.GetComponentsInChildren<Collider>()) collider.enabled = false;
                LocationMechanisms.Say("LOCK RELEASED - GATE OPENING");
            }
            else LocationMechanisms.Say(open ? "GATE CLOSING" : "GATE OPENING");
            if (!open) AimAwayFrom(presser);
            open = !open;
        }

        // Each leaf swings to the side of the fence the robot is not on.
        void AimAwayFrom(Vector3 presser)
        {
            foreach (var leaf in leaves)
            {
                var tip = lockCenter - leaf.hinge;
                tip.y = 0;
                var away = Vector3.ProjectOnPlane(lockCenter - presser, Vector3.up);
                var turned = Quaternion.AngleAxis(OpenAngle, Vector3.up) * tip;
                leaf.sign = Vector3.Dot(turned, away) >= 0 ? 1 : -1;
            }
        }

        void Update()
        {
            if (lockTime < 0 || !lockPart) return;
            lockTime += Time.deltaTime;
            float t = Mathf.Clamp01(lockTime / LockDropTime);
            lockFall = t * t;
            lockPart.SetPositionAndRotation(lockStart + Vector3.down * (lockDrop * lockFall),
                Quaternion.AngleAxis(70 * lockFall, leaves.Length > 0 ? Vector3.Cross(Vector3.up, lockCenter - leaves[0].hinge).normalized : Vector3.right) * lockRotation);
            if (t >= 1) lockTime = -1;
        }

        void FixedUpdate()
        {
            if (delay > 0) { delay -= Time.fixedDeltaTime; return; }
            float target = open ? 1 : 0;
            if (Mathf.Approximately(swing, target)) return;
            swing = Mathf.MoveTowards(swing, target, Time.fixedDeltaTime / SwingTime);
            float angle = OpenAngle * LocationMechanisms.Ease(swing);
            foreach (var leaf in leaves)
            {
                var turn = Quaternion.AngleAxis(angle * leaf.sign, Vector3.up);
                leaf.body.MovePosition(leaf.hinge + turn * (leaf.position - leaf.hinge));
                leaf.body.MoveRotation(turn * leaf.rotation);
            }
        }
    }
}
