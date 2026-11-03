using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MoonThemedGame.Scavenger
{
    // Kinematic tank drive over the LunarSurface: W/S throttle, A/D turn. Follows the ground
    // height and tilt, slides along rocks and shelter legs.
    [DisallowMultipleComponent]
    public sealed class ScavengerDrive : MonoBehaviour
    {
        [Tooltip("Robot lengths per second.")]
        public float speed = 1.6f;
        public float reverseFactor = 0.6f;
        [Tooltip("Robot lengths per second squared.")]
        public float acceleration = 2.5f;
        public float turnSpeed = 75;
        public float tiltSharpness = 8;

        public LunarSurface surface;
        public Vector3 localForward = Vector3.forward;
        public bool controllable;
        // Set by the link when the uplink is lost under cover: the robot backs out on its own.
        public Vector3? autopilotTarget;

        Collider[] own;
        Transform[] wheels;
        Renderer[] wheelRenderers;
        float velocity, heading, length, rideHeight, probeRadius, probeHeight;

        public float Velocity => velocity;
        public float Length => length;
        public Vector3 Heading => Quaternion.Euler(0, heading, 0) * Vector3.forward;

        public void Setup(LunarSurface ground, Vector3 forward)
        {
            surface = ground;
            localForward = forward;
            own = GetComponentsInChildren<Collider>(true);
            var found = new System.Collections.Generic.List<Transform>();
            foreach (var child in GetComponentsInChildren<Transform>())
                if (child.name.StartsWith("Wheel")) found.Add(child);
            wheels = found.ToArray();
            wheelRenderers = new Renderer[wheels.Length];
            for (int i = 0; i < wheels.Length; i++) wheelRenderers[i] = wheels[i].GetComponentInChildren<Renderer>();

            var bounds = WorldBounds();
            length = Mathf.Max(bounds.size.x, bounds.size.z);
            rideHeight = transform.position.y - bounds.min.y;
            probeRadius = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.8f;
            probeHeight = bounds.center.y - transform.position.y;
            heading = Quaternion.LookRotation(Flat(transform.TransformDirection(localForward))).eulerAngles.y;
        }

        Bounds WorldBounds()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(transform.position, Vector3.one);
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0;
            return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward;
        }

        public void PlaceAt(Vector3 position, float yaw)
        {
            heading = yaw;
            velocity = 0;
            var ground = surface ? surface.WorldHeight(position) : position.y;
            transform.position = new Vector3(position.x, ground + rideHeight, position.z);
            transform.rotation = Orientation(surface ? surface.WorldNormal(position) : Vector3.up);
        }

        Quaternion Orientation(Vector3 normal) =>
            Quaternion.LookRotation(Vector3.ProjectOnPlane(Heading, normal), normal) * Quaternion.Inverse(Quaternion.LookRotation(localForward, Vector3.up));

        void Update()
        {
            if (!surface) return;
            float dt = Time.deltaTime;
            Vector2 input = controllable ? ReadInput() : Vector2.zero;
            if (autopilotTarget.HasValue) input = Autopilot(autopilotTarget.Value);

            heading += input.x * turnSpeed * dt;
            float target = input.y * speed * length * (input.y < 0 ? reverseFactor : 1);
            velocity = Mathf.MoveTowards(velocity, target, acceleration * length * dt);

            var move = Heading * (velocity * dt);
            move = Slide(move);
            var position = surface.ClampInside(transform.position + move, length);
            position.y = surface.WorldHeight(position) + rideHeight;
            transform.position = position;
            var normal = surface.WorldNormal(position);
            transform.rotation = Quaternion.Slerp(transform.rotation, Orientation(normal), 1 - Mathf.Exp(-tiltSharpness * dt));
            SpinWheels(dt);
        }

        Vector2 Autopilot(Vector3 target)
        {
            var offset = Flat(target - transform.position);
            float distance = Vector3.Distance(new Vector3(target.x, 0, target.z), new Vector3(transform.position.x, 0, transform.position.z));
            if (distance < length * 0.3f) return Vector2.zero;
            // Reverse out the way it came in: steer so that the back points at the target.
            float angle = Vector3.SignedAngle(-Heading, offset, Vector3.up);
            return new Vector2(Mathf.Clamp(angle / 30, -1, 1), Mathf.Abs(angle) < 60 ? -1 : 0);
        }

        Vector3 Slide(Vector3 move)
        {
            float distance = move.magnitude;
            if (distance < 1e-5f) return move;
            var origin = transform.position + Vector3.up * probeHeight;
            for (int pass = 0; pass < 2; pass++)
            {
                var direction = move.normalized;
                bool blocked = false;
                foreach (var hit in Physics.SphereCastAll(origin, probeRadius, direction, move.magnitude + 0.05f * length, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (System.Array.IndexOf(own, hit.collider) >= 0 || hit.transform.name == "Ground" || hit.distance <= 0) continue;
                    var normal = Flat(hit.normal);
                    move = Vector3.ProjectOnPlane(move, normal);
                    move.y = 0;
                    blocked = true;
                    break;
                }
                if (!blocked) break;
                if (pass == 1) return Vector3.zero;
            }
            if (Mathf.Abs(Vector3.Dot(move.normalized, Heading)) < 0.2f) velocity *= 0.5f;
            return move;
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

        void SpinWheels(float dt)
        {
            if (wheels == null) return;
            float radius = Mathf.Max(0.05f, length * 0.12f);
            float angle = velocity / radius * Mathf.Rad2Deg * dt;
            var axis = Vector3.Cross(transform.up, transform.TransformDirection(localForward));
            for (int i = 0; i < wheels.Length; i++)
            {
                var pivot = wheelRenderers[i] ? wheelRenderers[i].bounds.center : wheels[i].position;
                wheels[i].RotateAround(pivot, axis, angle);
            }
        }
    }
}
