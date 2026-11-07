using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Repair
{
    [DisallowMultipleComponent]
    public sealed class BreakableLamp : MonoBehaviour
    {
        public Renderer glass;
        public Light[] lights = Array.Empty<Light>();
        public bool IsBroken { get; private set; }
        Collider glassCollider;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallOnLoad()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneLoaded += SceneLoaded;
            Install();
        }
        static void SceneLoaded(Scene scene, LoadSceneMode mode) => Install();
        public static void Install()
        {
            // Some point lights are parented under a different ceiling lamp in the model.
            // Associate by distance, assigning each light to exactly one glowing glass.
            var lamps = new List<BreakableLamp>();
            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!string.Equals(renderer.name.Split('.')[0].Split('(')[0].Trim(), "lamp", StringComparison.OrdinalIgnoreCase)) continue;
                bool emissive = false;
                foreach (var material in renderer.sharedMaterials)
                    if (material && material.HasProperty("_EmissionColor") && material.GetColor("_EmissionColor").maxColorComponent > .1f) emissive = true;
                if (!emissive) continue;
                var existing = renderer.GetComponentInParent<BreakableLamp>();
                if (existing) { if (!lamps.Contains(existing)) lamps.Add(existing); continue; }
                var host = renderer.transform;
                if (host.parent && host.parent.TryGetComponent<MeshRenderer>(out var metal) &&
                    string.Equals(metal.name.Split('(')[0].Trim(), "lamp", StringComparison.OrdinalIgnoreCase)) host = host.parent;
                var lamp = host.gameObject.AddComponent<BreakableLamp>();
                lamp.glass = renderer; lamp.Configure(); lamps.Add(lamp);
            }
            var associations = new Dictionary<BreakableLamp, List<Light>>();
            foreach (var lamp in lamps) associations[lamp] = new List<Light>();
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type != LightType.Point) continue;
                BreakableLamp nearest = null; float distance = float.PositiveInfinity;
                foreach (var lamp in lamps)
                {
                    if (!lamp.glass || lamp.gameObject.scene != light.gameObject.scene) continue;
                    float d = Vector3.Distance(lamp.glass.bounds.center, light.transform.position);
                    if (d < distance && d < Mathf.Max(.5f, lamp.glass.bounds.size.magnitude * 2)) { nearest = lamp; distance = d; }
                }
                if (nearest) associations[nearest].Add(light);
            }
            foreach (var lamp in lamps) lamp.lights = associations[lamp].ToArray();
        }
        void Start() => Configure();
        void Configure()
        {
            if (!glass) return;
            glassCollider = glass.GetComponent<Collider>();
            if (!glassCollider && glass.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh)
            {
                var collider = glass.gameObject.AddComponent<BoxCollider>();
                var bounds = filter.sharedMesh.bounds; var scale = glass.transform.lossyScale;
                collider.center = bounds.center;
                collider.size = new Vector3(Mathf.Max(bounds.size.x, .025f / Mathf.Max(.001f, Mathf.Abs(scale.x))),
                    Mathf.Max(bounds.size.y, .025f / Mathf.Max(.001f, Mathf.Abs(scale.y))), Mathf.Max(bounds.size.z, .025f / Mathf.Max(.001f, Mathf.Abs(scale.z))));
                glassCollider = collider;
            }
        }
        public bool TryBreak(RaycastHit hit, Ray shot, float scale, Material particles)
        {
            if (IsBroken || !glass) return false;
            if (!glassCollider) Configure();
            // A broad scene box may be hit before the glass mesh. Refine the hit against
            // the actual glass so bullets in the metal post do not break the lamp.
            if (hit.collider != glassCollider)
            {
                if (!(hit.collider is BoxCollider)) return false;
                if (!glassCollider || !glassCollider.Raycast(shot, out var exact, hit.distance + glass.bounds.size.magnitude)) return false;
                hit = exact;
            }
            IsBroken = true;
            foreach (var light in lights) if (light) light.enabled = false;
            ZombieImpact.Emit(hit.point, hit.normal, Mathf.Max(scale, glass.bounds.size.magnitude * .5f), particles, true);
            glass.enabled = false;
            if (glassCollider) glassCollider.enabled = false;
            return true;
        }
    }
}
