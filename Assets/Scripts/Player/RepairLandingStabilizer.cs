using MoonThemedGame.Scavenger;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    // Repair deliveries settle vertically on the stand; they are not driveable rovers.
    [DisallowMultipleComponent]
    public sealed class RepairLandingStabilizer : MonoBehaviour
    {
        Rigidbody body;
        ScavengerDrive drive;
        float restingHeight, contactBand, landingTime, quietTime, originalDamping;
        bool landed;

        public void Initialize(ScavengerDrive suspension, float height, float band)
        {
            drive = suspension;
            body = GetComponent<Rigidbody>();
            restingHeight = height;
            contactBand = Mathf.Max(0.12f, band);
            originalDamping = body.linearDamping;
            // Keep Y free so the existing wheel springs can absorb the impact.
            body.constraints = RigidbodyConstraints.FreezePositionX |
                RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
            body.angularVelocity = Vector3.zero;
        }

        void FixedUpdate()
        {
            if (!body || !drive) return;
            float dt = Time.fixedDeltaTime;
            if (!landed)
            {
                landed = body.position.y <= restingHeight + contactBand &&
                    body.linearVelocity.y <= 0f;
                if (!landed) return;
            }

            landingTime += dt;
            // Let the initial compression read clearly, then damp the remaining bounce.
            float blend = Mathf.Clamp01((landingTime - 0.15f) / 0.65f);
            body.linearDamping = Mathf.Lerp(originalDamping, 6f, blend);
            bool nearSurface = Mathf.Abs(body.position.y - restingHeight) <= contactBand;
            bool quiet = nearSurface && Mathf.Abs(body.linearVelocity.y) < 0.08f;
            quietTime = quiet ? quietTime + dt : 0f;
            if (landingTime < 0.7f || quietTime < 0.25f) return;

            // Preserve the achieved body and wheel poses, without snapping to a target.
            drive.enabled = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            enabled = false;
        }
    }
}
