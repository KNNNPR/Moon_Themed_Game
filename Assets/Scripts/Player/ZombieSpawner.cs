using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Repair
{
    public static class ZombieSpawner
    {
        public static ZombieActor Create(GameObject prefab, Vector3 position, Quaternion rotation, float scale)
        {
            if (!prefab || !prefab.GetComponent<ZombieActor>()) return null;
            // Physics joints capture their scale on activation. Set the suit's game
            // scale before Awake/OnEnable rather than enlarging an active ragdoll.
            var staging = new GameObject("Zombie / spawn staging");
            staging.SetActive(false);
            staging.transform.SetPositionAndRotation(position, rotation);
            staging.transform.localScale = Vector3.one * Mathf.Max(.01f, Mathf.Abs(scale));
            var instance = Object.Instantiate(prefab, staging.transform, false);
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            instance.transform.SetParent(null, true);
            Object.Destroy(staging);
            return instance.GetComponent<ZombieActor>();
        }

        public static ZombieActor SpawnNearOperator()
        {
            var player = Object.FindFirstObjectByType<FirstPersonController>();
            var asset = Resources.Load<GameObject>("Zombie");
            var prefab = asset ? asset.GetComponent<ZombieActor>() : null;
            if (!player || !prefab) return null;
            float unit = Mathf.Max(.1f, PlayerBody.Scale);
            var capsule = player.GetComponent<CharacterController>();
            var eye = capsule ? capsule.bounds.center : player.transform.position + Vector3.up * unit;
            var forward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
            foreach (float distance in new[] { 4f, 6f, 2.5f })
                foreach (float angle in new[] { 0f, -35, 35, -70, 70, 180 })
                {
                    var point = player.transform.position + Quaternion.AngleAxis(angle, Vector3.up) * forward * distance * unit;
                    RaycastHit ground = default; bool found = false;
                    foreach (var hit in Physics.RaycastAll(point + Vector3.up * 3 * unit, Vector3.down, 8 * unit, ~0, QueryTriggerInteraction.Ignore))
                        if (hit.normal.y > .65f && !hit.transform.IsChildOf(player.transform) && (!found || hit.distance < ground.distance)) { ground = hit; found = true; }
                    if (!found) continue;
                    point = ground.point + Vector3.up * .03f * unit;
                    if (Physics.CheckCapsule(point + Vector3.up * .34f * unit, point + Vector3.up * 1.45f * unit, .3f * unit, ~0, QueryTriggerInteraction.Ignore)) continue;
                    var delta = point + Vector3.up * unit - eye;
                    bool blocked = false;
                    foreach (var hit in Physics.RaycastAll(eye, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                        if (!hit.transform.IsChildOf(player.transform)) { blocked = true; break; }
                    if (blocked) continue;
                    var zombie = Create(asset, point, Quaternion.LookRotation(-forward), unit);
                    zombie.name = "Zombie";
                    SceneManager.MoveGameObjectToScene(zombie.gameObject, player.gameObject.scene);
                    return zombie;
                }
            return null;
        }
    }
}
