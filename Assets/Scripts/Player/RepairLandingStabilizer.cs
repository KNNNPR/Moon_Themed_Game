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
        Vector3 dropStart,dropEnd;
        float dropElapsed,dropDuration;
        public bool IsDescending { get; private set; }

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
            dropStart=body.position;
            dropEnd=new Vector3(dropStart.x,restingHeight+0.12f,dropStart.z);
            dropDuration=Mathf.Clamp((dropStart.y-dropEnd.y)/4.2f,0.35f,1.6f);
            IsDescending=dropStart.y>dropEnd.y+0.05f;
            if(IsDescending)
            {
                drive.enabled=false;
                body.isKinematic=true;
                body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
            }
        }

        void FixedUpdate()
        {
            if (!body || !drive) return;
            float dt = Time.fixedDeltaTime;
            if(IsDescending)
            {
                dropElapsed+=dt;
                float t=Mathf.Clamp01(dropElapsed/dropDuration);
                // Nonzero entry/exit speeds prevent the visible pause of a smoothstep drop.
                float t2=t*t,t3=t2*t;
                float distance=(-2*t3+3*t2)+(t3-2*t2+t)*0.3f+(t3-t2)*0.15f;
                body.MovePosition(Vector3.LerpUnclamped(dropStart,dropEnd,distance));
                if(t>=1)
                {
                    body.position=dropEnd;
                    body.isKinematic=false;
                    body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
                    body.linearVelocity=Vector3.down*((dropStart.y-dropEnd.y)*0.15f/dropDuration);
                    drive.enabled=true;
                    IsDescending=false;
                }
                return;
            }
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
