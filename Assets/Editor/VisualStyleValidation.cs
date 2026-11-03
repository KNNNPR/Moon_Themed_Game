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
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif
using Object = UnityEngine.Object;

namespace MoonThemedGame.Editor
{
    public static class VisualStyleValidation
    {
        const string Output = "Logs/VisualStyleCaptures";

        // Intended for an isolated batch-mode copy of the project.
        public static async void Run()
        {
            Directory.CreateDirectory(Output);
            var report = new StringBuilder();
            bool passed = false;
            int quality = QualitySettings.GetQualityLevel();
            bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
            GameObject host = null;
            VisualStyleSettings settings = null;
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Operator.unity", OpenSceneMode.Single);
                string modelMeta = File.ReadAllText("Assets/Models/Pale_Horse.fbx.meta");
                VisualStyleSetup.Install();
                VisualStyleSetup.Install();
                Require(File.ReadAllText("Assets/Models/Pale_Horse.fbx.meta") == modelMeta, "Model remaps changed.");
                settings = AssetDatabase.LoadAssetAtPath<VisualStyleSettings>(VisualStyleSetup.SettingsPath);
                Require(settings && settings.AsciiMaterial && settings.RetroMaterial, "Missing style assets.");
                string savedSettings = EditorJsonUtility.ToJson(settings);
                foreach (string path in VisualStyleSetup.RendererPaths)
                {
                    var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                    Require(renderer.rendererFeatures.OfType<VisualStyleRendererFeature>().Count() == 1, "Duplicate/missing style feature: " + path);
                    Require(!renderer.rendererFeatures.Any(f => f && f.name == "Retro Dither + Palette"), "Legacy dither still stacks with ASCII.");
                }

                var source = Camera.main;
                Require(source, "No main camera.");
                host = new GameObject("Visual style validation") { hideFlags = HideFlags.HideAndDontSave };
                var camera = host.AddComponent<Camera>();
                camera.CopyFrom(source);
                camera.enabled = false;
                camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.None;
                ShaderUtil.allowAsyncCompilation = false;
                QualitySettings.SetQualityLevel(1, true);
                settings.SetStyle(VisualStyle.ColorAscii);
                await Task.Delay(1000);
                var ascii = Capture(camera, "ascii-pc.png");
                Require(ascii.Count(p => Math.Max(p.r, Math.Max(p.g, p.b)) - Math.Min(p.r, Math.Min(p.g, p.b)) > 20) > ascii.Length / 100,
                    "ASCII lost scene colors.");
                VisualStyleSwitcher.ToggleStyle();
                Require(settings.CurrentStyle == VisualStyle.Retro, "Toggle did not select Retro.");
                var retro = Capture(camera, "retro-pc.png");
                Require(ascii.Where((p, i) => !p.Equals(retro[i])).Count() > ascii.Length / 10, "Modes render the same image.");
                VisualStyleSwitcher.ToggleStyle();
                Require(settings.CurrentStyle == VisualStyle.ColorAscii, "Toggle did not return to ASCII.");
                var repeat = Capture(camera, "ascii-repeat.png");
                Require(!ascii.Where((p, i) => !p.Equals(repeat[i])).Any(), "ASCII pattern changes on a static frame.");

                float intensity = settings.AsciiMaterial.GetFloat("_Intensity");
                try
                {
                    settings.AsciiMaterial.SetFloat("_Intensity", 0);
                    var bypass = Capture(camera, "ascii-bypass.png");
                    var feature = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(VisualStyleSetup.RendererPaths[0])
                        .rendererFeatures.OfType<VisualStyleRendererFeature>().Single();
                    feature.SetActive(false);
                    Color32[] raw;
                    try { raw = Capture(camera, "original.png"); }
                    finally { feature.SetActive(true); }
                    int largestError = bypass.Select((p, i) => Math.Max(Math.Abs(p.r - raw[i].r), Math.Max(Math.Abs(p.g - raw[i].g), Math.Abs(p.b - raw[i].b)))).Max();
                    Require(largestError <= 2, "Intensity zero does not bypass the effect: " + largestError);
                    report.AppendLine("Intensity 0 matches the unprocessed image (maximum channel error: " + largestError + ").");
                }
                finally { settings.AsciiMaterial.SetFloat("_Intensity", intensity); }

                QualitySettings.SetQualityLevel(0, true);
                await Task.Delay(1000);
                settings.SetStyle(VisualStyle.ColorAscii);
                Capture(camera, "ascii-mobile.png");
                settings.AsciiMaterial.SetFloat("_Intensity", 0);
                try { Capture(camera, "ascii-bypass-mobile.png"); }
                finally { settings.AsciiMaterial.SetFloat("_Intensity", intensity); }
                settings.SetStyle(VisualStyle.Retro);
                Capture(camera, "retro-mobile.png");
                if (Application.isBatchMode) await TestHotkey();
                Require(EditorJsonUtility.ToJson(settings) == savedSettings, "Session switching modified saved settings.");
                foreach (string name in new[] { "Moon Game/Color ASCII", "Old Tech/Retro Dither", "Old Tech/Retro Cel Lit" })
                {
                    var shader = Shader.Find(name);
                    Require(shader && !ShaderUtil.ShaderHasError(shader), "Shader errors: " + name);
                    foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    {
                        report.AppendLine($"{name}: {message.severity}: {message.message}");
                        Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
                    }
                }
                report.AppendLine("PC deferred + Mobile forward: both modes rendered without magenta or blank output.");
                report.AppendLine("Verified color preservation, exclusive passes, round-trip toggle, deterministic glyphs, idempotent setup, unchanged model remaps and saved start mode.");
                report.AppendLine($"Unity {Application.unityVersion}; {SystemInfo.graphicsDeviceName}");
                passed = true;
            }
            catch (Exception exception)
            {
                report.AppendLine(exception.ToString());
                Debug.LogException(exception);
            }
            finally
            {
                if (settings) settings.ResetSession();
                QualitySettings.SetQualityLevel(quality, true);
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                if (host) Object.DestroyImmediate(host);
                File.WriteAllText(Output + "/validation.txt", (passed ? "PASS\n" : "FAIL\n") + report);
                Debug.Log("[VisualStyle] " + (passed ? "PASS\n" : "FAIL\n") + report);
                if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
            }
        }

