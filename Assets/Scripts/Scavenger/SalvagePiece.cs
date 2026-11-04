using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    // One robot part lying on the regolith, with a blinking beacon so it shows up through dust.
    [DisallowMultipleComponent]
    public sealed class SalvagePiece : MonoBehaviour
    {
        static readonly List<SalvagePiece> all = new List<SalvagePiece>();
        public static IReadOnlyList<SalvagePiece> All => all;

        public GameObject prefab;
        public float radius = 0.3f;
        Light beacon;
        Transform target;
        Vector3 startPosition, startScale;
        float collectTime = -1, phase;
        const float CollectDuration = 0.5f;

        public bool Collected => collectTime >= 0;

        public static SalvagePiece Create(GameObject prefab, Vector3 ground, Vector3 normal, float scale)
        {
            var instance = Instantiate(prefab);
            instance.name = "Salvage / " + prefab.name;
            foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            instance.transform.localScale *= scale;
            var tilt = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-25f, 25f));
            instance.transform.SetPositionAndRotation(ground, tilt);

            var renderers = instance.GetComponentsInChildren<Renderer>();
            var bounds = new Bounds(ground, Vector3.one * 0.2f);
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            }
            // Half buried in the dust.
            instance.transform.position += Vector3.up * (ground.y - bounds.min.y - bounds.size.y * 0.25f);

            var piece = instance.AddComponent<SalvagePiece>();
            piece.prefab = prefab;
            piece.radius = Mathf.Max(0.15f, new Vector2(bounds.extents.x, bounds.extents.z).magnitude);
            var light = new GameObject("Beacon").AddComponent<Light>();
            light.transform.SetParent(instance.transform, false);
            light.transform.position = instance.transform.position + Vector3.up * (bounds.size.y * 0.75f + 0.15f);
            light.type = LightType.Point;
            light.color = new Color(1, 0.62f, 0.2f);
            light.range = Mathf.Max(2.5f, piece.radius * 6);
            light.shadows = LightShadows.None;
            piece.beacon = light;
            piece.phase = Random.value;
            return piece;
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public void Collect(Transform into)
        {
            if (Collected) return;
            target = into;
            collectTime = 0;
            startPosition = transform.position;
            startScale = transform.localScale;
            if (beacon) beacon.enabled = false;
            all.Remove(this);
        }

        void Update()
        {
            if (!Collected)
            {
                if (beacon) beacon.intensity = Mathf.Repeat(Time.time * 0.8f + phase, 1) < 0.18f ? 2.4f : 0.35f;
                return;
            }
            collectTime += Time.deltaTime;
            float t = Mathf.Clamp01(collectTime / CollectDuration);
            float smooth = t * t * (3 - 2 * t);
            var end = target ? target.position : startPosition;
            transform.position = Vector3.Lerp(startPosition, end, smooth) + Vector3.up * Mathf.Sin(t * Mathf.PI) * radius * 1.5f;
            transform.localScale = startScale * Mathf.Lerp(1, 0.15f, smooth);
            if (t >= 1) Destroy(gameObject);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() => all.Clear();
    }
}
