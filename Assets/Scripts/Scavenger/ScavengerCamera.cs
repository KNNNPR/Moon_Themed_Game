using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    // Chase camera behind the robot: the "live feed" the operator watches.
    public sealed class ScavengerCamera : MonoBehaviour
    {
        public ScavengerRobot target;
        public Vector3 offset = new Vector3(0, 2.4f, -5.5f);
        public float lookHeight = 0.8f;
        public float followSharpness = 7;
        public float turnSharpness = 4;
        public float clearance = 0.3f;

        float yaw;
        Collider[] own;

        float TargetYaw => Quaternion.LookRotation(target.Forward, Vector3.up).eulerAngles.y;

        public void Snap()
        {
            if (!target) return;
            yaw = TargetYaw;
            transform.position = Desired(out var look);
            transform.LookAt(look);
        }

        Vector3 Desired(out Vector3 look)
        {
            own ??= target.GetComponentsInChildren<Collider>(true);
            var pivot = target.transform.position;
            look = pivot + Vector3.up * lookHeight;
            var wanted = pivot + Quaternion.Euler(0, yaw, 0) * offset;
            var direction = wanted - look;
            float distance = direction.magnitude;
            foreach (var hit in Physics.SphereCastAll(look, clearance, direction / distance, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (System.Array.IndexOf(own, hit.collider) >= 0 || hit.distance <= 0) continue;
                distance = Mathf.Min(distance, hit.distance);
            }
            return look + direction.normalized * distance;
        }

        void LateUpdate()
        {
            if (!target) return;
            float dt = Time.deltaTime;
            yaw = Mathf.LerpAngle(yaw, TargetYaw, 1 - Mathf.Exp(-turnSharpness * dt));
            var position = Desired(out var look);
            transform.position = Vector3.Lerp(transform.position, position, 1 - Mathf.Exp(-followSharpness * dt));
            transform.rotation = Quaternion.LookRotation(look - transform.position, Vector3.up);
        }
    }
}
