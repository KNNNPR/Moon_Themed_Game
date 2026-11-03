using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MoonThemedGame.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MoonThemedGame.Editor
{
    // Persistent assets: the effect also works in builds, without runtime setup.
    public static class RetroLookSetup
    {
        const string ModelPath = "Assets/Models/Pale_Horse.fbx";
        const string ScenePath = "Assets/Scenes/Operator.unity";
        const string MaterialFolder = "Assets/Materials/Retro";
        const string PostPath = MaterialFolder + "/RetroDither.mat";
        const string FeatureName = VisualStyleRendererFeature.FeatureName;
        const string OutputFolder = "Temp/RetroLook";
        static readonly string[] RendererPaths =
        {
            "Assets/Settings/PC_Renderer.asset",
            "Assets/Settings/Mobile_Renderer.asset"
        };

        [MenuItem("Tools/Moon Game/Apply Retro Dither + Cel")]
        public static void Apply()
        {
            var scene = SceneManager.GetActiveScene();
            bool hadUnsavedSceneChanges = scene.isDirty;
            var cel = RequireShader("Old Tech/Retro Cel Lit");
            var dither = RequireShader("Old Tech/Retro Dither");
            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh();
            var post = AssetDatabase.LoadAssetAtPath<Material>(PostPath);
            if (!post)
            {
                post = new Material(dither) { name = "RetroDither" };
                AssetDatabase.CreateAsset(post, PostPath);
            }

            VisualStyleSetup.Install();

            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (!importer) throw new InvalidOperationException("Missing model: " + ModelPath);
            var sources = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Material>().ToArray();
            var remaps = importer.GetExternalObjectMap();
            int created = 0;
            foreach (var source in sources)
            {
                var id = new AssetImporter.SourceAssetIdentifier(source);
                if (remaps.TryGetValue(id, out var existing) && existing is Material mapped && mapped.shader == cel)
                    continue;
                string safeName = string.Concat(source.name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                string path = MaterialFolder + "/" + safeName + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material)
                {
                    material = ConvertMaterial(source, cel);
                    AssetDatabase.CreateAsset(material, path);
                }
                importer.AddRemap(id, material);
                ++created;
            }
            if (created > 0) importer.SaveAndReimport();

            foreach (var camera in scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Camera>(true)))
            {
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.None;
                EditorUtility.SetDirty(data);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            // Never save unrelated edits from an interactive editor session.
            if (Application.isBatchMode || !hadUnsavedSceneChanges) EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
            Debug.Log($"[RetroLook] Applied PunchCard0 cel + dither to PC/Mobile; created {created} material remaps.");
        }

        static Material ConvertMaterial(Material source, Shader shader)
        {
            string property = source.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            var texture = source.HasProperty(property) ? source.GetTexture(property) : null;
            var color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") :
                source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            var material = new Material(shader) { name = source.name, enableInstancing = true };
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", color);
            if (source.HasProperty(property))
            {
                material.SetTextureScale("_BaseMap", source.GetTextureScale(property));
                material.SetTextureOffset("_BaseMap", source.GetTextureOffset(property));
            }
            if (source.HasProperty("_EmissionColor") && source.IsKeywordEnabled("_EMISSION"))
                material.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));
            if (source.HasProperty("_EmissionMap") && source.GetTexture("_EmissionMap"))
                material.SetTexture("_EmissionMap", source.GetTexture("_EmissionMap"));
            if (source.IsKeywordEnabled("_ALPHATEST_ON"))
            {
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetFloat("_AlphaClip", 1);
                if (source.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
            }
            if (source.HasProperty("_Cull")) material.SetFloat("_Cull", source.GetFloat("_Cull"));
            Debug.Log($"[RetroLook] {source.name}: {source.shader.name}; texture={texture?.name ?? "none"}; tint={color}");
            return material;
        }

        static Shader RequireShader(string name)
        {
            var shader = Shader.Find(name);
            if (!shader) throw new InvalidOperationException("Shader not imported: " + name);
            if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Shader has compiler errors: " + name);
            return shader;
        }

        [MenuItem("Tools/Moon Game/Select Dither Settings")]
        public static void SelectSettings() => Selection.activeObject = AssetDatabase.LoadAssetAtPath<Material>(PostPath);

        // CLI: Unity -batchmode -projectPath <project> -executeMethod
        // MoonThemedGame.Editor.RetroLookSetup.ApplyAndValidate -logFile <log>
        // Do not pass -nographics: validation exercises actual GPU shader variants.
        [MenuItem("Tools/Moon Game/Apply and Validate Retro Look")]
        public static async void ApplyAndValidate()
        {
            Directory.CreateDirectory(OutputFolder);
            int quality = QualitySettings.GetQualityLevel();
            bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
            GameObject captureObject = null;
            var report = new StringBuilder();
            bool passed = false;
            VisualStyleSettings styles = null;
            VisualStyle previousStyle = VisualStyle.Retro;
            try
            {
                VisualStyleSetup.Install();
                styles = AssetDatabase.LoadAssetAtPath<VisualStyleSettings>(VisualStyleSetup.SettingsPath);
                previousStyle = styles.CurrentStyle;
                styles.SetStyle(VisualStyle.Retro);
                if (Application.isBatchMode) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var scene = SceneManager.GetActiveScene();
                var camera = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Camera>())
                    .FirstOrDefault(c => c.enabled && c.gameObject.activeInHierarchy);
                if (!camera) throw new InvalidOperationException("Scene has no active camera.");
                captureObject = new GameObject("Retro validation camera") { hideFlags = HideFlags.HideAndDontSave };
                var capture = captureObject.AddComponent<Camera>();
                capture.CopyFrom(camera);
                capture.enabled = false;
                capture.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
                var data = capture.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.None;
                ShaderUtil.allowAsyncCompilation = false;
                QualitySettings.SetQualityLevel(1, true);
                await Task.Delay(800);
                bool wasApplied = AssetDatabase.LoadAssetAtPath<Material>(PostPath);
                var before = Capture(capture, OutputFolder + "/before.png");
                Apply();
                int materialCount = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath)
                    .GetComponentsInChildren<Renderer>(true)
                    .SelectMany(r => r.sharedMaterials).Where(m => m && m.shader.name == "Old Tech/Retro Cel Lit").Distinct().Count();
                int emptySlots = 0;
                foreach (var renderer in AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath).GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                    {
                        // These model slots are deliberately unassigned. Unity displays
                        // its package default material; keep them available for later.
                        if (!material || (material.name == "Lit" && AssetDatabase.GetAssetPath(material).StartsWith("Packages/")))
                        {
                            ++emptySlots;
                            continue;
                        }
                        if (!material || material.shader.name != "Old Tech/Retro Cel Lit")
                            throw new InvalidOperationException("Cel material missing on " + renderer.name);
                    }
                int remapCount = ((ModelImporter)AssetImporter.GetAtPath(ModelPath)).GetExternalObjectMap().Count;
                Apply();
                if (((ModelImporter)AssetImporter.GetAtPath(ModelPath)).GetExternalObjectMap().Count != remapCount)
                    throw new InvalidOperationException("Repeated setup changed material remap count.");
                await Task.Delay(800);
                var after = Capture(capture, OutputFolder + "/retro-pc.png");
                if (!wasApplied && !before.Where((pixel, index) => !pixel.Equals(after[index])).Any())
                    throw new InvalidOperationException("Before and after images are identical.");

                var post = AssetDatabase.LoadAssetAtPath<Material>(PostPath);
                float intensity = post.GetFloat("_Intensity");
                try
                {
                    post.SetFloat("_Intensity", 0);
                    var bypass = Capture(capture, OutputFolder + "/cel-only.png");
                    post.SetFloat("_Intensity", intensity);
                    var repeat = Capture(capture, OutputFolder + "/retro-repeat.png");
                    if (!bypass.Where((pixel, index) => !pixel.Equals(repeat[index])).Any())
                        throw new InvalidOperationException("Dither pass did not change the rendered image.");
                    int unstable = after.Where((pixel, index) => !pixel.Equals(repeat[index])).Count();
                    if (unstable != 0) throw new InvalidOperationException("Static grain changed between identical frames: " + unstable);
                }
                finally { post.SetFloat("_Intensity", intensity); }
                QualitySettings.SetQualityLevel(0, true);
                await Task.Delay(800);
                Capture(capture, OutputFolder + "/retro-mobile.png");
                foreach (string path in RendererPaths)
                {
                    var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                    if (renderer.rendererFeatures.OfType<FullScreenPassRendererFeature>()
                        .Count(f => f.name == FeatureName && f.isActive && f.passMaterial == post) != 1)
                        throw new InvalidOperationException("Expected one active dither feature on " + path);
                }
                foreach (string name in new[] { "Old Tech/Retro Cel Lit", "Old Tech/Retro Dither" })
                {
                    var shader = RequireShader(name);
                    foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    {
                        report.AppendLine($"{name}: {message.severity}: {message.message} ({message.file}:{message.line})");
                        if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                            throw new InvalidOperationException("Shader compilation failed: " + message.message);
                    }
                }
                report.AppendLine($"Unity {Application.unityVersion}; {SystemInfo.graphicsDeviceName}");
                report.AppendLine($"{materialCount} model materials; PC deferred + Mobile forward rendered.");
                report.AppendLine($"Preserved {emptySlots} intentionally unassigned/default material slots.");
                report.AppendLine("Verified setup is idempotent, post-effect changes pixels, grain is static, shaders have no compiler errors.");
                passed = true;
            }
            catch (Exception exception)
            {
                report.AppendLine(exception.ToString());
                Debug.LogException(exception);
            }
            finally
            {
                QualitySettings.SetQualityLevel(quality, true);
                if (styles) styles.SetStyle(previousStyle);
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                if (captureObject) Object.DestroyImmediate(captureObject);
                File.WriteAllText(OutputFolder + "/validation.txt", (passed ? "PASS\n" : "FAIL\n") + report);
                Debug.Log("[RetroLook] " + (passed ? "VALIDATION PASSED\n" : "VALIDATION FAILED\n") + report);
                if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
            }
        }

        static Color32[] Capture(Camera camera, string path)
        {
            var target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                var pixels = image.GetPixels32();
                if (pixels.Count(c => Math.Max(c.r, Math.Max(c.g, c.b)) > 8) < pixels.Length / 100)
                    throw new InvalidOperationException("Capture is effectively blank: " + path);
                if (pixels.Count(c => c.r > 230 && c.g < 30 && c.b > 230) > pixels.Length / 100)
                    throw new InvalidOperationException("Capture contains error-shader magenta: " + path);
                return pixels;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);
            }
        }
    }
}
