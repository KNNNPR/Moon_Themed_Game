using System;
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

        GameObject spawned;
        Vector3 restPosition;
        float fallingSpeed;
        bool falling;
        FirstPersonController player;
        System.Random random;

        void Awake()
        {
            random = new System.Random();
            player = FindAnyObjectByType<FirstPersonController>();
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame &&
                Time.timeScale > 0f && Cursor.lockState == CursorLockMode.Locked &&
                player && player.isActiveAndEnabled)
                DropRobot();

            if (!falling || !spawned) return;
            fallingSpeed += gravity * Time.deltaTime;
            Vector3 position = spawned.transform.position;
            position.y = Mathf.Max(restPosition.y, position.y - fallingSpeed * Time.deltaTime);
            spawned.transform.position = position;
            if (position.y <= restPosition.y) falling = false;
        }

        public void DropRobot()
        {
            if (!robotPrefab || parts == null || parts.Length == 0)
            {
                Debug.LogError("Configure the robot prefab and parts on the drop point.", this);
                return;
            }
            if (spawned) { spawned.SetActive(false); Destroy(spawned); }
            Transform anchor = landingPoint ? landingPoint : transform;
            spawned = Instantiate(robotPrefab, anchor.position, anchor.rotation);
            spawned.transform.localScale = Vector3.one * 0.5f;
            SceneManager.MoveGameObjectToScene(spawned, gameObject.scene);
            spawned.name = "Repair robot / current delivery";
            var target = spawned.GetComponent<RepairRobotTarget>();
            if (!target) target = spawned.AddComponent<RepairRobotTarget>();
            try
            {
                target.Initialize(parts, minimumMissingParts, maximumMissingParts,
                    fixedSeed != 0 ? fixedSeed : random.Next());
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                Destroy(spawned);
                return;
            }

            // A controlled delivery drop: no dynamic Rigidbody/non-convex mesh conflicts.
            foreach (var body in spawned.GetComponentsInChildren<Rigidbody>(true))
                body.isKinematic = true;
            foreach (var renderer in spawned.GetComponentsInChildren<MeshRenderer>())
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh || renderer.GetComponent<Collider>()) continue;
                var box = renderer.gameObject.AddComponent<BoxCollider>();
                box.center = filter.sharedMesh.bounds.center;
                box.size = filter.sharedMesh.bounds.size;
            }

            // Land the remaining geometry on the chosen delivery surface.
            var renderers = spawned.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) { Destroy(spawned); return; }
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            // Surviving geometry must never finish below the landing surface.
            restPosition = spawned.transform.position;
            restPosition.x += anchor.position.x - bounds.center.x;
            restPosition.y += anchor.position.y - bounds.min.y + 0.02f;
            restPosition.z += anchor.position.z - bounds.center.z;
            spawned.transform.position = restPosition + Vector3.up * dropHeight;
            fallingSpeed = 0f;
            falling = true;
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
