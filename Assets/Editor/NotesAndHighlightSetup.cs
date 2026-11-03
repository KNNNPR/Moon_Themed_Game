using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MoonThemedGame.Computer;
using MoonThemedGame.Rendering;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace MoonThemedGame.Editor
{
    public static class NotesAndHighlightSetup
    {
        const string NotesPath = "Assets/Prefabs/Notes.prefab";
        const string InkPath = "Assets/Computer/NotesInk.mat";
        const string PaperPath = "Assets/Computer/NotesPaper.mat";
        const string Output = "Logs/NotesAndHighlightCaptures";

        [MenuItem("Tools/Moon Game/PC Monitor/Apply Notes Credentials")]
        public static void Apply()
        {
            var prefab = PrefabUtility.LoadPrefabContents(NotesPath);
            try
            {
                var notes = prefab.GetComponentInChildren<MeshFilter>();
                if (!notes) throw new InvalidOperationException("Notes mesh is missing.");
                var print = prefab.GetComponent<NotesCredentials>();
                if (!print) print = prefab.AddComponent<NotesCredentials>();
                print.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/PTMono/PTMono-Regular.ttf");
                var ink = AssetDatabase.LoadAssetAtPath<Material>(InkPath);
                if (!ink)
                {
                    ink = new Material(Shader.Find("Moon Game/Notes Ink")) { name = "NotesInk" };
                    AssetDatabase.CreateAsset(ink, InkPath);
                }
                print.inkMaterial = ink;
                var paper = AssetDatabase.LoadAssetAtPath<Material>(PaperPath);
                if (!paper)
                {
                    paper = new Material(Shader.Find("Moon Game/Readable Notes Paper")) { name = "NotesPaper" };
                    paper.SetTexture("_BaseMap", notes.GetComponent<MeshRenderer>().sharedMaterial.GetTexture("_BaseMap"));
                    AssetDatabase.CreateAsset(paper, PaperPath);
                }
                notes.GetComponent<MeshRenderer>().sharedMaterial = paper;
                paper.EnableKeyword("_ALPHATEST_ON");
                if (!print.lettering)
                {
                    var child = new GameObject("Printed credentials");
                    child.transform.SetParent(prefab.transform, false);
                    print.lettering = child.AddComponent<TextMesh>();
                }
                print.lettering.characterSize = 0.1f;
                print.Refresh();
                using var data = MeshUtility.AcquireReadOnlyMeshData(notes.sharedMesh);
                using var positions = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp);
                using var uvs = new NativeArray<Vector2>(data[0].vertexCount, Allocator.Temp);
                data[0].GetVertices(positions);
                data[0].GetUVs(0, uvs);
                Directory.CreateDirectory(Output);
                File.WriteAllLines(Output + "/notes-geometry.txt", Enumerable.Range(0, positions.Length)
                    .Select(i => $"{i}: position={positions[i]:F5} UV={uvs[i]:F5}"));
                using var indices = new NativeArray<int>((int)data[0].GetSubMesh(0).indexCount, Allocator.Temp);
                data[0].GetIndices(indices, 0);
                Vector3 Point(Vector2 uv, out Vector3 n)
                {
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                        var ab = uvs[b] - uvs[a]; var ac = uvs[c] - uvs[a]; var ap = uv - uvs[a];
                        float det = ab.x * ac.y - ab.y * ac.x;
                        if (Mathf.Abs(det) < 0.000001f) continue;
                        float v = (ap.x * ac.y - ap.y * ac.x) / det;
                        float w = (ab.x * ap.y - ab.y * ap.x) / det;
                        if (v < -0.001f || w < -0.001f || v + w > 1.001f) continue;
                        var p0 = notes.transform.TransformPoint(positions[a]);
                        var p1 = notes.transform.TransformPoint(positions[b]);
                        var p2 = notes.transform.TransformPoint(positions[c]);
                        n = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                        return p0 * (1 - v - w) + p1 * v + p2 * w;
                    }
                    throw new InvalidOperationException("Notes paper UV does not contain " + uv);
                }
                var top = Point(new Vector2(0.1f, 0.84f), out var normal);
                var rightPoint = Point(new Vector2(0.585f, 0.84f), out _);
                var bottom = Point(new Vector2(0.1f, 0.2f), out _);
                var right = (rightPoint - top).normalized;
                var up = Vector3.ProjectOnPlane(top - bottom, right).normalized;
                var text = print.lettering.transform;
                text.SetPositionAndRotation(top, Quaternion.LookRotation(Vector3.Cross(right, up), up));
                text.localScale = Vector3.one;
                var bounds = print.lettering.GetComponent<MeshRenderer>().localBounds;
                float fit = Mathf.Min(Vector3.Distance(top, rightPoint) / (bounds.size.x * text.lossyScale.x),
                    Vector3.Distance(top, bottom) / (bounds.size.y * text.lossyScale.y));
                text.localScale *= fit;
                float scale = text.lossyScale.x;
                text.position = top + normal * 0.002f - right * (bounds.min.x * scale) - up * (bounds.max.y * scale);
                // Only persistent references belong in the prefab; the live font atlas is bound by NotesCredentials.
                print.lettering.GetComponent<MeshRenderer>().sharedMaterial = ink;
                PrefabUtility.SaveAsPrefabAsset(prefab, NotesPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[NotesAndHighlight] Notes print installed. Paper anchor {top}, normal {normal}, print size {bounds.size * scale}.");
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }

        public static async void ApplyAndValidate()
        {
            Directory.CreateDirectory(Output);
            bool passed = false;
            bool optionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            var options = EditorSettings.enterPlayModeOptions;
            var report = new StringBuilder();
            try
            {
                Apply();
                EditorSceneManager.OpenScene("Assets/Scenes/Operator.unity");
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
                EditorApplication.EnterPlaymode();
                for (int i = 0; i < 150 && !EditorApplication.isPlaying; i++) await Task.Delay(100);
                Require(EditorApplication.isPlaying, "Play Mode did not start.");
                ShaderUtil.allowAsyncCompilation = false;
                QualitySettings.SetQualityLevel(1, true);
                var terminal = Object.FindFirstObjectByType<PcMonitorTerminal>();
                terminal.BuildDisplay();
                await Task.Delay(1500);
                var camera = Camera.main;
                var normal = terminal.transform.TransformDirection(terminal.screenNormal).normalized;
                var up = terminal.transform.TransformDirection(terminal.screenUp).normalized;
                var center = terminal.transform.TransformPoint(terminal.screenCenter);
                var initialPosition = camera.transform.position;
                var initialRotation = camera.transform.rotation;
                var controller = Object.FindFirstObjectByType<FirstPersonController>();
                bool playerEnabled = controller && controller.enabled;
                if (controller) controller.enabled = false;
                camera.transform.SetPositionAndRotation(center + normal * 2.5f, Quaternion.LookRotation(-normal, up));
                await Task.Delay(400);
                Require(terminal.IsAimed, "Aiming at the monitor did not highlight it.");
                Require(terminal.ScreenMaterial.GetFloat("_HoverHighlight") > 0.5f, "Highlight did not fade in.");
                Capture(camera, Output + "/monitor-hover.png");
                camera.transform.rotation = Quaternion.LookRotation(normal, up);
                await Task.Delay(400);
                Require(!terminal.IsAimed && terminal.ScreenMaterial.GetFloat("_HoverHighlight") < 0.01f, "Highlight remained after looking away.");
                camera.transform.SetPositionAndRotation(initialPosition, initialRotation);
                if (controller && playerEnabled) controller.enabled = true;
                terminal.Focus(true);
                await Task.Delay(400);
                Require(terminal.Root.Q("login-screen") == null, "The cancelled login screen is still present.");
                Require(terminal.Root.Q("satellites") != null, "SATELLITES desktop icon is missing.");
                terminal.SetPointer(new Vector2(110, 220));
                Require(terminal.HoveredIcon == "scavenger", "Existing SCAVENGER interaction changed.");
                terminal.HidePointer();
                Capture(camera, Output + "/desktop-pc.png");
                QualitySettings.SetQualityLevel(0, true);
                await Task.Delay(400);
                Capture(camera, Output + "/desktop-mobile.png");
                QualitySettings.SetQualityLevel(1, true);
                terminal.ExitFocus(true);
                var notes = Object.FindFirstObjectByType<NotesCredentials>();
                Require(notes && notes.lettering && notes.lettering.text == NotesCredentials.Message, "Printed note text is missing.");
                notes.Refresh();
                var textTransform = notes.lettering.transform;
                var paperBounds = notes.GetComponent<MeshRenderer>().bounds;
                var noteNormal = -textTransform.forward;
                // Match the capture target before projecting the printed area into pixel coordinates.
                camera.aspect = 1280f / 720f;
                if (controller) controller.enabled = false;
                camera.transform.SetPositionAndRotation(paperBounds.center + noteNormal * Mathf.Max(paperBounds.size.magnitude * 1.1f, 1),
                    Quaternion.LookRotation(-noteNormal, textTransform.up));
                camera.fieldOfView = 42;
                await Task.Delay(400);
                Capture(camera, Output + "/notes.png");
                var styles = Resources.Load<VisualStyleSettings>("VisualStyleSettings");
                var previousStyle = styles.CurrentStyle;
                try
                {
                    var a = camera.WorldToViewportPoint(textTransform.TransformPoint(notes.lettering.GetComponent<MeshRenderer>().localBounds.min));
                    var b = camera.WorldToViewportPoint(textTransform.TransformPoint(notes.lettering.GetComponent<MeshRenderer>().localBounds.max));
                    var printRect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                    foreach (int quality in new[] { 1, 0 })
                    foreach (var style in new[] { VisualStyle.Retro, VisualStyle.ColorAscii })
                    {
                        QualitySettings.SetQualityLevel(quality, true);
                        styles.SetStyle(style);
                        await Task.Delay(300);
                        var effect = styles.CurrentMaterial;
                        float intensity = effect.GetFloat("_Intensity");
                        Texture2D processed = null, original = null;
                        try
                        {
                            effect.SetFloat("_Intensity", 1);
                            processed = CapturePixels(camera, Output + $"/notes-{quality}-{style}.png");
                            effect.SetFloat("_Intensity", 0);
                            original = CapturePixels(camera, Output + $"/notes-{quality}-{style}-unfiltered.png");
                            var p = processed.GetPixels(); var o = original.GetPixels();
                            double noteError = 0, backgroundError = 0;
                            int noteCount = 0, backgroundCount = 0;
                            for (int y = 0; y < processed.height; y++)
                            for (int x = 0; x < processed.width; x++)
                            {
                                var uv = new Vector2((x + 0.5f) / processed.width, (y + 0.5f) / processed.height);
                                int i = y * processed.width + x;
                                float error = Mathf.Abs(p[i].r - o[i].r) + Mathf.Abs(p[i].g - o[i].g) + Mathf.Abs(p[i].b - o[i].b);
                                if (printRect.Contains(uv)) { noteError += error; noteCount++; }
                                else { backgroundError += error; backgroundCount++; }
                            }
                            noteError /= Math.Max(1, noteCount) * 3;
                            backgroundError /= Math.Max(1, backgroundCount) * 3;
                            report.AppendLine($"Quality {quality} / {style}: note error {noteError:F6}, background effect {backgroundError:F6}.");
                            Require(noteCount > 100 && noteError < 0.003, "Notes were still processed by the full-screen effect.");
                            Require(backgroundError > 0.005, "The visual effect was disabled outside Notes.");
                        }
                        finally
                        {
                            effect.SetFloat("_Intensity", intensity);
                            if (processed) Object.DestroyImmediate(processed);
                            if (original) Object.DestroyImmediate(original);
                        }
                    }
                }
                finally { styles.SetStyle(previousStyle); }
                report.AppendLine("Physical hover fades in/out. Original desktop, SATELLITES and SCAVENGER interaction preserved; no login. Notes printed with PT Mono. PC/Mobile captures rendered.");
                foreach (string shaderName in new[] { "Moon Game/PC Monitor CRT", "Moon Game/Notes Ink", "Moon Game/Readable Notes Paper", "Moon Game/Charge Monitor CRT", "Old Tech/Retro Dither", "Moon Game/Color ASCII" })
                    foreach (var message in ShaderUtil.GetShaderMessages(Shader.Find(shaderName)))
                        Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, shaderName + ": " + message.message);
                passed = true;
            }
            catch (Exception exception) { report.AppendLine(exception.ToString()); Debug.LogException(exception); }
            finally
            {
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.ExitPlaymode();
                    for (int i = 0; i < 100 && EditorApplication.isPlaying; i++) await Task.Delay(100);
                }
                AssetDatabase.Refresh();
                EditorSettings.enterPlayModeOptionsEnabled = optionsEnabled;
                EditorSettings.enterPlayModeOptions = options;
                File.WriteAllText(Output + "/validation.txt", (passed ? "PASS\n" : "FAIL\n") + report);
                Debug.Log("[NotesAndHighlight] " + (passed ? "PASS\n" : "FAIL\n") + report);
                if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
            }
        }
        static void Capture(Camera camera, string path)
        {
            Object.DestroyImmediate(CapturePixels(camera, path));
        }

        static Texture2D CapturePixels(Camera camera, string path)
        {
            var target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                var previous = RenderTexture.active;
                var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                try
                {
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    pixels.Apply();
                    if (path != null) File.WriteAllBytes(path, pixels.EncodeToPNG());
                    return pixels;
                }
                finally { RenderTexture.active = previous; }
            }
            finally { RenderTexture.ReleaseTemporary(target); }
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
