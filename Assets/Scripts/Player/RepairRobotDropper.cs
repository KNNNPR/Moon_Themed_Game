using System;
using MoonThemedGame.Scavenger;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Repair
{
    [DisallowMultipleComponent]
    public sealed class RepairRobotDropper : MonoBehaviour
    {
        public GameObject robotPrefab;
        public RepairPartDefinition[] parts;
        [Tooltip("Optional override; otherwise loads Resources/RepairRobotCatalog.")]
        public RepairRobotCatalog robotCatalog;
        RepairRobotDefinition defaultDefinition;
        public RepairRobotDefinition DefaultDefinition
        {
            get
            {
                if (!defaultDefinition)
                {
                    defaultDefinition = ScriptableObject.CreateInstance<RepairRobotDefinition>();
                    defaultDefinition.hideFlags = HideFlags.HideAndDontSave;
                }
                defaultDefinition.displayName = "SCAV_BOT";
                defaultDefinition.prefab = robotPrefab;
                defaultDefinition.parts = parts;
                return defaultDefinition;
            }
        }
        public System.Collections.Generic.List<RepairRobotDefinition> RobotTypes
        {
            get
            {
                var result = new System.Collections.Generic.List<RepairRobotDefinition>();
                if (DefaultDefinition.IsValid) result.Add(DefaultDefinition);
                var catalog = robotCatalog ? robotCatalog : Resources.Load<RepairRobotCatalog>("RepairRobotCatalog");
                if (catalog && catalog.robots != null)
                    foreach (var robot in catalog.robots)
                        if (robot && robot.IsValid && robot.prefab != robotPrefab && !result.Contains(robot)) result.Add(robot);
                return result;
            }
        }
        public GameObject[] AllParts
        {
            get
            {
                var result = new System.Collections.Generic.List<GameObject>();
                foreach (var robot in RobotTypes) RepairRobotDefinition.AppendParts(result, robot.parts);
                return result.ToArray();
            }
        }
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
            => TryDropOrder(missingSlots, DefaultDefinition);

        internal RepairRobotTarget TryDropOrder(int[] missingSlots, RepairRobotDefinition definition)
        {
            if (!CanDropOrder || missingSlots == null) return null;
            orderRobot = SpawnRobot(missingSlots, definition);
            return orderRobot;
        }
        internal RepairRobotTarget ReplaceOrderForDebug(int[] missingSlots,RepairRobotDefinition definition)
        {
            if(!isActiveAndEnabled||activeDropper!=this||(CurrentRobot&&CurrentRobot.IsBeingCollected))return null;
            var previous=orderRobot;int previousFrame=lastDropFrame;
            orderRobot=null;lastDropFrame=-1;
            var replacement=TryDropOrder(missingSlots,definition);
            if(!replacement){orderRobot=previous;lastDropFrame=previousFrame;}
            return replacement;
        }
        public RepairRobotTarget CurrentRobot => spawned ? spawned.GetComponent<RepairRobotTarget>() : null;
        public void ForgetCollectedRobot(RepairRobotTarget robot)
        {
            if (spawned && robot && spawned == robot.gameObject) spawned = null;
            if (orderRobot == robot) orderRobot = null;
        }
        System.Random random;

        void Awake() => random = new System.Random();

        void OnEnable()
        {
            if (activeDropper && activeDropper != this)
            {
                Debug.LogWarning("Duplicate Repair Robot Drop Point disabled; only one handles repair deliveries.", this);
                enabled = false;
                return;
            }
            activeDropper = this;
            if(gameObject.scene.isLoaded)MoonThemedGame.Scavenger.RepairBayFogVolume.EnsureFor(this);
            RepairCranePickup.ConnectScene(this);
        }

        void OnDisable()
        {
            if (activeDropper == this) activeDropper = null;
        }

        void Start()=>MoonThemedGame.Scavenger.RepairBayFogVolume.EnsureFor(this);

        RepairRobotTarget SpawnRobot(int[] missingSlots, RepairRobotDefinition definition)
        {
            if (!isActiveAndEnabled || activeDropper != this || lastDropFrame == Time.frameCount) return null;
            if (CurrentRobot && CurrentRobot.IsBeingCollected) return null;
            if (!definition || !definition.IsValid)
            {
                Debug.LogError("Configure the robot prefab and parts on the drop point.", this);
                return null;
            }
            lastDropFrame = Time.frameCount;
            Transform anchor = landingPoint ? landingPoint : transform;
            var replacement = Instantiate(definition.prefab, anchor.position, anchor.rotation);
            replacement.transform.localScale = Vector3.one * definition.deliveryScale;
            SceneManager.MoveGameObjectToScene(replacement, gameObject.scene);
            replacement.name = "Repair robot / " + definition.DisplayName;
            var target = replacement.GetComponent<RepairRobotTarget>();
            if (!target) target = replacement.AddComponent<RepairRobotTarget>();
            try
            {
                target.Definition = definition;
                target.Initialize(definition.parts, minimumMissingParts, maximumMissingParts,
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

            if (spawned) { spawned.SetActive(false); Destroy(spawned); }
            spawned = replacement;
            return target;
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
            if (defaultDefinition) Destroy(defaultDefinition);
        }
    }
}
