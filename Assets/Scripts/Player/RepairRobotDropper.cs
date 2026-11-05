using System;
using MoonThemedGame.Scavenger;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Repair
{
    [DisallowMultipleComponent]
    public sealed class RepairRobotDropper : MonoBehaviour
    {
        public GameObject robotPrefab;
        public RepairPartDefinition[] parts;
        [Tooltip("This transform marks the landing surface, not the robot pivot.")]
        public Transform landingPoint;
        [Min(0.1f)] public float dropHeight = 5f;
        [Min(0.1f)] public float gravity = 9.81f;
        [Min(1)] public int minimumMissingParts = 2;
        [Min(1)] public int maximumMissingParts = 5;
        [Tooltip("Zero gives a new damage pattern each drop; other values are reproducible.")]
        public int fixedSeed;

        [Header("Landing Suspension")]
        [Min(1f)] public float robotMass = 400f;
        [Range(0.1f, 1.5f)] public float suspensionTravel = 0.6f;
        [Range(0.1f, 2f)] public float suspensionDamping = 0.9f;

        static RepairRobotDropper activeDropper;
        int lastDropFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetActiveDropper() => activeDropper = null;

        GameObject spawned;
        RepairRobotTarget orderRobot;
        internal static RepairRobotDropper Active => activeDropper;
        internal bool CanDropOrder => isActiveAndEnabled && activeDropper == this && !orderRobot &&
            (!CurrentRobot || !CurrentRobot.IsBeingCollected);

        internal RepairRobotTarget TryDropOrder(int[] missingSlots)
        {
            if (!CanDropOrder || missingSlots == null || missingSlots.Length == 0) return null;
            orderRobot = SpawnRobot(missingSlots);
            return orderRobot;
        }
        public RepairRobotTarget CurrentRobot => spawned ? spawned.GetComponent<RepairRobotTarget>() : null;
        public void ForgetCollectedRobot(RepairRobotTarget robot)
        {
            if (spawned && robot && spawned == robot.gameObject) spawned = null;
            if (orderRobot == robot) orderRobot = null;
        }
        FirstPersonController player;
        System.Random random;

        void Awake()
        {
            random = new System.Random();
            player = FindAnyObjectByType<FirstPersonController>();

        }

        void OnEnable()
        {
            if (activeDropper && activeDropper != this)
            {
                Debug.LogWarning("Duplicate Repair Robot Drop Point disabled; only one handles F.", this);
                enabled = false;
                return;
            }
            activeDropper = this;
            RepairCranePickup.ConnectScene(this);
        }

        void OnDisable()
        {
            if (activeDropper == this) activeDropper = null;
        }

        void Update()
        {
            if (Keyboard.current != null && MoonThemedGame.Interactions.GameControls.Pressed(MoonThemedGame.Interactions.GameCommand.Repair) &&
                Time.timeScale > 0f && Cursor.lockState == CursorLockMode.Locked &&
                player && player.isActiveAndEnabled)
                DropRobot();


        }

        public void DropRobot()
        {
            if (!orderRobot) SpawnRobot(null);
        }

        RepairRobotTarget SpawnRobot(int[] missingSlots)
        {
            if (!isActiveAndEnabled || activeDropper != this || lastDropFrame == Time.frameCount) return null;
            if (CurrentRobot && CurrentRobot.IsBeingCollected) return null;
            if (!robotPrefab || parts == null || parts.Length == 0)
            {
                Debug.LogError("Configure the robot prefab and parts on the drop point.", this);
                return null;
            }
            lastDropFrame = Time.frameCount;
            Transform anchor = landingPoint ? landingPoint : transform;
            var replacement = Instantiate(robotPrefab, anchor.position, anchor.rotation);
            replacement.transform.localScale = Vector3.one * 0.5f;
            SceneManager.MoveGameObjectToScene(replacement, gameObject.scene);
            replacement.name = "Repair robot / current delivery";
            var target = replacement.GetComponent<RepairRobotTarget>();
            if (!target) target = replacement.AddComponent<RepairRobotTarget>();
            try
            {
                target.Initialize(parts, minimumMissingParts, maximumMissingParts,
                    fixedSeed != 0 ? fixedSeed : random.Next(), missingSlots);
                // Land the remaining geometry on the chosen delivery surface.
                var renderers = replacement.GetComponentsInChildren<MeshRenderer>();
                if (renderers.Length == 0) { Destroy(replacement); return null; }
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                // Surviving geometry must never finish below the landing surface.
                Vector3 restPosition = replacement.transform.position;
                restPosition.x += anchor.position.x - bounds.center.x;
                restPosition.y += anchor.position.y - bounds.min.y + 0.02f;
                restPosition.z += anchor.position.z - bounds.center.z;
                replacement.transform.position = restPosition + Vector3.up * dropHeight;
                // Initialize only AFTER damage, scaling and positioning: missing wheels
                // must not contribute springs, and wheel radii must be in world metres.
                var drive = replacement.AddComponent<ScavengerDrive>();
                drive.controllable = false;
                drive.mass = robotMass;
                drive.gravity = gravity;
                drive.travel = suspensionTravel;
                drive.dampingRatio = suspensionDamping;
                drive.Setup(Vector3.forward);
                drive.SetFallbackGround(anchor.position.y);
                drive.PlaceAt(replacement.transform.position, replacement.transform.rotation);
                var stabilizer = replacement.AddComponent<RepairLandingStabilizer>();
                stabilizer.Initialize(drive, restPosition.y, bounds.size.y * 0.35f);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                replacement.SetActive(false);
                Destroy(replacement);
                return null;
            }

            // Land the remaining geometry on the chosen delivery surface.
            var renderers = spawned.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) { Destroy(spawned); return; }
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            // Surviving geometry must never finish below the landing surface.
            Vector3 restPosition = spawned.transform.position;
            restPosition.x += anchor.position.x - bounds.center.x;
            restPosition.y += anchor.position.y - bounds.min.y + 0.02f;
            restPosition.z += anchor.position.z - bounds.center.z;
            spawned.transform.position = restPosition + Vector3.up * dropHeight;
            // Initialize only AFTER damage, scaling and positioning: missing wheels
            // must not contribute springs, and wheel radii must be in world metres.
            var drive = spawned.AddComponent<ScavengerDrive>();
            drive.controllable = false;
            drive.mass = robotMass;
            drive.gravity = gravity;
            drive.travel = suspensionTravel;
            drive.dampingRatio = suspensionDamping;
            drive.Setup(Vector3.forward);
            drive.PlaceAt(spawned.transform.position, spawned.transform.rotation);
            var stabilizer = spawned.AddComponent<RepairLandingStabilizer>();
            stabilizer.Initialize(drive, restPosition.y, bounds.size.y * 0.35f);
        }

        void OnDrawGizmosSelected()
        {
            Transform anchor = landingPoint ? landingPoint : transform;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(anchor.position, new Vector3(2f, 0.05f, 2f));
            Gizmos.DrawLine(anchor.position, anchor.position + Vector3.up * dropHeight);
            Gizmos.DrawWireSphere(anchor.position + Vector3.up * dropHeight, 0.25f);
        }

        void OnDestroy()
        {
            if (spawned) Destroy(spawned);
        }
    }
}