        static async Task TestHotkey()
        {
#if ENABLE_INPUT_SYSTEM
            bool optionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            var options = EditorSettings.enterPlayModeOptions;
            var background = InputSystem.settings.backgroundBehavior;
            var editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            var updateMode = InputSystem.settings.updateMode;
            Keyboard keyboard = null;
            try
            {
                // Keep this async check alive across Play Mode in the isolated copy.
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                EditorApplication.EnterPlaymode();
                for (int i = 0; i < 100 && (!EditorApplication.isPlaying || !Object.FindFirstObjectByType<VisualStyleSwitcher>()); ++i)
                    await Task.Delay(100);
                Require(EditorApplication.isPlaying && Object.FindFirstObjectByType<VisualStyleSwitcher>(), "Automatic F6 switcher was not created in Play Mode.");
                var switcher = Object.FindFirstObjectByType<VisualStyleSwitcher>();
                switcher.enabled = false;
                var update = typeof(VisualStyleSwitcher).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
                keyboard = InputSystem.AddDevice<Keyboard>();
                InputSystem.EnableDevice(keyboard);
                keyboard.MakeCurrent();
                VisualStyleSwitcher.SetStyle(VisualStyle.Retro);
                InputSystem.Update();
                // Headless Unity discards queued keyboard events without a Game
                // window. Inject device state directly, then run the real handler.
                InputState.Change(keyboard, new KeyboardState(Key.F6));
                Debug.Log($"[VisualStyle] Virtual F6: current={Keyboard.current == keyboard}; pressed={keyboard.f6Key.isPressed}; frame={keyboard.f6Key.wasPressedThisFrame}");
                update.Invoke(switcher, null);
                Require(VisualStyleSwitcher.CurrentStyle == VisualStyle.ColorAscii, "F6 did not select ASCII.");
                InputSystem.Update();
                InputState.Change(keyboard, new KeyboardState());
                InputSystem.Update();
                InputState.Change(keyboard, new KeyboardState(Key.F6));
                update.Invoke(switcher, null);
                Require(VisualStyleSwitcher.CurrentStyle == VisualStyle.Retro, "Second F6 did not restore Retro.");
                Debug.Log("[VisualStyle] F6 passed in Play Mode: Retro -> ASCII -> Retro.");
            }
            finally
            {
                if (keyboard != null) InputSystem.RemoveDevice(keyboard);
                EditorApplication.ExitPlaymode();
                for (int i = 0; i < 100 && EditorApplication.isPlaying; ++i) await Task.Delay(100);
                EditorSettings.enterPlayModeOptionsEnabled = optionsEnabled;
                EditorSettings.enterPlayModeOptions = options;
                InputSystem.settings.backgroundBehavior = background;
                InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
                InputSystem.settings.updateMode = updateMode;
            }
#endif
        }

        static Color32[] Capture(Camera camera, string filename)
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
                File.WriteAllBytes(Output + "/" + filename, image.EncodeToPNG());
                var pixels = image.GetPixels32();
                Require(pixels.Count(p => Math.Max(p.r, Math.Max(p.g, p.b)) > 8) > pixels.Length / 100, "Blank frame: " + filename);
                Require(pixels.Count(p => p.r > 230 && p.g < 30 && p.b > 230) < pixels.Length / 100, "Magenta error pixels: " + filename);
                return pixels;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);
            }
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
