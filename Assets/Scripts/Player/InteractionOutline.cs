using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoonThemedGame.Interactions
{
    // Keep this class/file name so existing scene references remain valid.
    [DisallowMultipleComponent]
    [AddComponentMenu("Moon Game/Interactions/Interaction Highlight")]
    public sealed class InteractionOutline : MonoBehaviour
    {
        [Tooltip("Existing highlight material; a default is created when empty.")]
        public Material outlineMaterial;
        public MeshRenderer[] targets;
        [Range(0f, 0.4f)] public float highlightAmount = 0.12f;
        [Min(0.1f)] public float fadeSpeed = 12f;

        int amount;
        readonly List<MeshRenderer> overlays = new List<MeshRenderer>();
        readonly List<MeshRenderer> sources = new List<MeshRenderer>();
        MaterialPropertyBlock properties;
        Material ownedMaterial;
        bool highlighted;
        float opacity;

        void Awake() => InitializeRuntimeState();

        void OnEnable() => InitializeRuntimeState();

        void InitializeRuntimeState()
        {
            // Native Unity objects must be created on the main thread, after construction.
            if (properties == null) properties = new MaterialPropertyBlock();
            amount = Shader.PropertyToID("_HighlightAmount");
        }

        public void SetHighlighted(bool value)
        {
            highlighted = value && isActiveAndEnabled;
            if (highlighted && overlays.Count == 0) Build();
        }

        void Build()
        {
            Material material = outlineMaterial;
            if (!material)
            {
                var shader = Shader.Find("Moon Game/Interaction Highlight");
                if (!shader)
                {
                    Debug.LogError("Interaction Highlight shader is unavailable.", this);
                    return;
                }
                if (!ownedMaterial) ownedMaterial = new Material(shader)
                    { hideFlags = HideFlags.HideAndDontSave };
                material = ownedMaterial;
            }
            var renderers = targets != null && targets.Length > 0
                ? targets : GetComponentsInChildren<MeshRenderer>();
            foreach (var source in renderers)
            {
                if (!source) continue;
                var filter = source.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh) continue;
                var host = new GameObject("Interaction / soft white highlight")
                    { hideFlags = HideFlags.DontSave, layer = source.gameObject.layer };
                host.transform.SetParent(source.transform, false);
                host.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var overlay = host.AddComponent<MeshRenderer>();
                var materials = new Material[filter.sharedMesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                overlay.sharedMaterials = materials;
                overlay.shadowCastingMode = ShadowCastingMode.Off;
                overlay.receiveShadows = false;
                overlay.enabled = false;
                overlays.Add(overlay);
                sources.Add(source);
            }
        }

        void LateUpdate()
        {
            float target = highlighted ? highlightAmount : 0f;
            opacity = Mathf.Lerp(opacity, target, 1f - Mathf.Exp(-fadeSpeed * Time.deltaTime));
            if (Mathf.Abs(opacity - target) < 0.0001f) opacity = target;
            if (properties == null) InitializeRuntimeState();
            properties.SetFloat(amount, opacity);
            for (int i = 0; i < overlays.Count; i++)
            {
                if (!overlays[i]) continue;
                overlays[i].enabled = opacity > 0.0001f && sources[i] && sources[i].enabled;
                overlays[i].SetPropertyBlock(properties);
            }
        }

        void OnDisable()
        {
            highlighted = false;
            opacity = 0f;
            foreach (var overlay in overlays) if (overlay) overlay.enabled = false;
        }

        void OnDestroy()
        {
            foreach (var overlay in overlays) if (overlay) Destroy(overlay.gameObject);
            if (ownedMaterial) Destroy(ownedMaterial);
        }
    }
}
