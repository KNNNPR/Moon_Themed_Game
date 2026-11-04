using System.Collections.Generic;
using MoonThemedGame.Scavenger;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Repair
{
    // Next to the repair landing point: salvage unloaded by SCAVENGER appears here.
    [DisallowMultipleComponent, RequireComponent(typeof(RepairRobotDropper))]
    public sealed class RepairBay : MonoBehaviour
    {
        [Tooltip("Where delivered parts appear, relative to the landing point (x right, z forward).")]
        public Vector3 deliveryOffset = new Vector3(2.4f, 0.5f, 0);
        public Vector3 wrenchOffset = new Vector3(2.4f, 0.5f, 1.3f);

        public static RepairBay Active { get; private set; }
        RepairRobotDropper dropper;
        int delivered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallOnLoad()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Install();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install();

        static void Install()
        {
            foreach (var dropper in FindObjectsByType<RepairRobotDropper>(FindObjectsSortMode.None))
                if (dropper.enabled && !dropper.GetComponent<RepairBay>()) dropper.gameObject.AddComponent<RepairBay>();
        }

        void Awake()
        {
            dropper = GetComponent<RepairRobotDropper>();
            if (!Active) Active = this;
        }

        void Start()
        {
            if (FindAnyObjectByType<RepairWrench>() || FindAnyObjectByType<PlayerBody>()) return;
            var catalog = Resources.Load<ShopCatalog>("ShopCatalog");
            if (!catalog || !catalog.wrench) return;
            var wrench = Instantiate(catalog.wrench);
            wrench.name = "Wrench";
            wrench.AddComponent<RepairWrench>().Settle(Point(wrenchOffset), Anchor.position.y);
        }

        Transform Anchor => dropper && dropper.landingPoint ? dropper.landingPoint : transform;
        Vector3 Point(Vector3 offset) => Anchor.position + Quaternion.Euler(0, Anchor.eulerAngles.y, 0) * offset;

        public GameObject[] Parts
        {
            get
            {
                var parts = new List<GameObject>();
                if (dropper && dropper.parts != null)
                    foreach (var part in dropper.parts) if (part != null && part.prefab) parts.Add(part.prefab);
                return parts.ToArray();
            }
        }

        public void Deliver(GameObject prefab)
        {
            if (!prefab) return;
            var scatter = new Vector3(Random.Range(-0.4f, 0.4f), 0.35f * (delivered % 3), Random.Range(-0.7f, 0.7f));
            delivered++;
            RepairPartProp.Create(prefab, Point(deliveryOffset + scatter), Anchor.position.y);
        }

        void OnDestroy() { if (Active == this) Active = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() => Active = null;
    }
}
