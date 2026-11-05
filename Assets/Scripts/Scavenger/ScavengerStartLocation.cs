using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    // An authored location can override the spawn/evacuation point. The fallback creates
    // transforms only; the actual level supplies its own terrain, floors and obstacles.
    public static class ScavengerStartLocation
    {
        public const string ObjectName = "Scavenger Start Location";
        public const string MarkerName = "Robot Start";

        public static Transform Find()
        {
            var found = GameObject.Find(ObjectName);
            return found ? found.transform : null;
        }

        public static Transform Marker(Transform location) => location ? location.Find(MarkerName) : null;

        public static GameObject CreateMarkerFor(Transform robot, Renderer chargeBuilding)
        {
            var renderers = robot.GetComponentsInChildren<Renderer>();
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(robot.position, Vector3.one);
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var heading = robot.TransformDirection(ScavengerLink.DetectForward(robot, bounds.center));
            heading.y = 0;
            if (heading.sqrMagnitude < 1e-4f) heading = Vector3.forward;
            float groundY = chargeBuilding ? chargeBuilding.bounds.min.y : bounds.min.y;

            var root = new GameObject(ObjectName).transform;
            root.SetPositionAndRotation(new Vector3(robot.position.x, groundY, robot.position.z), Quaternion.LookRotation(heading));
            var marker = new GameObject(MarkerName).transform;
            marker.SetParent(root, false);
            return root.gameObject;
        }
    }
}
