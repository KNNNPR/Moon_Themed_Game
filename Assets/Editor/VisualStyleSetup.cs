using System;
using System.Linq;
using MoonThemedGame.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace MoonThemedGame.Editor
{
    [InitializeOnLoad]
    public static class VisualStyleSetup
    {
        public const string SettingsPath = "Assets/Resources/VisualStyleSettings.asset";
        public const string AsciiPath = "Assets/Materials/Retro/ColorAscii.mat";
        public const string RetroPath = "Assets/Materials/Retro/RetroDither.mat";
        public static readonly string[] RendererPaths =
        {
            "Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset"
        };

        static VisualStyleSetup()
        {
            EditorApplication.delayCall += InstallWhenReady;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredEditMode) return;
                var settings = AssetDatabase.LoadAssetAtPath<VisualStyleSettings>(SettingsPath);
                if (settings) settings.ResetSession();
                Repaint();
            };
        }

        static void InstallWhenReady()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += InstallWhenReady;
                return;
            }
            var settings = AssetDatabase.LoadAssetAtPath<VisualStyleSettings>(SettingsPath);
            if (!settings || RendererPaths.Any(path =>
            {
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                return renderer && !renderer.rendererFeatures.OfType<VisualStyleRendererFeature>().Any(f => f.Settings == settings);
            })) Install();
        }

        [MenuItem("Tools/Moon Game/Visual Style/Install or Repair")]
        public static void Install()
        {
            EnsureFolder("Assets/Materials/Retro");
            EnsureFolder("Assets/Resources");
            var retro = GetOrCreateMaterial(RetroPath, "Old Tech/Retro Dither");
            var ascii = GetOrCreateMaterial(AsciiPath, "Moon Game/Color ASCII");
            var settings = AssetDatabase.LoadAssetAtPath<VisualStyleSettings>(SettingsPath);
            if (!settings)
            {
                settings = ScriptableObject.CreateInstance<VisualStyleSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            settings.Configure(retro, ascii);
            EditorUtility.SetDirty(settings);

            foreach (string path in RendererPaths)
            {
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (!renderer) throw new InvalidOperationException("Missing URP renderer: " + path);
                var feature = renderer.rendererFeatures.OfType<VisualStyleRendererFeature>().FirstOrDefault();
                if (!feature)
                {
                    feature = ScriptableObject.CreateInstance<VisualStyleRendererFeature>();
                    feature.name = VisualStyleRendererFeature.FeatureName;
                    AssetDatabase.AddObjectToAsset(feature, renderer);
                    renderer.rendererFeatures.Add(feature);
                }
                // Remove only the previous effect installed by RetroLookSetup.
                // Other renderer features, including SSAO, remain in place.
                foreach (var old in renderer.rendererFeatures.Where(f => f && f != feature &&
                    (f.name == "Retro Dither + Palette" || f is VisualStyleRendererFeature)).ToArray())
                {
                    renderer.rendererFeatures.Remove(old);
                    Object.DestroyImmediate(old, true);
                }
                feature.Settings = settings;
                feature.passMaterial = settings.CurrentMaterial;
                feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
                feature.fetchColorBuffer = true;
                feature.requirements = ScriptableRenderPassInput.Depth;
                feature.passIndex = 0;
                feature.bindDepthStencilAttachment = true;
                feature.SetActive(true);
                EditorUtility.SetDirty(feature);
                var serialized = new SerializedObject(renderer);
                var map = serialized.FindProperty("m_RendererFeatureMap");
                map.arraySize = renderer.rendererFeatures.Count;
                for (int i = 0; i < map.arraySize; ++i)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i], out string _, out long id);
                    map.GetArrayElementAtIndex(i).longValue = id;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                renderer.SetDirty();
                EditorUtility.SetDirty(renderer);
            }
            AssetDatabase.SaveAssets();
            Repaint();
            Debug.Log("[VisualStyle] PC + Mobile ready. F6 switches Retro / Color ASCII in Play mode.");
        }

        static Material GetOrCreateMaterial(string path, string shaderName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            var shader = Shader.Find(shaderName);
            if (!shader || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Shader unavailable or has compiler errors: " + shaderName);
            material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        [MenuItem("Tools/Moon Game/Visual Style/Retro Dither + Cel", false, 1)]
        public static void UseRetro() => SelectStyle(VisualStyle.Retro);
        [MenuItem("Tools/Moon Game/Visual Style/Color ASCII", false, 2)]
        public static void UseAscii() => SelectStyle(VisualStyle.ColorAscii);
        [MenuItem("Tools/Moon Game/Visual Style/Toggle", false, 3)]
        public static void Toggle()
        {
            var settings = AssetDatabase.LoadAssetAtPath<VisualStyleSettings>(SettingsPath);
            if (!settings) { Install(); settings = AssetDatabase.LoadAssetAtPath<VisualStyleSettings>(SettingsPath); }
            SelectStyle(settings.CurrentStyle == VisualStyle.Retro ? VisualStyle.ColorAscii : VisualStyle.Retro);
        }
        [MenuItem("Tools/Moon Game/Visual Style/Select Settings", false, 20)]
        public static void SelectSettings() => Selection.activeObject = AssetDatabase.LoadAssetAtPath<VisualStyleSettings>(SettingsPath);
        [MenuItem("Tools/Moon Game/Visual Style/Select ASCII Material", false, 21)]
        public static void SelectAsciiMaterial() => Selection.activeObject = AssetDatabase.LoadAssetAtPath<Material>(AsciiPath);

        static void SelectStyle(VisualStyle style)
        {
            var settings = AssetDatabase.LoadAssetAtPath<VisualStyleSettings>(SettingsPath);
            if (!settings) { Install(); settings = AssetDatabase.LoadAssetAtPath<VisualStyleSettings>(SettingsPath); }
            if (EditorApplication.isPlaying) settings.SetStyle(style);
            else
            {
                Undo.RecordObject(settings, "Change visual style");
                settings.SetDefaultStyle(style);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
            }
            Repaint();
        }

        static void Repaint()
        {
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }
}
