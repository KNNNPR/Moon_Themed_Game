using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    // The shaft lift: either button sends it to the other end, or reverses it mid-way.
    public sealed class LocationPlatform : MonoBehaviour
    {
        const float TravelTime = 9, Smoothing = 1.2f;

        Rigidbody body;
        Vector3 bottom, top;
        float position, velocity, target;

        public bool AtTop => target > 0.5f;

        public void Setup(Transform topButton, Transform bottomButton, Transform topGround)
        {
            bottom = transform.position;
            var bounds = LocationMechanisms.WorldBounds(transform);
            float surface = topGround
                ? LocationMechanisms.WorldBounds(topGround).max.y
                : topButton.position.y - (bottomButton.position.y - bounds.max.y);
            top = bottom + Vector3.up * Mathf.Max(0, surface - bounds.max.y);
            body = LocationMechanisms.Kinematic(gameObject);
        }

        public void Press()
        {
            target = target > 0.5f ? 0 : 1;
            LocationMechanisms.Say(target > 0.5f ? "PLATFORM GOING UP" : "PLATFORM GOING DOWN");
        }

        void FixedUpdate()
        {
            if (!body) return;
            if (!GetComponent<Collider>())
            {
                var box = gameObject.AddComponent<BoxCollider>();
                var filter = GetComponent<MeshFilter>();
                if (filter && filter.sharedMesh) { box.center = filter.sharedMesh.bounds.center; box.size = filter.sharedMesh.bounds.size; }
            }
            if (Mathf.Abs(position - target) < 1e-4f && Mathf.Abs(velocity) < 1e-4f) return;
            position = Mathf.SmoothDamp(position, target, ref velocity, Smoothing, 1 / TravelTime, Time.fixedDeltaTime);
            body.MovePosition(Vector3.Lerp(bottom, top, position));
        }
    }
}
