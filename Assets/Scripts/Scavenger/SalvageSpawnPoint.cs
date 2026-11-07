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
        [Tooltip("Only selected for an active order with a manipulator. May supply a required part behind a gate, bridge or ship access mechanism.")]
        public bool requiresManipulator;
        [Tooltip("Earliest order distance tier that may use this optional manipulator cache. Its loop must also fit the battery budget.")]
        [Min(0)] public int minimumOrderTier=4;

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
