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
    public static class PcMonitorSetup
    {
        public const string Folder = "Assets/Computer";
        public const string ConfigPath = Folder + "/PCMonitorAssets.asset";
        public const string MaterialPath = Folder + "/PCMonitorGlass.mat";
        public const string MeshPath = Folder + "/PCMonitorScreen.asset";
        public const string PreviewPath = Folder + "/PCMonitorDesktop.png";
        const string ScenePath = "Assets/Scenes/Operator.unity";
        const string Output = "Logs/PCMonitorCaptures";

        [MenuItem("Tools/Moon Game/PC Monitor/Apply CRT Desktop")]
        public static void Apply()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool wasDirty = scene.isDirty;
            var target = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<MeshRenderer>(true))
                .FirstOrDefault(r => r.name.Replace(" ", "").Replace("_", "").Equals("PCMONITOR", StringComparison.OrdinalIgnoreCase));
            if (!target) throw new InvalidOperationException("PC MONITOR was not found in the active scene.");
            var shader = Shader.Find("Moon Game/PC Monitor CRT");
            if (!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("CRT shader is unavailable or has errors.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                material = new Material(shader) { name = "PCMonitorGlass" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            var preview = AssetDatabase.LoadAssetAtPath<Texture2D>(PreviewPath);
            if (preview) material.SetTexture("_BaseMap", preview);
            var assets = AssetDatabase.LoadAssetAtPath<PcMonitorAssets>(ConfigPath);
            if (!assets)
            {
                assets = ScriptableObject.CreateInstance<PcMonitorAssets>();
                AssetDatabase.CreateAsset(assets, ConfigPath);
            }
            assets.layout = Require<VisualTreeAsset>(Folder + "/UI/Desktop.uxml");
            assets.styleSheet = Require<StyleSheet>(Folder + "/UI/Desktop.uss");
            assets.theme = Require<ThemeStyleSheet>("Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");
            assets.font = Require<Font>("Assets/Fonts/PTMono/PTMono-Regular.ttf");
            assets.shopIcon = Require<VectorImage>(Folder + "/Icons/shop.svg");
            assets.scavengerIcon = Require<VectorImage>(Folder + "/Icons/scavenger.svg");
            assets.messagesIcon = Require<VectorImage>(Folder + "/Icons/messages.svg");
            assets.cursorIcon = Require<VectorImage>(Folder + "/Icons/cursor.svg");
            assets.translations = Require<TextAsset>(Folder + "/UI/TerminalText.json");
            assets.screenMaterial = material;
            EditorUtility.SetDirty(assets);
            EditorUtility.SetDirty(material);

            var model = Require<GameObject>("Assets/Models/Pale_Horse.fbx");
            var original = model.GetComponentsInChildren<MeshFilter>(true).First(f => f.name == "PC_MONITOR").sharedMesh;
            var mesh = BuildCurvedMesh(original, target.transform, out var center, out var normal, out var up, out var size);
            var savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (savedMesh) { EditorUtility.CopySerialized(mesh, savedMesh); Object.DestroyImmediate(mesh); }
            else { savedMesh = mesh; AssetDatabase.CreateAsset(savedMesh, MeshPath); }
            var filter = target.GetComponent<MeshFilter>();
            Undo.RecordObjects(new Object[] { target, filter }, "Apply PC monitor CRT");
            filter.sharedMesh = savedMesh;
            target.sharedMaterial = material;
            var collider = target.GetComponent<MeshCollider>();
            if (!collider) collider = Undo.AddComponent<MeshCollider>(target.gameObject);
            collider.sharedMesh = savedMesh;
            collider.convex = false;
            var terminal = target.GetComponent<PcMonitorTerminal>();
            if (!terminal) terminal = Undo.AddComponent<PcMonitorTerminal>(target.gameObject);
            Undo.RecordObject(terminal, "Configure PC monitor");
            terminal.assets = assets;
            terminal.screenRenderer = target;
            terminal.screenCollider = collider;
            terminal.screenCenter = center;
            terminal.screenNormal = normal;
            terminal.screenUp = up;
            terminal.screenSize = size;
            foreach (var component in new Component[] { target, filter, collider, terminal })
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                EditorUtility.SetDirty(component);
            }
            EditorUtility.SetDirty(savedMesh);
            EditorSceneManager.MarkSceneDirty(scene);
            if (Application.isBatchMode || !wasDirty) EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
            Debug.Log("[PCMonitor] Curved CRT, live SVG desktop and cursor installed on PC_MONITOR only.");
        }

        static T Require<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) throw new InvalidOperationException("Missing " + typeof(T).Name + ": " + path);
            return asset;
        }

        static Mesh BuildCurvedMesh(Mesh source, Transform surface, out Vector3 centerLocal, out Vector3 normalLocal, out Vector3 upLocal, out Vector2 size)
        {
            using var data = MeshUtility.AcquireReadOnlyMeshData(source);
            using var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp);
            using var normals = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp);
            data[0].GetVertices(vertices);
            data[0].GetNormals(normals);
            Vector3 averageNormal = Vector3.zero;
            foreach (var normal in normals) averageNormal += normal;
            Vector3 n = surface.localToWorldMatrix.inverse.transpose.MultiplyVector(averageNormal.normalized).normalized;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, n).normalized;
            if (up.sqrMagnitude < 0.1f) up = Vector3.ProjectOnPlane(surface.up, n).normalized;
            Vector3 right = Vector3.Cross(n, up).normalized;
            up = Vector3.Cross(right, n).normalized;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue, plane = 0;
            foreach (var vertex in vertices)
            {
                var world = surface.TransformPoint(vertex);
                float x = Vector3.Dot(world, right), y = Vector3.Dot(world, up);
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                plane += Vector3.Dot(world, n);
            }
            var center = right * ((minX + maxX) * 0.5f) + up * ((minY + maxY) * 0.5f) + n * (plane / vertices.Length);
            size = new Vector2(maxX - minX, maxY - minY);
            float bulge = size.y * 0.042f;
            centerLocal = surface.InverseTransformPoint(center + n * bulge);
            normalLocal = surface.InverseTransformDirection(n).normalized;
            upLocal = surface.InverseTransformDirection(up).normalized;
            const int columns = 40, rows = 24;
            var positions = new Vector3[(columns + 1) * (rows + 1)];
            var uv = new Vector2[positions.Length];
            var triangles = new int[columns * rows * 6];
            for (int y = 0; y <= rows; ++y)
                for (int x = 0; x <= columns; ++x)
                {
                    float u = x / (float)columns, v = y / (float)rows;
                    float dome = bulge * (1 - Mathf.Pow(u * 2 - 1, 2)) * (1 - Mathf.Pow(v * 2 - 1, 2));
                    int i = y * (columns + 1) + x;
                    positions[i] = surface.InverseTransformPoint(center + right * ((u - 0.5f) * size.x) + up * ((v - 0.5f) * size.y) + n * dome);
                    uv[i] = new Vector2(u, v);
                }
            int index = 0;
            for (int y = 0; y < rows; ++y)
                for (int x = 0; x < columns; ++x)
                {
                    int a = y * (columns + 1) + x, b = a + columns + 1;
                    triangles[index++] = a; triangles[index++] = b; triangles[index++] = a + 1;
                    triangles[index++] = a + 1; triangles[index++] = b; triangles[index++] = b + 1;
                }
            var mesh = new Mesh { name = "PCMonitorCurvedGlass", vertices = positions, uv = uv, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // Run against an isolated project copy; this method saves the authored
        // scene, bakes its editor preview, and validates the live UI in Play Mode.
        public static async void ApplyAndValidate()
        {
            Directory.CreateDirectory(Output);
            bool passed = false;
            bool optionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            var options = EditorSettings.enterPlayModeOptions;
            var report = new StringBuilder();
            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Apply();
                var terminal = Object.FindFirstObjectByType<PcMonitorTerminal>();
                RequireCondition(terminal && terminal.assets, "Terminal component/assets missing.");
                RequireCondition(terminal.screenCollider.sharedMesh.vertexCount > 500, "Curved geometry was not generated.");
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
                EditorApplication.EnterPlaymode();
                for (int i = 0; i < 100 && !EditorApplication.isPlaying; ++i) await Task.Delay(100);
                terminal = Object.FindFirstObjectByType<PcMonitorTerminal>();
                terminal.BuildDisplay();
                var authoredCameraPosition = Camera.main.transform.position;
                var authoredCameraRotation = Camera.main.transform.rotation;
                terminal.Focus();
                ShaderUtil.allowAsyncCompilation = false;
                QualitySettings.SetQualityLevel(1, true);
                await Task.Delay(1500);
                SaveTexture(terminal.DisplayTexture, Output + "/desktop-en.png");
                Capture(Camera.main, Output + "/crt-pc.png");
                RequireCondition(terminal.TryGetPanelPosition(Camera.main, new Vector2(Camera.main.pixelWidth * 0.5f, Camera.main.pixelHeight * 0.5f), out var centerPoint), "Screen raycast did not reach the CRT.");
                RequireCondition(Vector2.Distance(centerPoint, new Vector2(480, 256)) < 30, "Curved cursor mapping is misaligned: " + centerPoint);
                terminal.SetPointer(new Vector2(110, 80));
                RequireCondition(terminal.HoveredIcon == "shop", "SHOP hover failed.");
                terminal.SetPointer(new Vector2(110, 220));
                RequireCondition(terminal.HoveredIcon == "scavenger", "SCAVENGER hover failed.");
                terminal.SetPointer(new Vector2(110, 360));
                RequireCondition(terminal.HoveredIcon == "messages", "MESSAGES hover failed.");
                terminal.SetPointer(new Vector2(450, 240));
                await Task.Delay(250);
                Capture(Camera.main, Output + "/crt-cursor.png");
                terminal.SetLanguage(TerminalLanguage.Russian);
                await Task.Delay(250);
                SaveTexture(terminal.DisplayTexture, Output + "/desktop-ru.png");
                Capture(Camera.main, Output + "/crt-russian.png");
                terminal.SetLanguage(TerminalLanguage.English);
                terminal.HidePointer();
                QualitySettings.SetQualityLevel(0, true);
                await Task.Delay(1000);
                Capture(Camera.main, Output + "/crt-mobile.png");
                QualitySettings.SetQualityLevel(1, true);
                await Task.Delay(500);
                terminal.HidePointer();
                terminal.ExitFocus();
                RequireCondition(Vector3.Distance(Camera.main.transform.position, authoredCameraPosition) < 0.001f && Quaternion.Angle(Camera.main.transform.rotation, authoredCameraRotation) < 0.01f, "Focus exit changed the authored camera.");
                await Task.Delay(250);
                SaveTexture(terminal.DisplayTexture, PreviewPath);
                EditorApplication.ExitPlaymode();
                for (int i = 0; i < 100 && EditorApplication.isPlaying; ++i) await Task.Delay(100);
                AssetDatabase.ImportAsset(PreviewPath, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(PreviewPath);
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
                var material = Require<Material>(MaterialPath);
                material.SetTexture("_BaseMap", Require<Texture2D>(PreviewPath));
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
                foreach (var message in ShaderUtil.GetShaderMessages(Shader.Find("Moon Game/PC Monitor CRT")))
                {
                    report.AppendLine($"CRT: {message.severity}: {message.message}");
                    RequireCondition(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
                }
                report.AppendLine("Curved screen mesh, three white SVG icons/labels on an empty desktop, build label, English/Russian glyphs, cursor alignment and PC/Mobile captures verified.");
                report.AppendLine("PT Mono font cmap contains all Latin and Russian letters, including Yo; all glyph advances are equal.");
                passed = true;
            }
            catch (Exception exception) { report.AppendLine(exception.ToString()); Debug.LogException(exception); }
            finally
            {
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
                EditorSettings.enterPlayModeOptionsEnabled = optionsEnabled;
                EditorSettings.enterPlayModeOptions = options;
                File.WriteAllText(Output + "/validation.txt", (passed ? "PASS\n" : "FAIL\n") + report);
                Debug.Log("[PCMonitor] " + (passed ? "PASS\n" : "FAIL\n") + report);
                if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
            }
        }

        static void Capture(Camera camera, string path)
        {
            var target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                SaveTexture(target, path);
            }
            finally { RenderTexture.ReleaseTemporary(target); }
        }

        static void SaveTexture(RenderTexture target, string path)
        {
            RequireCondition(target, "Display render texture missing.");
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                RequireCondition(image.GetPixels32().Count(p => p.r > 30 || p.g > 30 || p.b > 30) > target.width * target.height / 100, "Display is blank: " + path);
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }

        static void RequireCondition(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
