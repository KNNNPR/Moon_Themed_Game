using UnityEngine;
using UnityEngine.AI;

namespace MoonThemedGame.Computer
{
    public sealed class CrazyCosmonautsPursuit : MonoBehaviour
    {
        public Transform target;
        NavMeshPath path;
        Vector3[] corners = System.Array.Empty<Vector3>();
        float nextPath;
        public Vector3 Waypoint()
        {
            if (!target) return transform.position;
            if (path == null) path = new NavMeshPath();
            if (Time.time >= nextPath)
            {
                nextPath = Time.time + .55f;
                if (NavMesh.SamplePosition(transform.position, out var from, 1, NavMesh.AllAreas) &&
                    NavMesh.SamplePosition(target.position, out var to, 1, NavMesh.AllAreas))
                {
                    NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path);
                    corners = path.corners ?? System.Array.Empty<Vector3>();
                }
            }
            for (int i = 1; i < corners.Length; i++)
                if (Vector3.ProjectOnPlane(corners[i] - transform.position, Vector3.up).magnitude > .5f) return corners[i];
            return target.position;
        }
    }
}
