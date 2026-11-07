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
        public bool IsBusy { get; private set; }
        public bool IsReady { get; private set; }

        RepairRobotTarget candidate, carried;
        bool attached;
        Vector3 contactCorrection, appliedCorrection;
        int takeState, idleState;

        public static void ConnectScene(RepairRobotDropper source)
        {
            // Explicit room names: merely spawning a crane never starts a collection.
            var host = GameObject.Find("Cran");
            if (!host || host.scene != source.gameObject.scene) return;
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

        void Awake()
        {
            if (!craneAnimator) craneAnimator = GetComponentInChildren<Animator>();
            InitializeIdle();
        }

        void InitializeIdle()
        {
            takeState = Animator.StringToHash("Base Layer.Take");
            idleState = Animator.StringToHash("Base Layer.Nothing");
            if (!craneAnimator || !craneAnimator.runtimeAnimatorController) return;
            craneAnimator.enabled = true;
            craneAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            craneAnimator.Rebind();
            craneAnimator.Update(0f);
            IsReady = grip && craneAnimator.HasState(0, takeState);
            if (craneAnimator.HasState(0, idleState))
            {
                craneAnimator.Play(idleState, 0, 0f);
                craneAnimator.Update(0f);
            }
            craneAnimator.speed = 0f;
        }

        public bool TryCollect()
        {
            if (IsBusy || !dropper || !craneAnimator || !grip ||
                !craneAnimator.runtimeAnimatorController || !craneAnimator.HasState(0, takeState)) return false;
            var robot = dropper.CurrentRobot;
            if (!robot || !robot.TryReserveCollection()) return false;
            if (carried) Destroy(carried.gameObject); // Previous completed delivery.
            candidate = robot;
            attached = false;
            RemoveContactCorrection();
            IsBusy = true;
            craneAnimator.enabled = true;
            // Sample the grasp pose before the first rendered animation frame.
            // Move the crane down to the robot instead of moving the robot up to it.
            craneAnimator.speed = 0f;
            craneAnimator.Play(takeState, 0, grabAt);
            craneAnimator.Update(0f);
            float gap = grip.position.y - RobotTop(candidate).y - gripClearance;
            contactCorrection = Vector3.down * Mathf.Max(0f, gap);
            craneAnimator.speed = 1f;
            craneAnimator.Play(takeState, 0, 0f);
            craneAnimator.Update(0f);
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

        void ApplyContactCorrection(float phase)
        {
            float amount = phase <= grabAt
                ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phase / grabAt))
                : 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((phase - grabAt) / (1f - grabAt)));
            appliedCorrection = contactCorrection * amount;
            craneAnimator.transform.position += appliedCorrection;
        }

        static Vector3 RobotTop(RepairRobotTarget robot)
        {
            Bounds bounds = new Bounds(robot.transform.position, Vector3.zero);
            bool first = true;
            foreach (var renderer in robot.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.name.StartsWith("Interaction /")) continue;
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }

        void LateUpdate()
        {
            if (!IsBusy) return;
            if (!candidate || !craneAnimator || !grip) { Cancel(); return; }
            var state = craneAnimator.GetCurrentAnimatorStateInfo(0);
            if (state.fullPathHash != takeState) { Cancel(); return; }
            if (!attached && state.normalizedTime >= grabAt)
            {
                if (!candidate.ReadyForCollection) { Cancel(); return; }
                // Grasp at the sampled contact pose even if a frame steps past it.
                craneAnimator.Play(takeState, 0, grabAt);
                craneAnimator.Update(0f);
                state = craneAnimator.GetCurrentAnimatorStateInfo(0);
                ApplyContactCorrection(grabAt);
                Attach();
            }
            else ApplyContactCorrection(Mathf.Clamp01(state.normalizedTime));
            if (state.normalizedTime < 0.995f) return;
            // Take is a looping asset: explicitly hold its last pose after one cycle.
            RemoveContactCorrection();
            craneAnimator.Play(takeState, 0, 0.999f);
            craneAnimator.Update(0f);
            craneAnimator.speed = 0f;
            if (attached)
            {
                dropper.ForgetCollectedRobot(candidate);
                candidate.GetComponent<AurinkoDismantling>()?.Collected();
                candidate.gameObject.SetActive(false);
                Destroy(candidate.gameObject);
                carried = null;
            }
            else candidate.CancelCollection();
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
            if (craneAnimator) craneAnimator.speed = 0f;
        }

        void OnDisable()
        {
            if (IsBusy) Cancel();
        }
    }
}
