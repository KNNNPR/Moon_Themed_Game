using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    // Manipulator arm on the robot: reaches the lever's grip with CCD IK over the model's bones,
    // holds it while the lever swings, then lets go and folds back.
    public sealed class RobotArm : MonoBehaviour
    {
        enum Phase { Idle, Reach, Hold, Release }

        public float reachTime = 0.7f;
        public float releaseTime = 0.6f;
        [Range(1, 30)] public int iterations = 12;

        readonly List<Transform> joints = new List<Transform>();
        Quaternion[] rest;
        Transform tip;
        RobotLever lever;
        Phase phase;
        float time, weight;

        public bool Busy => phase != Phase.Idle;
        public float Length { get; private set; }
        Vector3 Base => joints.Count > 0 ? joints[0].position : transform.position;

        void Awake()
        {
            joints.Add(transform);
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "Bone.002_end") { tip = t; break; }
            if (tip)
            {
                var chain = new List<Transform>();
                for (var t = tip.parent; t && t != transform && t.name.StartsWith("Bone"); t = t.parent) chain.Add(t);
                chain.Reverse();
                joints.AddRange(chain);
            }
            else tip = CreateTip();
            rest = new Quaternion[joints.Count];
            for (int i = 0; i < joints.Count; i++) rest[i] = joints[i].localRotation;
        }

        void Start() => Length = MeasureLength();

        // Without bones the farthest point of the model along its forward axis is the claw.
        Transform CreateTip()
        {
            var point = transform.position;
            float best = float.MinValue;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                var b = r.bounds;
                var candidate = b.center + transform.forward * Vector3.Dot(b.extents, new Vector3(Mathf.Abs(transform.forward.x), Mathf.Abs(transform.forward.y), Mathf.Abs(transform.forward.z)));
                float along = Vector3.Dot(candidate - transform.position, transform.forward);
                if (along > best) { best = along; point = candidate; }
            }
            var marker = new GameObject("Arm Tip").transform;
            marker.SetParent(transform, true);
            marker.position = point;
            return marker;
        }

        float MeasureLength()
        {
            float length = 0;
            for (int i = 1; i < joints.Count; i++) length += Vector3.Distance(joints[i - 1].position, joints[i].position);
            length += Vector3.Distance(joints[joints.Count - 1].position, tip.position);
            return Mathf.Max(length, 0.01f);
        }

        public bool CanReach(RobotLever target) => target && Vector3.Distance(Base, target.GripPosition) <= Length * 0.97f;

        public void Pull(RobotLever target)
        {
            if (Busy || !target) return;
            lever = target;
            phase = Phase.Reach;
            time = 0;
        }

        void LateUpdate()
        {
            for (int i = 0; i < joints.Count; i++) joints[i].localRotation = rest[i];
            if (phase == Phase.Idle) return;
            time += Time.deltaTime;
            switch (phase)
            {
                case Phase.Reach:
                    weight = Smooth(time / reachTime);
                    if (time >= reachTime) { lever.Pull(); phase = Phase.Hold; time = 0; }
                    break;
                case Phase.Hold:
                    weight = 1;
                    if (time >= lever.pullTime + 0.15f) { phase = Phase.Release; time = 0; }
                    break;
                case Phase.Release:
                    weight = 1 - Smooth(time / releaseTime);
                    if (time >= releaseTime) { phase = Phase.Idle; weight = 0; lever = null; return; }
                    break;
            }
            if (!lever) { phase = Phase.Idle; return; }

            var solved = Solve(lever.GripPosition);
            for (int i = 0; i < joints.Count; i++)
                joints[i].localRotation = Quaternion.Slerp(rest[i], solved[i], weight);
        }

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3 - 2 * t);
        }

        // Cyclic coordinate descent from the rest pose; the base only turns around the robot's up axis.
        Quaternion[] Solve(Vector3 target)
        {
            var up = transform.parent ? transform.parent.up : Vector3.up;
            for (int pass = 0; pass < iterations; pass++)
            {
                for (int i = joints.Count - 1; i >= 0; i--)
                {
                    var joint = joints[i];
                    var toTip = tip.position - joint.position;
                    var toTarget = target - joint.position;
                    if (i == 0)
                    {
                        toTip = Vector3.ProjectOnPlane(toTip, up);
                        toTarget = Vector3.ProjectOnPlane(toTarget, up);
                    }
                    if (toTip.sqrMagnitude < 1e-8f || toTarget.sqrMagnitude < 1e-8f) continue;
                    var turn = Quaternion.FromToRotation(toTip, toTarget);
                    turn.ToAngleAxis(out float angle, out var axis);
                    if (angle > 180) angle -= 360;
                    turn = Quaternion.AngleAxis(Mathf.Clamp(angle, -25, 25), axis);
                    joint.rotation = turn * joint.rotation;
                }
                if ((tip.position - target).sqrMagnitude < 1e-6f) break;
            }
            var result = new Quaternion[joints.Count];
            for (int i = 0; i < joints.Count; i++) result[i] = joints[i].localRotation;
            for (int i = 0; i < joints.Count; i++) joints[i].localRotation = rest[i];
            return result;
        }
    }
}
