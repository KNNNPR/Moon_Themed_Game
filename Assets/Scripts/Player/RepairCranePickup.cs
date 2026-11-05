using MoonThemedGame.Scavenger;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    [DisallowMultipleComponent]
    public sealed class RepairCranePickup : MonoBehaviour
    {
        public Animator craneAnimator;
        public Transform grip;
        public RepairRobotDropper dropper;
        [Range(0.1f, 0.9f)] public float grabAt = 0.62f;
        [Tooltip("Gap in metres between the robot top and the grip when it closes.")]
        [Min(0f)] public float gripClearance = 0.02f;
        [Tooltip("Height of the parked grip above the repair stand, in metres.")]
        [Min(0.1f)] public float parkedGripHeight = 11f;
        [Tooltip("Seconds to lift the attached robot back to the parked height.")]
        [Min(0.1f)] public float liftDuration = 2f;
        [Tooltip("Clearance from the ceiling to the lowest point of the robot before removal.")]
        [Min(0f)] public float ceilingClearance = 0.15f;
        public bool IsBusy { get; private set; }
        public bool IsReady { get; private set; }

        RepairRobotTarget candidate, carried;
        bool attached;
        float liftElapsed;
        Vector3 parkedGrip, contactGrip, exitGrip, appliedCorrection;
        int takeState;

        public static void ConnectScene(RepairRobotDropper source)
        {
            // Explicit room names: merely spawning a crane never starts a collection.
            var host = GameObject.Find("Cran");
            if (!host || host.scene != source.gameObject.scene)
            {
                Debug.LogWarning("Repair crane 'Cran' is missing or inactive in the repair bay scene.", source);
                return;
            }
            var pickup = host.GetComponent<RepairCranePickup>();
            if (!pickup) pickup = host.AddComponent<RepairCranePickup>();
            pickup.dropper = source;
            if (!pickup.craneAnimator) pickup.craneAnimator = host.GetComponentInChildren<Animator>();
            if (!pickup.grip) pickup.grip = host.transform.Find("Armature/Bone/Bone.001/Bone.002/Bone.003/Bone.003_end");
            pickup.InitializeIdle();
            var button = GameObject.Find("Button");
            if (!button || button.scene != source.gameObject.scene)
            {
                Debug.LogWarning("Crane button not found. Name the red button 'Button'.", source);
                return;
            }
            var call = button.GetComponent<CraneCallButton>();
            if (!call) call = button.AddComponent<CraneCallButton>();
            call.crane = pickup;
            call.interactionDistance = 2.5f;
            foreach (var mesh in button.GetComponentsInChildren<MeshFilter>())
                if (mesh.sharedMesh && !mesh.GetComponent<Collider>())
                {
                    var collider = mesh.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = mesh.sharedMesh;
                }
        }

        void InitializeIdle()
        {
            takeState = Animator.StringToHash("Base Layer.Take");
            if (!craneAnimator || !craneAnimator.runtimeAnimatorController || !grip) return;
            craneAnimator.enabled = true;
            craneAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            craneAnimator.Rebind();
            craneAnimator.Update(0f);
            IsReady = craneAnimator.HasState(0, takeState);
            if (!IsReady)
            {
                Debug.LogWarning("Repair crane animator has no Base Layer.Take state.", this);
                return;
            }
            var anchor = dropper.landingPoint ? dropper.landingPoint : dropper.transform;
            parkedGrip = anchor.position + Vector3.up * parkedGripHeight;
            Park();
        }

        void Park()
        {
            RemoveContactCorrection();
            craneAnimator.speed = 0f;
            craneAnimator.Play(takeState, 0, 0f);
            craneAnimator.Update(0f);
            MoveGripTo(parkedGrip);
        }

        public bool TryCollect()
        {
            if (IsBusy || !IsReady || !dropper || !craneAnimator || !grip) return false;
            var robot = dropper.CurrentRobot;
            if (!robot || !robot.TryReserveCollection()) return false;
            if (carried) Destroy(carried.gameObject); // Previous completed delivery.
            candidate = robot;
            attached = false;
            liftElapsed = 0f;
            RemoveContactCorrection();
            IsBusy = true;
            craneAnimator.enabled = true;
            Bounds bounds = RobotBounds(candidate);
            contactGrip = new Vector3(bounds.center.x, bounds.max.y + gripClearance, bounds.center.z);
            Park();
            craneAnimator.speed = 1f;
            return true;
        }

        void Update()
        {
            // Remove last frame's offset before Animator evaluates the authored pose.
            // This also prevents offset accumulation while the game is paused.
            if (IsBusy) RemoveContactCorrection();
        }

        void RemoveContactCorrection()
        {
            if (craneAnimator) craneAnimator.transform.position -= appliedCorrection;
            appliedCorrection = Vector3.zero;
        }

        void MoveGripTo(Vector3 position)
        {
            var offset = position - grip.position;
            craneAnimator.transform.position += offset;
            appliedCorrection += offset;
        }

        void ApplyDescent(float phase, float reach)
        {
            float amount = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phase / reach));
            // The clip animates the claws and arm, while the grip follows the live repair bay.
            MoveGripTo(Vector3.Lerp(parkedGrip, contactGrip, amount));
        }

        static Bounds RobotBounds(RepairRobotTarget robot)
        {
            Bounds bounds = new Bounds(robot.transform.position, Vector3.zero);
            bool first = true;
            foreach (var renderer in robot.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled || renderer.name.StartsWith("Interaction /")) continue;
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        void LateUpdate()
        {
            if (!IsBusy) return;
            if (!candidate || !craneAnimator || !grip) { Cancel(); return; }
            float reach = Mathf.Clamp(grabAt, 0.1f, 0.9f);
            if (attached)
            {
                AdvanceLift(reach);
                return;
            }
            var state = craneAnimator.GetCurrentAnimatorStateInfo(0);
            if (state.fullPathHash != takeState) { Cancel(); return; }
            if (state.normalizedTime < reach)
            {
                ApplyDescent(state.normalizedTime, reach);
                return;
            }

            if (!candidate.IsRepaired) { Cancel(); return; }
            // Grasp at the contact pose even if a frame steps past it.
            RemoveContactCorrection();
            craneAnimator.speed = 0f;
            craneAnimator.Play(takeState, 0, reach);
            craneAnimator.Update(0f);
            MoveGripTo(contactGrip);
            Attach();
            PrepareExit(reach);
        }

        float FindCeilingHeight()
        {
            // Start just above the repaired robot and look for an overhead surface.
            float height = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(contactGrip, Vector3.up, 30f,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.attachedRigidbody || hit.transform.IsChildOf(transform) ||
                    hit.transform.IsChildOf(candidate.transform) || hit.normal.y > -0.5f) continue;
                height = Mathf.Min(height, hit.point.y);
            }
            // Open bays and ceilings without colliders still lift the whole robot past
            // the previous parking plane before removing it.
            return float.IsPositiveInfinity(height) ? parkedGrip.y : height;
        }

        void PrepareExit(float reach)
        {
            float ceilingY = FindCeilingHeight();
            // Measure the robot at the final wrist rotation, not at the pickup pose.
            // Both samples happen in this LateUpdate before rendering.
            RemoveContactCorrection();
            craneAnimator.Play(takeState, 0, 0.999f);
            craneAnimator.Update(0f);
            MoveGripTo(parkedGrip);
            exitGrip = parkedGrip;
            exitGrip.y = Mathf.Max(contactGrip.y + 0.1f,
                parkedGrip.y + ceilingY + ceilingClearance - RobotBounds(candidate).min.y);

            RemoveContactCorrection();
            craneAnimator.Play(takeState, 0, reach);
            craneAnimator.Update(0f);
            MoveGripTo(contactGrip);
        }

        void AdvanceLift(float reach)
        {
            liftElapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(liftElapsed / Mathf.Max(0.1f, liftDuration));
            RemoveContactCorrection();
            // Take loops at 1. Sample the final pose just before the wrap.
            craneAnimator.Play(takeState, 0, Mathf.Lerp(reach, 0.999f, progress));
            craneAnimator.Update(0f);
            MoveGripTo(Vector3.Lerp(contactGrip, exitGrip, Mathf.SmoothStep(0f, 1f, progress)));
            if (progress < 1f) return;

            if (dropper) dropper.ForgetCollectedRobot(candidate);
            candidate.gameObject.SetActive(false);
            Destroy(candidate.gameObject);
            carried = null;
            candidate = null;
            IsBusy = false;
        }

        void Attach()
        {
            var robot = candidate.gameObject;
            var stabilizer = robot.GetComponent<RepairLandingStabilizer>();
            if (stabilizer) stabilizer.enabled = false;
            var drive = robot.GetComponent<ScavengerDrive>();
            if (drive) drive.enabled = false;
            var body = robot.GetComponent<Rigidbody>();
            if (body)
            {
                if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.None;
            }
            foreach (var collider in robot.GetComponentsInChildren<Collider>()) collider.enabled = false;
            candidate.SetHighlighted(false);
            candidate.enabled = false;
            // Preserve position, rotation and world scale exactly at contact.
            // The robot follows the claw from here; no interpolation toward it.
            robot.transform.SetParent(grip, true);
            attached = true;
        }

        void Cancel()
        {
            RemoveContactCorrection();
            if (candidate && !attached) candidate.CancelCollection();
            if (attached && candidate)
            {
                carried = candidate;
                if (dropper) dropper.ForgetCollectedRobot(candidate);
            }
            candidate = null;
            IsBusy = false;
            if (IsReady && craneAnimator && grip) Park();
            else if (craneAnimator) craneAnimator.speed = 0f;
        }

        void OnDisable()
        {
            if (IsBusy) Cancel();
        }
    }
}
