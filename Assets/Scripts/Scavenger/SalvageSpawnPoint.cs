using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    // A hand-placed spot where a robot part lies. Once a scene has any, SalvageField uses only these.
    public sealed class SalvageSpawnPoint : MonoBehaviour
    {
        static readonly List<SalvageSpawnPoint> all = new List<SalvageSpawnPoint>();
        public static IReadOnlyList<SalvageSpawnPoint> All => all;

        [Tooltip("Editor reference only. An active repair order assigns its required part type to selected points.")]
        public GameObject part;
        [TextArea(2,6)] public string routeNotes;
        [Header("Order progression")]
        [Min(0)] public int progressionTier;
        [Min(0)] public float estimatedLoopSeconds=42;
        [Tooltip("Safe without an unopened gate or bridge; can carry required order supplies.")]
        public bool orderRoute=true;
        [Tooltip("Restricted cache: requires the manipulator or an already-open Access Button passage. May supply a required part behind a gate, bridge or ship access mechanism.")]
        public bool requiresManipulator;
        [Tooltip("Earliest order distance tier that may use this optional manipulator cache. Its loop must also fit the battery budget.")]
        [Min(0)] public int minimumOrderTier=4;
        [Tooltip("Scene button that opens access. An already opened passage also works without the manipulator.")]
        public string accessButton;
        public bool shipInterior;
        [Tooltip("Optional route via charge station 2. Includes the longest leg and time for collecting/turning; zero uses the base round trip.")]
        [Min(0)] public float chargerRouteSeconds;
        public float RouteSeconds=>chargerRouteSeconds>0?Mathf.Min(estimatedLoopSeconds,chargerRouteSeconds):estimatedLoopSeconds;

        public bool AccessAvailable
        {
            get
            {
                if(!requiresManipulator||RobotUpgrades.HasArm)return true;
                if(string.IsNullOrEmpty(accessButton))return false;
                foreach(var lever in RobotLever.All)
                    if(lever&&lever.gameObject.scene==gameObject.scene&&lever.name.Equals(accessButton,System.StringComparison.OrdinalIgnoreCase))return lever.PassageOpen;
                return false;
            }
        }

        public SalvagePiece Piece { get; set; }
        public bool Free => !Piece || Piece.Collected;

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1, 0.62f, 0.2f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 3);
            Gizmos.DrawSphere(transform.position + Vector3.up * 3, 0.5f);
            Gizmos.DrawWireSphere(transform.position, 1);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() => all.Clear();
    }
}
