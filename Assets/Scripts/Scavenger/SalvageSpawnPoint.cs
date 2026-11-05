using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    // A hand-placed spot where a robot part lies. Once a scene has any, SalvageField uses only these.
    public sealed class SalvageSpawnPoint : MonoBehaviour
    {
        static readonly List<SalvageSpawnPoint> all = new List<SalvageSpawnPoint>();
        public static IReadOnlyList<SalvageSpawnPoint> All => all;

        [Tooltip("Always this part; empty picks a random one.")]
        public GameObject part;

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
