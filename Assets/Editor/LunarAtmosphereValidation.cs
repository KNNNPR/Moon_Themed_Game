using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MoonThemedGame.Rendering;
using MoonThemedGame.Weather;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace MoonThemedGame.Editor
{
    public static class LunarAtmosphereValidation
    {
        const string Output = "Logs/LunarAtmosphereCaptures";
        [MenuItem("Tools/Moon Game/Atmosphere/Select Settings")]
        public static void SelectSettings() => Selection.activeObject = LunarAtmosphereSettings.Current;

        // Run in an isolated project copy: captures use real depth, lighting and both URP renderers.
        public static async void Run()
        {
            Directory.CreateDirectory(Output);
            var report = new StringBuilder();
            bool passed = false, oldAsync = ShaderUtil.allowAsyncCompilation;
            int quality = QualitySettings.GetQualityLevel();
            GameObject host = null, board = null;
            Material boardMaterial = null;
            var look = LunarAtmosphereSettings.Current;
            var styles = Resources.Load<VisualStyleSettings>("VisualStyleSettings");
            Material retro = styles ? styles.RetroMaterial : null, ascii = styles ? styles.AsciiMaterial : null;
            string savedStyle = styles ? EditorJsonUtility.ToJson(styles) : "";
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Operator.unity");
                Require(look && styles && retro && ascii, "Missing atmosphere / style assets.");
                ShaderUtil.allowAsyncCompilation = false;
                QualitySettings.SetQualityLevel(1, true);
                var weather = Object.FindFirstObjectByType<MoonWeatherSystem>();
                Require(weather, "No weather/shelter system.");
                weather.SetWeather(WeatherKind.Sunny); weather.SnapAtmosphere();
                var source = Camera.main;
                Require(source, "No main camera.");
                host = new GameObject("Atmosphere validation camera");
                var camera = host.AddComponent<Camera>(); camera.CopyFrom(source); camera.enabled = false;
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.None;
                camera.farClipPlane = 250;
                var landscape = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r =>
                {
                    var f = r.GetComponent<MeshFilter>();
                    string path = f && f.sharedMesh ? AssetDatabase.GetAssetPath(f.sharedMesh) : "";
                    return path.Contains("LandScape") || path.Contains("second_lan");
                }).ToArray();
                Bounds bounds = landscape.Length > 0 ? landscape[0].bounds : new Bounds(new Vector3(0, 2, 0), new Vector3(120, 10, 120));
                foreach (var r in landscape) bounds.Encapsulate(r.bounds);
                report.AppendLine("Landscape bounds: " + bounds);
                var eye = new Vector3(bounds.center.x - bounds.size.x * 0.2f, bounds.min.y + 6, bounds.center.z - bounds.size.z * 0.22f);
                var target = new Vector3(bounds.center.x, eye.y - 1, bounds.center.z + bounds.size.z * 0.12f);
                Physics.SyncTransforms();
                var hits = Physics.RaycastAll(new Vector3(eye.x, bounds.max.y + 100, eye.z), Vector3.down, bounds.size.y + 200)
                    .Where(h => !h.collider.isTrigger && h.normal.y > 0.6f).OrderBy(h => Mathf.Abs(h.point.y - bounds.center.y)).ToArray();
                if (hits.Length > 0) { eye.y = hits[0].point.y + 2.8f; target.y = eye.y - 0.6f; }
                if (TryRoadView(landscape, bounds.center, out var roadEye, out var roadTarget))
                { eye = roadEye; target = roadTarget; }
                else
                {
                    var lamp=landscape.Where(r=>r.name.StartsWith("LightSeek") && r.bounds.center.y>-10 && r.bounds.center.y<12)
                        .OrderBy(r=>new Vector2(r.bounds.center.x-bounds.center.x,r.bounds.center.z-bounds.center.z).sqrMagnitude).FirstOrDefault();
                    if(lamp)
                    {
                        var direction=bounds.center-lamp.bounds.center; direction.y=0; direction.Normalize();
                        eye=lamp.bounds.center+Vector3.up*3-direction*4;
                        target=eye+direction*50-Vector3.up*4;
                    }
                }
                foreach (var r in landscape.Take(20)) report.AppendLine(r.name + ": " + r.bounds + "; " + string.Join(",", r.sharedMaterials.Where(m=>m).Select(m=>m.name)));
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                report.AppendLine("Exterior eye: " + eye + "; target: " + target);
                await Task.Delay(600);
                var beforeRetro = AssetDatabase.LoadAssetAtPath<Material>("Assets/AtmosphereBaselines/BeforeRetro.mat");
                var beforeAscii = AssetDatabase.LoadAssetAtPath<Material>("Assets/AtmosphereBaselines/BeforeAscii.mat");
                if (beforeRetro && beforeAscii)
                {
                    beforeRetro.SetFloat("_DitherScale", 1); beforeAscii.SetFloat("_DistanceFade", 0);
                    look.lookEnabled = false; styles.Configure(beforeRetro, beforeAscii);
                    styles.SetStyle(VisualStyle.Retro); Capture(camera, "before-retro.png");
                    var baselineRotation=camera.transform.rotation;
                    camera.transform.rotation=Quaternion.Euler(0,-70,0)*baselineRotation;
                    Capture(camera,"before-angle--70.png");
                    camera.transform.rotation=baselineRotation;
                    styles.SetStyle(VisualStyle.ColorAscii); Capture(camera, "before-ascii.png");
                    styles.Configure(retro, ascii); look.lookEnabled = true;
                }
                styles.SetStyle(VisualStyle.Retro); var after = Capture(camera, "exterior-retro.png");
                var repeat = Capture(camera, "exterior-retro-repeat.png");
                Require(after.SequenceEqual(repeat), "Static atmosphere/dither changed between identical frames.");
                styles.SetStyle(VisualStyle.ColorAscii); Capture(camera, "exterior-ascii.png");
                var exteriorRotation=camera.transform.rotation;
                foreach(int yaw in new[]{-70,70})
                {
                    camera.transform.rotation=Quaternion.Euler(0,yaw,0)*exteriorRotation;
                    styles.SetStyle(VisualStyle.Retro);Capture(camera,"exterior-angle-"+yaw+".png");
                }
                camera.transform.rotation=exteriorRotation;
                foreach (var kind in new[] { WeatherKind.MoonDust, WeatherKind.Radiation })
                {
                    weather.SetWeather(kind); weather.SnapAtmosphere();
                    styles.SetStyle(VisualStyle.Retro); Capture(camera, "weather-" + kind + ".png");
                }
                weather.SetWeather(WeatherKind.Sunny); weather.SnapAtmosphere();
                camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                styles.SetStyle(VisualStyle.Retro); Capture(camera, "interior-retro.png");
                QualitySettings.SetQualityLevel(0, true);
                await Task.Delay(300);
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                Capture(camera, "exterior-mobile-retro.png");
                styles.SetStyle(VisualStyle.ColorAscii); Capture(camera, "exterior-mobile-ascii.png");

                // A neutral unlit card isolates the actual GPU shelter/fog and depth-based ASCII paths.
                board = GameObject.CreatePrimitive(PrimitiveType.Quad); board.layer = 30;
                boardMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                boardMaterial.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.7f, 1));
                boardMaterial.SetFloat("_Cull", 0); board.GetComponent<Renderer>().sharedMaterial = boardMaterial;
                camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.transform.SetPositionAndRotation(new Vector3(5000, 5, 5000), Quaternion.identity);
                board.transform.position = new Vector3(5000, 5, 5040); board.transform.localScale = new Vector3(36, 36, 1);
                var shelterCenter = weather.shelterCenter; var shelterSize = weather.shelterSize; var additional = weather.additionalShelters;
                try
                {
                    foreach (int tier in new[] { 1, 0 })
                    {
                        QualitySettings.SetQualityLevel(tier, true);
                        styles.SetStyle(VisualStyle.Retro);
                        float intensity = retro.GetFloat("_Intensity"); retro.SetFloat("_Intensity", 0);
                        look.lookEnabled = false;
                        var clear = Capture(camera, "card-clear-" + tier + ".png");
                        look.lookEnabled = true;
                        weather.shelterCenter = new Vector3(5000, 5, 5020); weather.shelterSize = new Vector3(100, 100, 100);
                        weather.additionalShelters = new[] { new Bounds(new Vector3(5000, 5, 5025), new Vector3(80, 80, 80)) };
                        var sheltered = Capture(camera, "card-sheltered-" + tier + ".png");
                        Require(CentreDifference(clear, sheltered) < 1.0, "Fog leaked into overlapping protected interiors on tier " + tier);
                        weather.shelterCenter = Vector3.zero; weather.shelterSize = Vector3.one; weather.additionalShelters = Array.Empty<Bounds>();
                        var hazy = Capture(camera, "card-outside-" + tier + ".png");
                        Require(CentreBrightness(hazy) < CentreBrightness(clear) * 0.85, "Permanent haze did not obscure the exterior on tier " + tier);
                        retro.SetFloat("_Intensity", intensity);
                        look.lookEnabled = false; styles.SetStyle(VisualStyle.ColorAscii);
                        board.transform.position = new Vector3(5000, 5, 5010); board.transform.localScale = new Vector3(9, 9, 1);
                        var near = Capture(camera, "ascii-near-" + tier + ".png");
                        board.transform.position = new Vector3(5000, 5, 5080); board.transform.localScale = new Vector3(72, 72, 1);
                        var far = Capture(camera, "ascii-far-" + tier + ".png");
                        Require(Brightness(far) < Brightness(near) * 0.75, "Distant ASCII did not thin out on tier " + tier);
                        board.transform.position = new Vector3(5000, 5, 5040); board.transform.localScale = new Vector3(36, 36, 1);
                        report.AppendLine("Tier " + tier + ": overlapping shelters stay clear; exterior haze and distant ASCII verified on GPU.");
                    }
                }
                finally { weather.shelterCenter = shelterCenter; weather.shelterSize = shelterSize; weather.additionalShelters = additional; }
                foreach (string name in new[] { "Moon Game/Exterior Weather Atmosphere", "Old Tech/Retro Dither", "Moon Game/Color ASCII", "Old Tech/Retro Cel Lit", "Old Tech/Retro Cel Transparent" })
                {
                    var shader = Shader.Find(name);
                    Require(shader && !ShaderUtil.ShaderHasError(shader), "Shader errors: " + name);
                    foreach (var message in ShaderUtil.GetShaderMessages(shader))
                        Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
                }
                styles.Configure(retro, ascii); styles.ResetSession();
                Require(EditorJsonUtility.ToJson(styles) == savedStyle, "Saved default style or material references changed.");
                report.AppendLine("PC deferred + Mobile forward; cold haze, palette, cel bands, static dither and both visual modes rendered without shader errors.");
                passed = true;
            }
            catch (Exception exception) { report.AppendLine(exception.ToString()); Debug.LogException(exception); }
            finally
            {
                if (look) { look.lookEnabled = true; look.ApplyCelGlobals(); }
                if (styles) { styles.Configure(retro, ascii); styles.ResetSession(); }
                QualitySettings.SetQualityLevel(quality, true); ShaderUtil.allowAsyncCompilation = oldAsync;
                if (host) Object.DestroyImmediate(host); if (board) Object.DestroyImmediate(board); if (boardMaterial) Object.DestroyImmediate(boardMaterial);
                File.WriteAllText(Output + "/validation.txt", (passed ? "PASS\n" : "FAIL\n") + report);
                Debug.Log("[LunarAtmosphere] " + report);
                if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
            }
        }

        static Color32[] Capture(Camera camera, string filename)
        {
            var target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active; var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                var pixels = image.GetPixels32();
                Require(pixels.Count(p => p.r > 230 && p.g < 30 && p.b > 230) < pixels.Length / 100, "Magenta shader errors: " + filename);
                Require(Brightness(pixels) > 0.15, "Blank output: " + filename);
                File.WriteAllBytes(Output + "/" + filename, image.EncodeToPNG()); return pixels;
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(image); }
        }
        static double Brightness(Color32[] pixels) => pixels.Average(p => (p.r + p.g + p.b) / 3.0);
        static double CentreDifference(Color32[] a, Color32[] b)
        {
            double sum=0; int count=0;
            for(int y=270;y<450;y++) for(int x=550;x<730;x++)
            { int i=y*1280+x; sum+=(Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b))/3.0; count++; }
            return sum/count;
        }
        static double CentreBrightness(Color32[] pixels)
        {
            double sum=0; int count=0;
            for(int y=270;y<450;y++) for(int x=550;x<730;x++)
            { var p=pixels[y*1280+x]; sum+=(p.r+p.g+p.b)/3.0; count++; }
            return sum/count;
        }
        static bool TryRoadView(MeshRenderer[] renderers, Vector3 desired, out Vector3 eye, out Vector3 target)
        {
            eye=target=Vector3.zero; float best=float.MaxValue;
            foreach(var renderer in renderers)
            {
                var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
                var materials=renderer.sharedMaterials;
                for(int sub=0;sub<Mathf.Min(mesh.subMeshCount,materials.Length);sub++)
                {
                    var material=materials[sub]; if(!material)continue;
                    var texture=material.mainTexture;
                    if(!material.name.ToLowerInvariant().Contains("road") && (!texture || !texture.name.ToLowerInvariant().Contains("road")))continue;
                    var vertices=mesh.vertices; var indices=mesh.GetTriangles(sub);
                    for(int i=0;i<indices.Length;i+=3)
                    {
                        var a=renderer.transform.TransformPoint(vertices[indices[i]]);
                        var b=renderer.transform.TransformPoint(vertices[indices[i+1]]);
                        var c=renderer.transform.TransformPoint(vertices[indices[i+2]]);
                        if(Mathf.Abs(Vector3.Cross(b-a,c-a).normalized.y)<0.7f)continue;
                        var point=(a+b+c)/3; var delta=point-desired; delta.y=0;
                        if(delta.sqrMagnitude>=best)continue;
                        best=delta.sqrMagnitude;
                        var edge=(b-a).sqrMagnitude>(c-a).sqrMagnitude?b-a:c-a; edge.y=0; edge.Normalize();
                        eye=point+Vector3.up*3-edge*5; target=point+edge*35+Vector3.up*2;
                    }
                }
            }
            return best<float.MaxValue;
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
