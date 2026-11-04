using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoonThemedGame.Interactions
{
    // Keep the class and file GUID to preserve existing scene references.
    [DisallowMultipleComponent]
    [AddComponentMenu("Moon Game/Interactions/Interaction Outline")]
    public sealed class InteractionOutline : MonoBehaviour
    {
        public Material outlineMaterial;
        public MeshRenderer[] targets;
        [Range(1f, 8f)] public float widthPixels = 3f;
        public Color outlineColor = new Color(1f, 0.95f, 0.75f, 1f);
        [Min(0.1f)] public float fadeSpeed = 14f;

        struct FixedMesh { public Mesh mesh; public Matrix4x4 matrix; public MeshRenderer source; }
        readonly List<FixedMesh> fixedMeshes = new List<FixedMesh>();
        bool fixedGeometry, highlighted;
        float opacity;
        Material material;
        CommandBuffer commands;
        RenderTexture mask, contour;
        Camera viewCamera;

        public void SetFixedGeometry(MeshRenderer[] renderers)
        {
            fixedGeometry = true;
            fixedMeshes.Clear();
            foreach (var renderer in renderers)
            {
                if (!renderer) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter && filter.sharedMesh)
                    fixedMeshes.Add(new FixedMesh { mesh = filter.sharedMesh,
                        matrix = renderer.localToWorldMatrix, source = renderer });
            }
        }

        public void SetHighlighted(bool value) => highlighted = value && isActiveAndEnabled;

        bool EnsureResources(int width, int height)
        {
            if (!material)
            {
                Shader shader = outlineMaterial ? outlineMaterial.shader : Shader.Find("Moon Game/Interaction Highlight");
                if (!shader) return false;
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (commands == null) commands = new CommandBuffer { name = "Interaction silhouette mask" };
            if (mask && mask.width == width && mask.height == height) return true;
            ReleaseTextures();
            mask = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                { name = "Interaction mask", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            contour = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                { name = "Interaction contour", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            mask.Create();
            contour.Create();
            return true;
        }

        void LateUpdate()
        {
            // Unscaled: the settings menu pauses time, and the outline must still disappear under it.
            opacity = Mathf.Lerp(opacity, highlighted ? 1f : 0f, 1f - Mathf.Exp(-fadeSpeed * Time.unscaledDeltaTime));
            if (!highlighted && GameSettingsMenu.IsOpen) opacity = 0f;
            if (!highlighted && opacity < 0.002f)
            {
                opacity = 0f;
                if (mask) ReleaseTextures();
                return;
            }
            viewCamera = Camera.main;
            if (!viewCamera || !EnsureResources(Mathf.Max(1, viewCamera.pixelWidth), Mathf.Max(1, viewCamera.pixelHeight))) return;
            var previous = RenderTexture.active;
            try
            {
                commands.Clear();
                commands.SetRenderTarget(mask);
                commands.ClearRenderTarget(true, true, Color.clear);
                commands.SetViewport(new Rect(0, 0, mask.width, mask.height));
                material.SetMatrix("_OutlineViewProjection",
                    GL.GetGPUProjectionMatrix(viewCamera.projectionMatrix, true) * viewCamera.worldToCameraMatrix);
                if (fixedGeometry)
                {
                    foreach (var entry in fixedMeshes)
                        if (entry.mesh && entry.source && entry.source.enabled && entry.source.gameObject.activeInHierarchy)
                            for (int part = 0; part < entry.mesh.subMeshCount; part++)
                                commands.DrawMesh(entry.mesh, entry.matrix, material, part, 0);
                }
                else
                {
                    // Re-read live renderers so newly installed robot parts also get a contour.
                    var renderers = targets != null && targets.Length > 0 ? targets : GetComponentsInChildren<MeshRenderer>();
                    foreach (var renderer in renderers)
                    {
                        if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                        var filter = renderer.GetComponent<MeshFilter>();
                        if (!filter || !filter.sharedMesh) continue;
                        for (int part = 0; part < filter.sharedMesh.subMeshCount; part++)
                            commands.DrawRenderer(renderer, material, part, 0);
                    }
                }
                Graphics.ExecuteCommandBuffer(commands);
                material.SetFloat("_OutlineWidth", widthPixels);
                Color color = outlineColor;
                color.a *= opacity;
                material.SetColor("_OutlineColor", color);
                Graphics.Blit(mask, contour, material, 1);
            }
            finally { RenderTexture.active = previous; }
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !contour || !viewCamera || opacity <= 0f) return;
            var previous = GUI.color;
            GUI.color = Color.white;
            Rect viewport = viewCamera.pixelRect;
            GUI.DrawTexture(new Rect(viewport.x, Screen.height - viewport.yMax, viewport.width, viewport.height),
                contour, ScaleMode.StretchToFill, true);
            GUI.color = previous;
        }

        void ReleaseTextures()
        {
            if (mask) { mask.Release(); Destroy(mask); mask = null; }
            if (contour) { contour.Release(); Destroy(contour); contour = null; }
        }

        void OnDisable()
        {
            highlighted = false;
            opacity = 0f;
            ReleaseTextures();
        }

        void OnDestroy()
        {
            ReleaseTextures();
            if (commands != null) commands.Release();
            if (material) Destroy(material);
        }
    }
}
