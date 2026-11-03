using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MoonThemedGame.Scavenger
{
    // Rigidbody rover with a sphere-cast spring per wheel: W/S throttle, A/D skid-steer.
    // Throttle and steering ramp in and out, so the robot has weight. Each wheel is a sphere
    // of its own radius, so its rim catches the edge of a step and rolls up onto it like a
    // car onto a curb (up to about one wheel radius). Where no collider is found the
    // hangar floor height acts as ground, so the robot never falls through the level.
    [DisallowMultipleComponent]
    public sealed class ScavengerDrive : MonoBehaviour
    {
        sealed class Wheel
        {
            public Transform transform;
            public Vector3 anchor;
            public Vector3 centerInParent, pivotInParent, axleInParent;
            public Quaternion restRotation;
            public float radius, length, spin, visualLift;
            public bool grounded;
            public Vector3 contact, normal;
        }

        [Header("Drive (robot lengths)")]
        public float maxSpeed = 1.3f;
        public float reverseFactor = 0.6f;
        public float acceleration = 1.1f;
        public float turnSpeed = 70;
        [Tooltip("How fast throttle follows the keys, per second.")]
        public float throttleResponse = 1.6f;
        public float steerResponse = 3;
        [Range(0, 1)] public float lateralGrip = 0.6f;
        public float coastDrag = 0.8f;

        [Header("Body")]
        public float mass = 400;
        public float gravity = 4;
        [Tooltip("Suspension travel as a share of the wheel radius.")]
        public float travel = 0.6f;
        [Range(0, 2)] public float dampingRatio = 1.1f;

        public Vector3 localForward = Vector3.forward;
        public bool controllable;

        readonly List<Wheel> wheels = new List<Wheel>();
        readonly RaycastHit[] hits = new RaycastHit[16];
        Rigidbody body;
        Collider[] own;
        float throttle, steer, length, groundHeight, stiffness, damper, upsideDown;

        public float Length => length;
        public Vector3 Forward => transform.TransformDirection(localForward);
        public Vector3 Heading
        {
            get
            {
                var flat = Forward;
                flat.y = 0;
                return flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            }
        }
        public float ForwardSpeed => body ? Vector3.Dot(body.linearVelocity, Forward) : 0;

        public void Setup(Vector3 forward)
        {
            localForward = forward;
            var renderers = GetComponentsInChildren<Renderer>();
            var local = LocalBounds(renderers);
            var world = WorldBounds(renderers);
            length = Mathf.Max(world.size.x, world.size.z);
            groundHeight = world.min.y;

            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            var axle = Vector3.Cross(transform.up, Forward).normalized;
            foreach (var child in GetComponentsInChildren<Transform>())
            {
                if (!child.name.StartsWith("Wheel")) continue;
                var r = child.GetComponentInChildren<Renderer>();
                if (!r) continue;
                var parent = child.parent;
                var center = r.bounds.center;
                wheels.Add(new Wheel
                {
                    transform = child,
                    anchor = transform.InverseTransformPoint(center),
                    centerInParent = parent.InverseTransformPoint(center),
                    pivotInParent = child.localPosition - parent.InverseTransformPoint(center),
                    axleInParent = parent.InverseTransformDirection(axle),
                    restRotation = child.localRotation,
                    radius = r.bounds.extents.y
                });
            }

            float wheelRadius = wheels.Count > 0 ? wheels[0].radius : world.size.y * 0.25f;
            foreach (var wheel in wheels) wheel.length = wheel.radius * travel;
            var hull = gameObject.AddComponent<BoxCollider>();
            float bottom = local.min.y + wheelRadius * 1.5f / Mathf.Max(0.001f, transform.lossyScale.y);
            hull.center = new Vector3(local.center.x, (bottom + local.max.y) * 0.5f, local.center.z);
            hull.size = new Vector3(local.size.x * 0.85f, Mathf.Max(0.01f, local.max.y - bottom), local.size.z * 0.85f);
            hull.sharedMaterial = new PhysicsMaterial("Scavenger hull")
            {
                dynamicFriction = 0, staticFriction = 0,
                frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum
            };
            own = new Collider[] { hull };

            body = gameObject.AddComponent<Rigidbody>();
            body.mass = mass;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearDamping = 0.05f;
            body.angularDamping = 3f;
            body.solverIterations = 12;
            body.centerOfMass = transform.InverseTransformPoint(new Vector3(world.center.x, world.min.y + wheelRadius, world.center.z));

            int count = Mathf.Max(1, wheels.Count);
            float sag = travel * wheelRadius * 0.5f;
            stiffness = mass * gravity / count / Mathf.Max(0.001f, sag);
            damper = 2 * dampingRatio * Mathf.Sqrt(stiffness * mass / count);
        }

        Bounds LocalBounds(Renderer[] renderers)
        {
            bool first = true;
            var result = new Bounds();
            foreach (var r in renderers)
            {
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = transform.InverseTransformPoint(corner);
                    if (first) { result = new Bounds(p, Vector3.zero); first = false; }
                    else result.Encapsulate(p);
                }
            }
            return result;
        }

        static Bounds WorldBounds(Renderer[] renderers)
        {
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds();
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        public void SetFallbackGround(float height) => groundHeight = height;

        public void PlaceAt(Vector3 position, Quaternion rotation)
        {
            throttle = steer = 0;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
        }

        void FixedUpdate()
        {
            if (!body) return;
            float dt = Time.fixedDeltaTime;
            var input = controllable ? ReadInput() : Vector2.zero;
            throttle = Mathf.MoveTowards(throttle, input.y, throttleResponse * dt);
            steer = Mathf.MoveTowards(steer, input.x, steerResponse * dt);

            body.AddForce(Vector3.down * gravity, ForceMode.Acceleration);
            int grounded = 0;
            var up = transform.up;
            foreach (var wheel in wheels)
            {
                Suspension(wheel, up, dt);
                if (wheel.grounded) grounded++;
            }
            if (grounded > 0) Traction(grounded);
            KeepUpright(dt);
        }

        // `length` is how far the wheel centre hangs below its mount point.
        void Suspension(Wheel wheel, Vector3 up, float dt)
        {
            float rest = wheel.radius * travel;
            var mount = transform.TransformPoint(wheel.anchor) + up * rest;
            float lead = wheel.radius;
            var origin = mount + up * lead;
            float previous = wheel.length;
            wheel.grounded = CastGround(origin, -up, wheel.radius * 0.95f, rest * 2 + lead, out float distance, out wheel.contact, out wheel.normal);
            wheel.length = wheel.grounded ? Mathf.Max(0, distance - lead) : rest * 2;
            if (!wheel.grounded) return;
            float compression = rest * 1.5f - wheel.length;
            float velocity = (previous - wheel.length) / dt;
            float force = Mathf.Max(0, stiffness * compression + damper * velocity);
            body.AddForceAtPosition(up * force, wheel.contact);
        }

        bool CastGround(Vector3 origin, Vector3 direction, float radius, float reach, out float distance, out Vector3 contact, out Vector3 normal)
        {
            distance = float.MaxValue;
            contact = origin;
            normal = Vector3.up;
            int count = Physics.SphereCastNonAlloc(origin, radius, direction, hits, reach, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.distance <= 0 || System.Array.IndexOf(own, hit.collider) >= 0 || hit.collider.attachedRigidbody == body) continue;
                if (hit.distance < distance) { distance = hit.distance; contact = hit.point; normal = hit.normal; }
            }
            if (distance < float.MaxValue) return true;
            if (direction.y >= -0.1f) return false;
            float planeDistance = (origin.y - radius - groundHeight) / -direction.y;
            if (planeDistance < 0 || planeDistance > reach) return false;
            distance = planeDistance;
            contact = origin + direction * distance - Vector3.up * radius;
            return true;
        }

        void Traction(int grounded)
        {
            var forward = Forward;
            float speed = Vector3.Dot(body.linearVelocity, forward);
            float limit = maxSpeed * length * (throttle < 0 ? reverseFactor : 1);
            float accel = throttle * acceleration * length;
            if (Mathf.Abs(speed) > limit && Mathf.Sign(speed) == Mathf.Sign(throttle)) accel = 0;
            if (Mathf.Approximately(throttle, 0)) accel = -speed * coastDrag;

            // Side slip is taken from the body's travel, not per wheel, so skid-steering still turns.
            foreach (var wheel in wheels)
            {
                if (!wheel.grounded) continue;
                var drive = Vector3.ProjectOnPlane(forward, wheel.normal).normalized;
                var side = Vector3.Cross(wheel.normal, drive);
                float slip = Vector3.Dot(body.linearVelocity, side);
                var force = drive * accel - side * slip * lateralGrip / Time.fixedDeltaTime;
                body.AddForceAtPosition(force * (body.mass / grounded), wheel.contact);
            }

            var up = transform.up;
            float yaw = Vector3.Dot(body.angularVelocity, up);
            float wanted = steer * turnSpeed * Mathf.Deg2Rad;
            body.AddTorque(up * (wanted - yaw) * 3, ForceMode.Acceleration);
        }

        void KeepUpright(float dt)
        {
            upsideDown = Vector3.Dot(transform.up, Vector3.up) < 0.3f ? upsideDown + dt : 0;
            if (upsideDown < 2.5f) return;
            upsideDown = 0;
            PlaceAt(body.position + Vector3.up * length * 0.5f, Quaternion.LookRotation(Heading) * Quaternion.Inverse(Quaternion.LookRotation(localForward)));
        }

        void LateUpdate()
        {
            var up = transform.up;
            float speed = ForwardSpeed;
            foreach (var wheel in wheels)
            {
                float lift = wheel.radius * travel - wheel.length;
                wheel.visualLift = Mathf.Lerp(wheel.visualLift, lift, 1 - Mathf.Exp(-25 * Time.deltaTime));
                var parent = wheel.transform.parent;
                wheel.spin += speed / Mathf.Max(0.01f, wheel.radius) * Mathf.Rad2Deg * Time.deltaTime;
                var spin = Quaternion.AngleAxis(wheel.spin, wheel.axleInParent);
                var offset = parent.InverseTransformVector(up * wheel.visualLift);
                wheel.transform.localPosition = wheel.centerInParent + offset + spin * wheel.pivotInParent;
                wheel.transform.localRotation = spin * wheel.restRotation;
            }
        }

        static Vector2 ReadInput()
        {
            var input = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var keys = Keyboard.current;
            if (keys == null) return input;
            if (keys.wKey.isPressed) input.y += 1;
            if (keys.sKey.isPressed) input.y -= 1;
            if (keys.dKey.isPressed) input.x += 1;
            if (keys.aKey.isPressed) input.x -= 1;
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKey(KeyCode.W)) input.y += 1;
            if (Input.GetKey(KeyCode.S)) input.y -= 1;
            if (Input.GetKey(KeyCode.D)) input.x += 1;
            if (Input.GetKey(KeyCode.A)) input.x -= 1;
#endif
            return input;
        }
    }
}
