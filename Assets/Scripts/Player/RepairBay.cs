using System.Collections.Generic;
using MoonThemedGame.Scavenger;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Repair
{
    // SCAVENGER salvage is queued into the pneumatic pipe above the repair room floor.
    [DisallowMultipleComponent, RequireComponent(typeof(RepairRobotDropper))]
    public sealed class RepairBay : MonoBehaviour
    {
        public ScrapTubeDelivery deliveryTube;
        public Vector3 wrenchOffset = new Vector3(2.4f, 0.5f, 1.3f);

        public static RepairBay Active { get; private set; }
        RepairRobotDropper dropper;

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

        public bool Deliver(GameObject prefab)
        {
            if(!prefab)return false;
            if(!deliveryTube)deliveryTube=ScrapTubeDelivery.EnsureInScene(gameObject.scene);
            return deliveryTube && deliveryTube.Enqueue(prefab);
        }

        void OnDestroy() { if (Active == this) Active = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() => Active = null;
    }
}
