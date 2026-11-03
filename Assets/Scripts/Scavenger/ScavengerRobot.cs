using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    // Remote-driven scavenger: W/S drive, A/D turn on the spot, E uses the
    // closest interactable. Moves kinematically: it keeps its starting height
    // and follows any collider found below it.
    [DisallowMultipleComponent]
    public sealed class ScavengerRobot : MonoBehaviour
    {
        public float maxSpeed = 4;
        public float reverseFactor = 0.55f;
        public float acceleration = 6;
        public float turnSpeed = 90;
        public float wheelRadius = 0.3f;
        [Tooltip("Model-space direction the robot drives; detected from the Headlights child when left at zero.")]
        public Vector3 localForward;

        Transform[] wheels;
        Renderer[] wheelRenderers;
        Collider[] own;
        float speed, rideHeight;
        bool hasGround;

        public bool Connected { get; set; }
        public float Speed => speed;
        public Vector3 Forward => transform.TransformDirection(localForward).normalized;
        public Bounds Bounds { get; private set; }
        public ScavengerInteractable Focus { get; private set; }

        void Awake()
        {
            own = GetComponentsInChildren<Collider>(true);
            var renderers = GetComponentsInChildren<Renderer>();
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(transform.position, Vector3.one);
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            Bounds = bounds;
            if (localForward == Vector3.zero) localForward = DetectForward(bounds.center);

            var found = new System.Collections.Generic.List<Transform>();
            foreach (var child in GetComponentsInChildren<Transform>())
                if (child.name.StartsWith("Wheel")) found.Add(child);
            wheels = found.ToArray();
            wheelRenderers = new Renderer[wheels.Length];
            for (int i = 0; i < wheels.Length; i++) wheelRenderers[i] = wheels[i].GetComponentInChildren<Renderer>();

            hasGround = TryGround(transform.position, out var ground);
            if (hasGround) rideHeight = transform.position.y - ground;
        }

        // The headlights sit at the front of the model; snap to the closest local axis.
        Vector3 DetectForward(Vector3 center)
        {
            Transform lights = null;
            foreach (var child in GetComponentsInChildren<Transform>())
                if (child.name == "Headlights") { lights = child; break; }
            if (!lights) return Vector3.forward;
            var lightRenderer = lights.GetComponentInChildren<Renderer>();
            var local = transform.InverseTransformDirection((lightRenderer ? lightRenderer.bounds.center : lights.position) - center);
            local.y = 0;
            return Mathf.Abs(local.x) > Mathf.Abs(local.z) ? new Vector3(Mathf.Sign(local.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(local.z));
        }

        void Update()
        {
            var input = Connected ? ScavengerInput.Move : Vector2.zero;
            float dt = Time.deltaTime;

            float target = input.y * maxSpeed * (input.y < 0 ? reverseFactor : 1);
            speed = Mathf.MoveTowards(speed, target, acceleration * dt);
            transform.Rotate(Vector3.up, input.x * turnSpeed * dt, Space.World);
            var position = transform.position + Forward * (speed * dt);
            if (hasGround && TryGround(position, out var ground)) position.y = ground + rideHeight;
            transform.position = position;
            SpinWheels(dt);

            Focus = Connected ? FindFocus() : null;
            if (Focus && ScavengerInput.Interact) Focus.Interact(this);
        }

        bool TryGround(Vector3 position, out float height)
        {
            height = 0;
            var hits = Physics.RaycastAll(position + Vector3.up * 2, Vector3.down, 10, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var hit in hits)
            {
                if (System.Array.IndexOf(own, hit.collider) >= 0 || hit.distance >= best) continue;
                best = hit.distance;
                height = hit.point.y;
            }
            return best < float.MaxValue;
        }

        ScavengerInteractable FindFocus()
        {
            ScavengerInteractable best = null;
            float bestDistance = float.MaxValue;
            float body = Mathf.Max(Bounds.extents.x, Bounds.extents.z);
            foreach (var item in ScavengerInteractable.All)
            {
                var offset = item.transform.position - transform.position;
                offset.y = 0;
                float distance = offset.magnitude;
                if (distance > item.reach + body || distance >= bestDistance) continue;
                if (item.GetPrompt(this) == null) continue;
                best = item;
                bestDistance = distance;
            }
            return best;
        }

        void SpinWheels(float dt)
        {
            float angle = speed / Mathf.Max(0.05f, wheelRadius) * Mathf.Rad2Deg * dt;
            var axis = Vector3.Cross(Vector3.up, Forward);
            for (int i = 0; i < wheels.Length; i++)
            {
                var pivot = wheelRenderers[i] ? wheelRenderers[i].bounds.center : wheels[i].position;
                wheels[i].RotateAround(pivot, axis, angle);
            }
        }
    }
}
