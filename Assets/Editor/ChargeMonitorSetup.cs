using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MoonThemedGame.Editor
{
    public static class ChargeMonitorSetup
    {
        public const string MaterialPath = "Assets/Computer/ChargeMonitorGlass.mat";
        public const string MeshPath = "Assets/Computer/ChargeMonitorScreen.asset";
        const string TexturePath = "Assets/Computer/ChargeMonitorText.png";
        const string Output = "Logs/ChargeMonitorCaptures";

        static bool IsCharge(string name) => name.Replace("_", "").Replace(" ", "").Equals("CHARGEMONITOR", StringComparison.OrdinalIgnoreCase);

        [MenuItem("Tools/Moon Game/Charge Monitor/Apply CHARGE Screen")]
        public static void Apply()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool wasDirty = scene.isDirty;
            var target = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<MeshRenderer>(true)).FirstOrDefault(r => IsCharge(r.name));
            if (!target) throw new InvalidOperationException("Charge_Monitor was not found in the active scene.");
            var shader = Shader.Find("Moon Game/Charge Monitor CRT");
            if (!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("CHARGE CRT shader is unavailable or has compiler errors.");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (!texture) throw new InvalidOperationException("CHARGE text texture is missing.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                material = new Material(shader) { name = "ChargeMonitorGlass" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.SetTexture("_BaseMap", texture);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Pale_Horse.fbx");
            var source = model.GetComponentsInChildren<MeshFilter>(true).First(f => IsCharge(f.name)).sharedMesh;
            var mesh = MakeScreenMesh(source, target.transform, out _, out _, out _, out _);
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (saved) { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); }
            else { saved = mesh; AssetDatabase.CreateAsset(saved, MeshPath); }
            var filter = target.GetComponent<MeshFilter>();
            Undo.RecordObjects(new Object[] { target, filter }, "Apply CHARGE CRT screen");
            filter.sharedMesh = saved;
            target.sharedMaterial = material;
            foreach (var component in new Component[] { target, filter })
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                EditorUtility.SetDirty(component);
            }
            EditorUtility.SetDirty(material);
            EditorUtility.SetDirty(saved);
            EditorSceneManager.MarkSceneDirty(scene);
            if (Application.isBatchMode || !wasDirty) EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
            Debug.Log("[ChargeMonitor] CHARGE CRT applied to Charge_Monitor with independent letter flicker.");
        }

        // Keep the authored rounded silhouette and triangles. Only the screen's
        // atlas UVs are replaced with an upright full-display mapping.
        static Mesh MakeScreenMesh(Mesh source, Transform surface, out Vector3 center, out Vector3 normal, out Vector3 up, out Vector2 size)
        {
            using var data = MeshUtility.AcquireReadOnlyMeshData(source);
            using var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp);
            using var normals = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp);
            using var indices = new NativeArray<int>(data[0].GetSubMesh(0).indexCount, Allocator.Temp);
            data[0].GetVertices(vertices);
            data[0].GetNormals(normals);
            data[0].GetIndices(indices, 0);
            var meanNormal = Vector3.zero;
            center = Vector3.zero;
            foreach (var n in normals) meanNormal += n;
            normal = surface.localToWorldMatrix.inverse.transpose.MultiplyVector(meanNormal.normalized).normalized;
            up = Vector3.ProjectOnPlane(Vector3.up, normal).normalized;
            if (up.sqrMagnitude < 0.1f) up = Vector3.ProjectOnPlane(surface.up, normal).normalized;
            var right = Vector3.Cross(normal, up).normalized;
            up = Vector3.Cross(right, normal).normalized;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var vertex in vertices)
            {
                var world = surface.TransformPoint(vertex);
                center += world;
                float x = Vector3.Dot(world, right), y = Vector3.Dot(world, up);
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            center /= vertices.Length;
            size = new Vector2(maxX - minX, maxY - minY);
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; ++i)
            {
                var world = surface.TransformPoint(vertices[i]);
                uv[i] = new Vector2((Vector3.Dot(world, right) - minX) / size.x, (Vector3.Dot(world, up) - minY) / size.y);
            }
            var mesh = new Mesh
            {
                name = "ChargeMonitorDisplay", vertices = vertices.ToArray(), normals = normals.ToArray(),
                uv = uv, triangles = indices.ToArray()
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        public static async void ApplyAndValidate()
        {
            Directory.CreateDirectory(Output);
            bool passed = false;
            var report = new StringBuilder();
            GameObject captureHost = null;
            Material material = null;
            float chance = 0.72f;
            int originalQuality = QualitySettings.GetQualityLevel();
            bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Operator.unity", OpenSceneMode.Single);
                Apply();
                var target = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).First(r => IsCharge(r.name));
                var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Pale_Horse.fbx");
                var source = model.GetComponentsInChildren<MeshFilter>(true).First(f => IsCharge(f.name)).sharedMesh;
                var reference = MakeScreenMesh(source, target.transform, out var center, out var normal, out var up, out var size);
                Object.DestroyImmediate(reference);
                if (target.GetComponent<MeshFilter>().sharedMesh.vertexCount != source.vertexCount)
                    throw new InvalidOperationException("Authored rounded screen geometry changed.");
                captureHost = new GameObject("CHARGE validation camera") { hideFlags = HideFlags.HideAndDontSave };
                var camera = captureHost.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.aspect = 16f / 9f;
                camera.orthographicSize = Mathf.Max(size.y * 0.7f, size.x / (2 * camera.aspect) * 1.16f);
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = size.x * 30;
                camera.transform.SetPositionAndRotation(center + normal * (size.x + 1), Quaternion.LookRotation(-normal, up));
                ShaderUtil.allowAsyncCompilation = false;
                material = target.sharedMaterial;
                chance = material.GetFloat("_LetterFlickerChance");
                QualitySettings.SetQualityLevel(1, true);
                await Task.Delay(800);
                material.SetFloat("_PreviewTime", 0);
                material.SetFloat("_LetterFlickerChance", 0);
                var baseline = Capture(camera, Output + "/charge-pc.png");
                var right = Vector3.Cross(normal, up).normalized;
                var areas = Enumerable.Range(0, 6).Select(i =>
                {
                    float u = 0.125f + (i + 0.5f) * 0.125f;
                    var point = camera.WorldToViewportPoint(center + right * ((u - 0.5f) * size.x));
                    float width = size.x / (2 * camera.orthographicSize * camera.aspect) * 1280;
                    float height = size.y / (2 * camera.orthographicSize) * 720;
                    return new RectInt(Mathf.RoundToInt(point.x * 1280 - width * 0.052f), Mathf.RoundToInt(point.y * 720 - height * 0.33f), Mathf.RoundToInt(width * 0.104f), Mathf.RoundToInt(height * 0.66f));
                }).ToArray();
                var light = areas.Select(a => Brightness(baseline, a)).ToArray();
                var observed = new System.Collections.Generic.HashSet<int>();
                material.SetFloat("_LetterFlickerChance", chance);
                for (int frame = 1; frame <= 75; ++frame)
                {
                    material.SetFloat("_PreviewTime", frame * 0.04f);
                    var pixels = Capture(camera, null);
                    var dim = Enumerable.Range(0, 6).Where(i => Brightness(pixels, areas[i]) < light[i] * 0.55).ToArray();
                    if (dim.Length > 1) throw new InvalidOperationException("More than one letter fully dimmed at once.");
                    if (dim.Length == 1 && observed.Add(dim[0])) Capture(camera, Output + "/flicker-" + "CHARGE"[dim[0]] + ".png");
                }
                if (observed.Count < 2) throw new InvalidOperationException("Individual random letter flicker was not observed.");
                report.AppendLine("Observed independent brightness dips on letters: " + string.Join(", ", observed.Select(i => "CHARGE"[i])));
                material.SetFloat("_LetterFlickerChance", 0);
                material.SetFloat("_PreviewTime", 0);
                QualitySettings.SetQualityLevel(0, true);
                await Task.Delay(800);
                Capture(camera, Output + "/charge-mobile.png");
                foreach (string name in new[] { "Moon Game/Charge Monitor CRT", "Moon Game/PC Monitor CRT" })
                {
                    var shader = Shader.Find(name);
                    if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Shader compiler errors: " + name);
                    foreach (var message in ShaderUtil.GetShaderMessages(shader))
                        if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) throw new InvalidOperationException(message.message);
                }
                report.AppendLine("PC deferred + Mobile forward captures rendered; rounded screen silhouette retained; both CRT shaders compiled.");
                passed = true;
            }
            catch (Exception exception) { report.AppendLine(exception.ToString()); Debug.LogException(exception); }
            finally
            {
                if (material)
                {
                    material.SetFloat("_PreviewTime", -1);
                    material.SetFloat("_LetterFlickerChance", chance);
                    EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssets();
                }
                QualitySettings.SetQualityLevel(originalQuality, true);
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                if (captureHost) Object.DestroyImmediate(captureHost);
                File.WriteAllText(Output + "/validation.txt", (passed ? "PASS\n" : "FAIL\n") + report);
                Debug.Log("[ChargeMonitor] " + (passed ? "PASS\n" : "FAIL\n") + report);
                if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
            }
        }

        static float Brightness(Color32[] pixels, RectInt area)
        {
            float sum = 0;
            for (int y = Mathf.Max(0, area.yMin); y < Mathf.Min(720, area.yMax); ++y)
                for (int x = Mathf.Max(0, area.xMin); x < Mathf.Min(1280, area.xMax); ++x)
                {
                    var p = pixels[y * 1280 + x];
                    sum += p.r + p.g + p.b;
                }
            return sum;
        }

        static Color32[] Capture(Camera camera, string path)
        {
            var target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                var pixels = image.GetPixels32();
                if (pixels.Count(p => p.r > 30 || p.g > 30 || p.b > 30) < pixels.Length / 100)
                    throw new InvalidOperationException("CHARGE capture is blank.");
                if (pixels.Count(p => p.r > 230 && p.b > 230 && p.g < 30) > pixels.Length / 100)
                    throw new InvalidOperationException("CHARGE capture contains error-shader magenta.");
                if (path != null) File.WriteAllBytes(path, image.EncodeToPNG());
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
