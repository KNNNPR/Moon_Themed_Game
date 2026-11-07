using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    // Visual compliance only: no forces are applied to the robot or the player.
    [DisallowMultipleComponent]
    public sealed class RepairPartMount : MonoBehaviour
    {
        [Min(.1f)] public float fittingDuration = .52f;
        [Range(0, .04f)] public float positionalPlay = .014f;
        [Range(0, 5)] public float angularPlay = 2.2f;
        public bool IsInstalling { get; private set; }
        public float Tightness { get; private set; }
        public float PlayFraction => Mathf.Pow(1 - Tightness, 1.25f);
        internal List<RepairBolt> Fasteners { get; private set; }
        internal float InspectionPlay { get; set; }
        RepairBolt lastSnugged;
        internal bool AllSnug
        {
            get
            {
                if (Fasteners == null || Fasteners.Count == 0) return true;
                foreach (var bolt in Fasteners) if (bolt && bolt.Progress < RepairBolt.SnugProgress - .0001f) return false;
                return true;
            }
        }
        public RepairBolt RecommendedFastener
        {
            get
            {
                if (AllSnug) return null;
                var reference = lastSnugged;
                if (!reference || reference.Progress < RepairBolt.SnugProgress - .0001f)
                {
                    reference = null;
                    foreach (var bolt in Fasteners) if (bolt && bolt.Progress >= RepairBolt.SnugProgress - .0001f) { reference = bolt; break; }
                }
                if (!reference) return null;
                RepairBolt opposite = null; float farthest = -1;
                foreach (var bolt in Fasteners)
                {
                    if (!bolt || bolt.Progress >= RepairBolt.SnugProgress - .0001f) continue;
                    float distance = (bolt.transform.localPosition - reference.transform.localPosition).sqrMagnitude;
                    if (distance > farthest) { farthest = distance; opposite = bolt; }
                }
                return opposite;
            }
        }
        internal float AllowedProgress(RepairBolt bolt)
        {
            if (AllSnug) return 1;
            if (bolt.Progress >= RepairBolt.SnugProgress - .0001f) return RepairBolt.SnugProgress;
            if (OpposedSupport) return RepairBolt.SnugProgress;
            var next = RecommendedFastener;
            return next && next != bolt ? bolt.Progress : RepairBolt.SnugProgress;
        }
        internal void FastenerMoved(RepairBolt bolt, float previous)
        {
            if (previous < RepairBolt.SnugProgress - .0001f && bolt.Progress >= RepairBolt.SnugProgress - .0001f) lastSnugged = bolt;
            if (lastSnugged == bolt && bolt.Progress < RepairBolt.SnugProgress - .0001f) lastSnugged = null;
        }
        bool OpposedSupport
        {
            get
            {
                if (Fasteners == null || Fasteners.Count < 2) return false;
                float fullSpan = 0, supportedSpan = -1;
                for (int i = 0; i < Fasteners.Count; i++)
                    for (int j = i + 1; j < Fasteners.Count; j++)
                    {
                        var a = Fasteners[i]; var b = Fasteners[j]; if (!a || !b) continue;
                        float span = (a.transform.localPosition - b.transform.localPosition).sqrMagnitude;
                        fullSpan = Mathf.Max(fullSpan, span);
                        if (a.Progress >= RepairBolt.SnugProgress - .0001f && b.Progress >= RepairBolt.SnugProgress - .0001f) supportedSpan = Mathf.Max(supportedSpan, span);
                    }
                return supportedSpan >= 0 && supportedSpan >= fullSpan * .85f;
            }
        }

        RepairRobotTarget robot;
        Vector3 home, homeScale, seatOffset, outward, center, startPosition, startScale;
        Quaternion homeRotation, startRotation;
        Collider[] colliders;
        bool[] colliderStates;
        bool initialized;
        float age, size, phase, impactAge = 10;

        internal void Begin(RepairRobotTarget owner, List<RepairBolt> fasteners, Vector3 restPosition,
            Quaternion restRotation, Vector3 offset, Vector3 normal, float worldSize, Bounds geometry, Transform carried)
        {
            robot = owner; Fasteners = fasteners;
            lastSnugged = null;
            home = restPosition; homeRotation = restRotation; homeScale = transform.localScale;
            seatOffset = offset; size = Mathf.Max(.02f, worldSize);
            outward = transform.parent ? transform.parent.InverseTransformVector(normal) : normal;
            center = transform.InverseTransformPoint(geometry.center);
            phase = (GetInstanceID() & 1023) * .017f;
            age = 0; impactAge = 10; Tightness = 0;
            initialized = true; IsInstalling = true;
            var bodies = new List<Collider>();
            foreach (var collider in GetComponentsInChildren<Collider>(true))
                if (!collider.GetComponentInParent<RepairBolt>()) bodies.Add(collider);
            colliders = bodies.ToArray(); colliderStates = new bool[colliders.Length];
            for (int i = 0; i < colliders.Length; i++) { colliderStates[i] = colliders[i].enabled; colliders[i].enabled = false; }

            startRotation = homeRotation * Quaternion.Euler(0, 5, 0);
            startScale = homeScale;
            startPosition = home + offset + outward * (size * .18f);
            if (carried)
            {
                var sourceBounds = GeometryBounds(carried);
                Quaternion relative = carried.rotation;
                // Match mesh frames where available (the source FBX can have rotated part pivots).
                foreach (var targetMesh in GetComponentsInChildren<MeshFilter>(true))
                {
                    if (targetMesh.GetComponentInParent<RepairBolt>()) continue;
                    bool matched = false;
                    foreach (var sourceMesh in carried.GetComponentsInChildren<MeshFilter>(true))
                        if (targetMesh.sharedMesh && targetMesh.sharedMesh == sourceMesh.sharedMesh)
                        {
                            relative = sourceMesh.transform.rotation * Quaternion.Inverse(targetMesh.transform.rotation) * transform.rotation;
                            matched = true; break;
                        }
                    if (matched) break;
                }
                startRotation = transform.parent ? Quaternion.Inverse(transform.parent.rotation) * relative : relative;
                float scale = Mathf.Clamp(sourceBounds.size.magnitude / Mathf.Max(.001f, geometry.size.magnitude), .2f, 5);
                startScale = homeScale * scale;
                Vector3 sourceCenter = transform.parent ? transform.parent.InverseTransformPoint(sourceBounds.center) : sourceBounds.center;
                startPosition = sourceCenter - startRotation * Vector3.Scale(center, startScale);
            }
            ApplyFlight(0);
        }

        internal static Bounds GeometryBounds(Transform root)
        {
            bool found = false; var bounds = new Bounds(root.position, Vector3.zero);
            // Missing slots are inactive while the robot is delivered; renderer.bounds
            // may still describe the old drop position. Derive bounds from mesh frames.
            foreach (var mesh in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mesh.sharedMesh || mesh.GetComponentInParent<RepairBolt>()) continue;
                var local = mesh.sharedMesh.bounds;
                var extents = Abs(mesh.transform.TransformVector(Vector3.right * local.extents.x)) +
                    Abs(mesh.transform.TransformVector(Vector3.up * local.extents.y)) +
                    Abs(mesh.transform.TransformVector(Vector3.forward * local.extents.z));
                var world = new Bounds(mesh.transform.TransformPoint(local.center), extents * 2);
                if (!found) { bounds = world; found = true; } else bounds.Encapsulate(world);
            }
            return bounds;
        }
        internal void BeginSecured(RepairRobotTarget owner,List<RepairBolt> fasteners,Bounds geometry)
        {
            Begin(owner,fasteners,transform.localPosition,transform.localRotation,Vector3.zero,
                RepairBolt.Outward(owner,gameObject,out float size),size,geometry,null);
            IsInstalling=false;RestoreColliders();Secure();
        }
        static Vector3 Abs(Vector3 vector) => new Vector3(Mathf.Abs(vector.x), Mathf.Abs(vector.y), Mathf.Abs(vector.z));

        internal void SetSeatOffset(Vector3 offset) { seatOffset = offset; impactAge = 0; }

        internal void Secure()
        {
            Tightness = 1; impactAge = 10;
            transform.SetLocalPositionAndRotation(home + seatOffset, homeRotation);
            transform.localScale = homeScale;
        }

        internal void ResetMount()
        {
            if (!initialized) return;
            initialized = false; IsInstalling = false;
            InspectionPlay = 0;
            lastSnugged = null;
            RestoreColliders();
            transform.SetLocalPositionAndRotation(home, homeRotation); transform.localScale = homeScale;
        }
        void OnDisable() => ResetMount();

        void RestoreColliders()
        {
            if (colliders == null) return;
            for (int i = 0; i < colliders.Length; i++) if (colliders[i]) colliders[i].enabled = colliderStates[i];
        }

        void ApplyFlight(float progress)
        {
            float approach = Mathf.Clamp01(progress / .78f);
            float ease = 1 - Mathf.Pow(1 - approach, 3);
            Vector3 target = home + seatOffset;
            Vector3 worldUp = transform.parent ? transform.parent.InverseTransformVector(Vector3.up) : Vector3.up;
            var scale = Vector3.Lerp(startScale, homeScale, ease);
            var rotation = Quaternion.Slerp(startRotation, homeRotation, Mathf.SmoothStep(0, 1, approach));
            var sourceCenter = startPosition + startRotation * Vector3.Scale(center, startScale);
            var targetCenter = target + homeRotation * Vector3.Scale(center, homeScale);
            var position = Vector3.Lerp(sourceCenter, targetCenter, ease) - rotation * Vector3.Scale(center, scale) +
                worldUp * (Mathf.Sin(approach * Mathf.PI) * size * .035f);
            if (progress > .78f)
            {
                float stop = Mathf.InverseLerp(.78f, 1, progress);
                position += outward * (Mathf.Sin(stop * Mathf.PI * 2) * Mathf.Exp(-stop * 5) * size * .018f);
            }
            transform.localScale = scale;
            transform.SetLocalPositionAndRotation(position, rotation);
        }

        void LateUpdate()
        {
            if (!initialized || Time.deltaTime <= 0) return;
            age += Time.deltaTime; impactAge += Time.deltaTime;
            if (IsInstalling)
            {
                float t = Mathf.Clamp01(age / fittingDuration); ApplyFlight(t);
                if (t < 1) return;
                IsInstalling = false; RestoreColliders();
                robot.BoltTightened(gameObject);
            }
            float sum = 0, weight = .2f, radius = .0001f; Vector3 pivot = center * weight, pressure = Vector3.zero;
            foreach (var fastener in Fasteners)
            {
                float progress = fastener ? Mathf.Clamp01(fastener.VisualTurns / fastener.Turns) : 1;
                sum += progress;
                if (!fastener) continue;
                var lever = fastener.transform.localPosition - center;
                pressure += lever * progress; radius = Mathf.Max(radius, lever.magnitude);
                float hold = Mathf.Pow(progress, 4) * 2;
                pivot += fastener.transform.localPosition * hold; weight += hold;
            }
            Tightness = Fasteners.Count > 0 ? sum / Fasteners.Count : 1;
            if (Tightness >= .99999f) { Secure(); return; }
            pivot /= weight;
            float play = PlayFraction * (OpposedSupport ? .28f : 1) * (1 + Mathf.Clamp01(InspectionPlay) * 1.4f);
            float sway = Mathf.Sin(age * 2.6f + phase), twist = Mathf.Sin(age * 1.9f + phase * .7f);
            var rotation = Quaternion.AngleAxis(angularPlay * play * sway, Vector3.up) *
                Quaternion.AngleAxis(angularPlay * .65f * play * twist, Vector3.right) * homeRotation;
            Vector3 normal = Quaternion.Inverse(homeRotation) * outward.normalized;
            Vector3 tilt = Vector3.Cross(normal, pressure);
            if (tilt.sqrMagnitude > .00000001f)
                rotation *= Quaternion.AngleAxis(3.2f * pressure.magnitude / (radius * Mathf.Max(1, Fasteners.Count)) * (1 - Tightness), tilt.normalized);
            Vector3 translation = outward * (size * positionalPlay * play * (.35f + .65f * sway));
            Vector3 down = transform.parent ? transform.parent.InverseTransformVector(Vector3.down) : Vector3.down;
            translation += down * (size * positionalPlay * play * (.4f + .15f * twist));
            float kick = Mathf.Exp(-impactAge * 16) * Mathf.Sin(impactAge * 32) * size * .016f;
            translation += outward * kick;
            transform.localScale = homeScale;
            transform.SetLocalPositionAndRotation(home + seatOffset + translation + homeRotation * Vector3.Scale(pivot, homeScale) - rotation * Vector3.Scale(pivot, homeScale), rotation);
        }
    }
}
