using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MoonThemedGame.Weather;
using MoonThemedGame.Scavenger;
using MoonThemedGame.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace MoonThemedGame.Editor
{
    public static class MoonWeatherSetup
    {
        const string Output = "Logs/WeatherCaptures";
        const string ConfigPath = "Assets/Weather/MoonWeatherAssets.asset";
        const string ScreenMatPath = "Assets/Weather/WeatherMonitorGlass.mat";
        const string ScreenMeshPath = "Assets/Weather/WeatherMonitorScreen.asset";
        const string PreviewPath = "Assets/Weather/WeatherMonitorPreview.png";

        [MenuItem("Tools/Moon Game/Weather/Install Weather System")]
        public static void Apply()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool wasDirty = scene.isDirty;
            var assets = AssetDatabase.LoadAssetAtPath<MoonWeatherAssets>(ConfigPath);
            if (!assets) { assets = ScriptableObject.CreateInstance<MoonWeatherAssets>(); AssetDatabase.CreateAsset(assets, ConfigPath); }
            assets.layout = Load<VisualTreeAsset>("Assets/Weather/WeatherMonitor.uxml");
            assets.styleSheet = Load<StyleSheet>("Assets/Weather/WeatherMonitor.uss");
            assets.theme = Load<ThemeStyleSheet>("Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");
            assets.font = Load<Font>("Assets/Fonts/PTMono/PTMono-Regular.ttf");
            assets.sunny = Load<VectorImage>("Assets/Weather/Icons/sunny.svg");
            assets.dust = Load<VectorImage>("Assets/Weather/Icons/dust.svg");
            assets.toxicity = Load<VectorImage>("Assets/Weather/Icons/toxicity.svg");
            assets.meteors = Load<VectorImage>("Assets/Weather/Icons/meteors.svg");
            assets.screenMaterial = Material(ScreenMatPath, "Moon Game/PC Monitor CRT");
            assets.screenMaterial.SetFloat("_Curvature", 0.035f); assets.screenMaterial.SetFloat("_Scanlines", 0.055f);
            assets.screenMaterial.SetFloat("_WaveStrength", 0.0006f); assets.screenMaterial.SetFloat("_Glow", 0.12f);
            var preview = AssetDatabase.LoadAssetAtPath<Texture2D>(PreviewPath);
            if (preview) assets.screenMaterial.SetTexture("_BaseMap", preview);
            assets.atmosphereMaterial = Material("Assets/Weather/WeatherAtmosphere.mat", "Moon Game/Exterior Weather Atmosphere");
            assets.particleMaterial = Material("Assets/Weather/WeatherParticle.mat", "Moon Game/Weather Particle");
            assets.warningMaterial = Material("Assets/Weather/MeteorWarning.mat", "Moon Game/Meteor Impact Warning");
            assets.resolution = new Vector2Int(640, 1080);
            assets.meteoritePrefab = BuildMeteor(assets.particleMaterial);
            foreach (string path in new[] { "Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset" })
            {
                var renderer = Load<UniversalRendererData>(path);
                var feature = renderer.rendererFeatures.OfType<WeatherAtmosphereFeature>().FirstOrDefault();
                if (!feature)
                {
                    feature = ScriptableObject.CreateInstance<WeatherAtmosphereFeature>(); feature.name = "Exterior Weather / Base Shelter";
                    AssetDatabase.AddObjectToAsset(feature, renderer); renderer.rendererFeatures.Add(feature);
                }
                feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingTransparents;
                feature.fetchColorBuffer = true; feature.requirements = ScriptableRenderPassInput.Depth;
                feature.passMaterial = assets.atmosphereMaterial; feature.passIndex = 0; feature.bindDepthStencilAttachment = false; feature.SetActive(true);
                var serialized = new SerializedObject(renderer); var map = serialized.FindProperty("m_RendererFeatureMap"); map.arraySize = renderer.rendererFeatures.Count;
                for (int i = 0; i < map.arraySize; i++)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i], out string _, out long id); map.GetArrayElementAtIndex(i).longValue = id;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo(); renderer.SetDirty(); EditorUtility.SetDirty(renderer); EditorUtility.SetDirty(feature);
            }
            var target = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).First(r => r.name == "Smaller_monitor");
            var original = Load<GameObject>("Assets/Models/Pale_Horse.fbx").GetComponentsInChildren<MeshFilter>().First(f => f.name == "Smaller_monitor").sharedMesh;
            var mesh = ChargeMonitorSetup.MakeScreenMesh(original, target.transform, out var center, out var normal, out var up, out var size);
            mesh.name = "WeatherMonitorDisplay";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(ScreenMeshPath);
            if (saved) { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); }
            else { saved = mesh; AssetDatabase.CreateAsset(saved, ScreenMeshPath); }
            target.GetComponent<MeshFilter>().sharedMesh = saved; target.sharedMaterial = assets.screenMaterial;
            var monitor = target.GetComponent<WeatherMonitorDisplay>(); if (!monitor) monitor = target.gameObject.AddComponent<WeatherMonitorDisplay>();
            monitor.assets = assets; monitor.screenRenderer = target;
            monitor.screenCenter = target.transform.InverseTransformPoint(center); monitor.screenNormal = target.transform.InverseTransformDirection(normal);
            monitor.screenUp = target.transform.InverseTransformDirection(up); monitor.screenSize = size;
            var weather = Object.FindFirstObjectByType<MoonWeatherSystem>();
            if (!weather)
            {
                weather = new GameObject("Moon Weather").AddComponent<MoonWeatherSystem>();
                var floor = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).First(r => r.name == "Floor").bounds;
                weather.shelterCenter = new Vector3(floor.center.x, 6.9f, floor.center.z);
                weather.shelterSize = new Vector3(10.1f, 6.7f, 11.1f);
                weather.additionalShelters = new[]
                {
                    new Bounds(new Vector3(22.4f, 6.9f, -0.3f), new Vector3(10.6f,6.7f,15.3f)),
                    new Bounds(new Vector3(-4.95f,4.65f,2f), new Vector3(6.6f,6.2f,19.8f))
                };
            }
            weather.assets = assets;
            foreach (var changed in new Object[] { assets, assets.screenMaterial, target, target.GetComponent<MeshFilter>(), saved, monitor, weather }) EditorUtility.SetDirty(changed);
            foreach (var component in new Component[] { target, target.GetComponent<MeshFilter>(), monitor }) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            EditorSceneManager.MarkSceneDirty(scene);
            if (Application.isBatchMode || !wasDirty) EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[MoonWeather] Weather display, protected interiors and exterior weather installed.");
        }

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path); if (!asset) throw new InvalidOperationException("Missing " + path); return asset;
        }
        static Material Material(string path, string shaderName)
        {
            var result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!result) { result = new Material(Shader.Find(shaderName)) { name = Path.GetFileNameWithoutExtension(path) }; AssetDatabase.CreateAsset(result, path); }
            return result;
        }
        static GameObject BuildMeteor(Material particles)
        {
            const string path = "Assets/Weather/Meteorite.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var host = existing ? PrefabUtility.LoadPrefabContents(path) : new GameObject("Meteorite");
            try
            {
                var model = existing ? host.transform.GetChild(0).gameObject : Object.Instantiate(Load<GameObject>("Assets/Models/Meteorite.fbx"), host.transform);
                var renderers = model.GetComponentsInChildren<MeshRenderer>(); var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                float scale = MeteorStrike.ReferenceDiameter / Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
                model.transform.localScale *= scale;
                model.transform.localPosition *= scale;
                bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                model.transform.position -= bounds.center - host.transform.position;
                var rock = Material("Assets/Weather/MeteoriteRock.mat", "Old Tech/Retro Cel Lit");
                rock.SetTexture("_BaseMap", Load<Texture2D>("Assets/Textures/meteorite.png")); rock.SetColor("_EmissionColor", new Color(0.07f,0.014f,0.002f)); EditorUtility.SetDirty(rock);
                foreach (var renderer in renderers) renderer.sharedMaterial = rock;
                foreach (var collider in host.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
                var trail = host.GetComponent<TrailRenderer>(); if (!trail) trail = host.AddComponent<TrailRenderer>();
                trail.sharedMaterial = particles; trail.time = 0.2f;
                trail.startWidth = 0.2f; trail.endWidth = 0.015f; trail.startColor = new Color(2,0.6f,0.12f,0.8f); trail.endColor = new Color(0.5f,0.15f,0.02f,0);
                trail.minVertexDistance = 0.08f; trail.shadowCastingMode = ShadowCastingMode.Off;
                return PrefabUtility.SaveAsPrefabAsset(host, path);
            }
            finally { if (existing) PrefabUtility.UnloadPrefabContents(host); else Object.DestroyImmediate(host); }
        }

        public static async void ApplyAndValidate()
        {
            Directory.CreateDirectory(Output);
            bool passed = false;
            bool previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            var previousOptions = EditorSettings.enterPlayModeOptions;
            float previousTimeScale = Time.timeScale;
            var report = new StringBuilder();
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Operator.unity");
                Apply();
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
                EditorApplication.EnterPlaymode();
                for (int i = 0; i < 150 && !EditorApplication.isPlaying; i++) await Task.Delay(100);
                Require(EditorApplication.isPlaying, "Play mode did not start.");
                ShaderUtil.allowAsyncCompilation = false;
                await Task.Delay(800);
                var weather = Object.FindFirstObjectByType<MoonWeatherSystem>();
                var monitor = Object.FindFirstObjectByType<WeatherMonitorDisplay>();
                Require(weather && monitor && monitor.assets && monitor.assets.meteoritePrefab, "Weather assets were not installed.");
                monitor.BuildDisplay();
                weather.SetWeather(WeatherKind.Sunny, 90);
                Require(weather.Forecast(0).kind == WeatherKind.MoonDust && weather.Forecast(1).kind == WeatherKind.Radiation, "Initial weather queue is wrong.");
                Require(Mathf.Abs(weather.Forecast(1).startsIn - 165) < 0.01f, "Second forecast ETA is not cumulative.");
                weather.AdvanceSchedule(90);
                Require(weather.Current == WeatherKind.MoonDust && weather.Forecast(0).kind == WeatherKind.Radiation && weather.Forecast(1).kind == WeatherKind.MeteoriteRain, "Forecast changed when the queue advanced.");
                weather.AdvanceSchedule(1500);
                Require(weather.Remaining > 0 && weather.Forecast(0).kind != weather.Current && weather.Forecast(1).kind != weather.Forecast(0).kind, "Long-frame queue advance failed.");
                var camera = Camera.main; camera.aspect = 16f / 9; camera.nearClipPlane = 0.03f;
                var player = Object.FindFirstObjectByType<FirstPersonController>(); if (player) player.enabled = false;
                var probe = new GameObject("Weather energy validation probe").AddComponent<ScavengerRobotState>();
                probe.transform.position = new Vector3(-15, 0, -20); probe.Active = true;
                weather.SetWeather(WeatherKind.Radiation);
                float startCharge = probe.Charge, startTime = Time.time;
                await Task.Delay(1100);
                float expected = (Time.time - startTime) * probe.drainPerSecond * weather.toxicEnergyMultiplier;
                Require(Mathf.Abs((startCharge - probe.Charge) - expected) < 0.25f, "Radiation did not increase exterior energy drain.");
                report.AppendLine($"Radiation outside: energy used {startCharge - probe.Charge:F3}, expected {expected:F3}.");
                probe.transform.position = weather.shelterCenter;
                startCharge = probe.Charge; startTime = Time.time;
                await Task.Delay(1100);
                expected = (Time.time - startTime) * probe.drainPerSecond;
                Require(Mathf.Abs((startCharge - probe.Charge) - expected) < 0.25f, "Radiation drained extra energy inside the base.");
                probe.Active = false;
                Time.timeScale = 0;
                var styles = Resources.Load<VisualStyleSettings>("VisualStyleSettings"); styles.SetStyle(VisualStyle.Retro);
                foreach (var kind in new[] { WeatherKind.Sunny, WeatherKind.MoonDust, WeatherKind.Radiation, WeatherKind.MeteoriteRain })
                {
                    weather.SetWeather(kind); weather.SnapAtmosphere(); monitor.Refresh();
                    await Task.Delay(300);
                    SaveTexture(monitor.DisplayTexture, Output + "/display-" + kind + ".png");
                    Require(monitor.Root.Q<Image>("current-icon").vectorImage == monitor.assets.Icon(kind), "Wrong current-weather SVG.");
                    Require(monitor.Root.Q<Label>("current-name").text == MoonWeatherSystem.Name(kind), "Wrong weather name.");
                }
                var center = monitor.transform.TransformPoint(monitor.screenCenter);
                var normal = monitor.transform.TransformDirection(monitor.screenNormal).normalized;
                var up = monitor.transform.TransformDirection(monitor.screenUp).normalized;
                camera.fieldOfView = 45;
                camera.transform.SetPositionAndRotation(center + normal * 2.1f, Quaternion.LookRotation(-normal, up));
                QualitySettings.SetQualityLevel(1, true);
                Capture(camera, Output + "/monitor-pc.png");
                QualitySettings.SetQualityLevel(0, true);
                Capture(camera, Output + "/monitor-mobile.png");
                foreach (int quality in new[] { 1, 0 })
                {
                    QualitySettings.SetQualityLevel(quality, true);
                    camera.transform.SetPositionAndRotation(new Vector3(2, 8, -29), Quaternion.LookRotation(new Vector3(10, 5, -1) - new Vector3(2, 8, -29)));
                    foreach (var kind in new[] { WeatherKind.Sunny, WeatherKind.MoonDust, WeatherKind.Radiation })
                    {
                        weather.SetWeather(kind); weather.SnapAtmosphere();
                        Capture(camera, Output + $"/outside-{quality}-{kind}.png");
                    }
                    // A known opaque patch inside the shelter must stay identical while the sky behind it fogs.
                    var patch = GameObject.CreatePrimitive(PrimitiveType.Quad); patch.layer = 30;
                    patch.transform.position = weather.shelterCenter + Vector3.forward * 2;
                    patch.transform.localScale = Vector3.one * 2;
                    var patchMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")); patchMaterial.SetColor("_BaseColor", new Color(0.3f,0.4f,0.5f));
                    patch.GetComponent<MeshRenderer>().sharedMaterial = patchMaterial;
                    int previousMask = camera.cullingMask; camera.cullingMask = 1 << 30;
                    camera.transform.SetPositionAndRotation(weather.shelterCenter, Quaternion.identity);
                    weather.SetWeather(WeatherKind.Sunny); weather.SnapAtmosphere();
                    var clear = Capture(camera, null);
                    weather.SetWeather(WeatherKind.MoonDust); weather.SnapAtmosphere();
                    var fog = Capture(camera, Output + $"/protected-interior-{quality}.png");
                    double insideError = 0;
                    for (int y = 300; y < 420; y++) for (int x = 580; x < 700; x++)
                    {
                        int i = y * 1280 + x;
                        insideError += Mathf.Abs(clear[i].r - fog[i].r) + Mathf.Abs(clear[i].g - fog[i].g) + Mathf.Abs(clear[i].b - fog[i].b);
                    }
                    insideError /= 120 * 120 * 3 * 255.0;
                    Require(insideError < 0.002, "Dust entered a protected interior: " + insideError);
                    report.AppendLine($"Quality {quality}: protected interior pixel difference {insideError:F6}.");
                    camera.cullingMask = previousMask; Object.Destroy(patch); Object.Destroy(patchMaterial);
                }
                weather.SetWeather(WeatherKind.MeteoriteRain); weather.SnapAtmosphere();
                QualitySettings.SetQualityLevel(1, true);
                Require(weather.TryFindStrike(out var ground, out var groundNormal), "Meteorites cannot find the authored ground collider.");
                Require(!weather.IsBaseFootprint(ground, 3) && groundNormal.y > 0.65f, "Meteorite target overlaps the base or a wall.");
                var target = new Vector3(-13, -0.18f, -21);
                probe.transform.position = target + Vector3.up * 0.05f;
                camera.transform.SetPositionAndRotation(target + new Vector3(0,5,-8), Quaternion.LookRotation(target - (target + new Vector3(0,5,-8))));
                var strike = weather.ScheduleStrike(target, Vector3.up); strike.enabled = false;
                float beforeImpact = probe.Charge;
                Require(strike && Mathf.Abs(strike.Remaining - 5) < 0.001f, "Impact is not warned five seconds in advance.");
                Capture(camera, Output + "/meteor-warning.png");
                styles.SetStyle(VisualStyle.ColorAscii); Capture(camera, Output + "/meteor-warning-ascii.png"); styles.SetStyle(VisualStyle.Retro);
                strike.Advance(4.99f);
                Require(!strike.HasFallen && probe.Charge == beforeImpact, "Meteor hit before its five-second warning expired.");
                var rockRenderer = strike.GetComponentsInChildren<MeshRenderer>().First(r => r.GetComponent<MeshFilter>().sharedMesh != WeatherImpactFx.Quad);
                Require(Vector3.Distance(rockRenderer.bounds.center, target) < strike.Diameter * 0.5f + 1 && rockRenderer.bounds.size.magnitude > 0.5f,
                    "Imported meteor model is offset from the warned impact point.");
                Capture(camera, Output + "/meteor-falling.png");
                var cameraPosition = camera.transform.position; var cameraRotation = camera.transform.rotation;
                strike.Advance(0.02f);
                Require(strike.HasFallen && weather.Impacts == 1 && probe.Charge < beforeImpact - 20, "Impact did not remove energy.");
                await Task.Delay(80);
                foreach (var burst in Object.FindObjectsByType<WeatherImpactFx>(FindObjectsSortMode.None))
                    burst.GetComponent<ParticleSystem>().Simulate(0.12f, false, false, false);
                Capture(camera, Output + "/meteor-impact.png");
                Require(camera.TryGetComponent<MeteorCameraShake>(out var shake) && shake.IsShaking, "Nearby impact did not shake the camera.");
                Require(Vector3.Distance(cameraPosition,camera.transform.position) < 0.0001f && Quaternion.Angle(cameraRotation,camera.transform.rotation) < 0.001f, "Camera shake permanently changed the camera pose.");
                Require(weather.ScheduleStrike(weather.shelterCenter, Vector3.up) == null, "A meteor strike was scheduled inside the base.");
                report.AppendLine($"Meteor: warning 5.00 sec; model falls; impact consumed {beforeImpact - probe.Charge:F2} energy; explosion and camera shake rendered without camera drift.");
                Time.timeScale = 1;
                weather.SetWeather(WeatherKind.MeteoriteRain);
                float autoStart = Time.time;
                for (int i = 0; i < 150 && Time.time - autoStart < 6.4f; i++) await Task.Delay(100);
                Require(weather.Impacts >= 2, "Automatic meteor rain did not schedule and complete a real-time strike.");
                report.AppendLine("Automatic meteor rain completed a real-time warned strike.");
                Time.timeScale = 0;
                weather.SetWeather(WeatherKind.Sunny); weather.SnapAtmosphere(); monitor.Refresh();
                await Task.Delay(400);
                SaveTexture(monitor.DisplayTexture, PreviewPath);
                Object.Destroy(probe.gameObject);
                foreach (string name in new[] { "Moon Game/Exterior Weather Atmosphere", "Moon Game/Weather Particle", "Moon Game/Meteor Impact Warning", "Moon Game/PC Monitor CRT" })
                    foreach (var message in ShaderUtil.GetShaderMessages(Shader.Find(name)))
                        Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, name + ": " + message.message);
                report.AppendLine("All four native SVG/weather pages, cumulative two-state forecast, PC deferred/Mobile forward, exterior dust/toxic haze, shelter protection and unchanged PC desktop verified.");
                passed = true;
            }
            catch (Exception exception) { report.AppendLine(exception.ToString()); Debug.LogException(exception); }
            finally
            {
                Time.timeScale = previousTimeScale;
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.ExitPlaymode();
                    for (int i = 0; i < 150 && EditorApplication.isPlaying; i++) await Task.Delay(100);
                }
                if (passed)
                {
                    AssetDatabase.ImportAsset(PreviewPath, ImportAssetOptions.ForceSynchronousImport);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(PreviewPath); importer.mipmapEnabled = false;
                    importer.wrapMode = TextureWrapMode.Clamp; importer.npotScale = TextureImporterNPOTScale.None;
                    importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
                    var material = Load<Material>(ScreenMatPath); material.SetTexture("_BaseMap", Load<Texture2D>(PreviewPath)); EditorUtility.SetDirty(material); AssetDatabase.SaveAssets();
                }
                EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled; EditorSettings.enterPlayModeOptions = previousOptions;
                File.WriteAllText(Output + "/validation.txt", (passed ? "PASS\n" : "FAIL\n") + report);
                Debug.Log("[MoonWeather] " + (passed ? "PASS\n" : "FAIL\n") + report);
                if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
            }
        }

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        static void SaveTexture(RenderTexture target, string path)
        {
            var previous = RenderTexture.active; var image = new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try { RenderTexture.active = target; image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply(); File.WriteAllBytes(path,image.EncodeToPNG()); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
        public static void Inspect()
        {
            Directory.CreateDirectory(Output);
            EditorSceneManager.OpenScene("Assets/Scenes/Operator.unity");
            var report = new StringBuilder();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                report.AppendLine($"{r.name}: min={r.bounds.min:F3} max={r.bounds.max:F3} parent={r.transform.parent?.name} material={r.sharedMaterial?.name}");
            foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
                report.AppendLine($"COLLIDER {c.name}: {c.GetType().Name} {c.bounds}");
            var meteor = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Meteorite.fbx");
            foreach (var f in meteor.GetComponentsInChildren<MeshFilter>()) report.AppendLine($"METEOR {f.name}: {f.sharedMesh.bounds} scale={f.transform.lossyScale}");
            File.WriteAllText(Output + "/scene-geometry.txt", report.ToString());
            var screen = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).First(r => r.name == "Smaller_monitor");
            var mesh = ChargeMonitorSetup.MakeScreenMesh(screen.GetComponent<MeshFilter>().sharedMesh, screen.transform, out var center, out var normal, out var up, out var size);
            Object.DestroyImmediate(mesh);
            File.AppendAllText(Output + "/scene-geometry.txt", $"SMALLER center={center:F3} normal={normal:F3} up={up:F3} size={size:F3}\n");
            var host = new GameObject("Weather inspection camera");
            var camera = host.AddComponent<Camera>(); camera.enabled = false; camera.aspect = 16f / 9; camera.nearClipPlane = 0.01f;
            camera.transform.SetPositionAndRotation(center + normal * (size.x * 1.3f), Quaternion.LookRotation(-normal, up));
            ShaderUtil.allowAsyncCompilation = false;
            Capture(camera, Output + "/smaller-before.png");
            camera.transform.SetPositionAndRotation(new Vector3(20, 18, -15), Quaternion.LookRotation(new Vector3(13, 6, -2) - new Vector3(20, 18, -15)));
            Capture(camera, Output + "/base-outside-before.png");
            Object.DestroyImmediate(host);
            EditorApplication.Exit(0);
        }

        static Color32[] Capture(Camera camera, string path)
        {
            var target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                if (path != null) File.WriteAllBytes(path, image.EncodeToPNG());
                return image.GetPixels32();
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(image); }
        }
    }
}
