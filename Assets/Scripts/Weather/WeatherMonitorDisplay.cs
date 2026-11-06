using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;
using UnityEngine.InputSystem;
using MoonThemedGame.Interactions;

namespace MoonThemedGame.Weather
{
    [DisallowMultipleComponent]
    public sealed class WeatherMonitorDisplay : MonoBehaviour
    {
        public MoonWeatherAssets assets;
        public MeshRenderer screenRenderer;
        public Vector3 screenCenter, screenNormal, screenUp;
        public Vector2 screenSize;
        [Min(0)] public float focusDuration = 0.9f;
        public bool IsFocused { get; private set; }
        public bool IsTransitioning => transitionDirection != 0;
        Camera focusedCamera;
        FirstPersonController movement;
        Vector3 previousPosition, focusPosition;
        Quaternion previousRotation, focusRotation;
        float previousFov, transition;
        int transitionDirection;
        CursorLockMode previousLock;
        bool previousCursorVisible;
        RenderTexture display;
        Material material;
        PanelSettings panel;
        GameObject uiHost;
        VisualElement root;
        float refreshAt;
        public RenderTexture DisplayTexture => display;
        public VisualElement Root => root;

        void OnEnable() { if (Application.isPlaying) BuildDisplay(); }
        public void BuildDisplay()
        {
            if (display || !assets || !screenRenderer) return;
            display = new RenderTexture(assets.resolution.x, assets.resolution.y, 24, RenderTextureFormat.ARGB32)
                { name = "Smaller monitor / ENV-LINK", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            display.Create();
            material = new Material(assets.screenMaterial) { hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_BaseMap", display); screenRenderer.sharedMaterial = material;
            panel = ScriptableObject.CreateInstance<PanelSettings>(); panel.hideFlags = HideFlags.HideAndDontSave;
            panel.themeStyleSheet = assets.theme; panel.targetTexture = display; panel.scaleMode = PanelScaleMode.ConstantPixelSize; panel.scale = 1;
            panel.clearColor = true; panel.colorClearValue = Color.black;
            panel.SetScreenToPanelSpaceFunction(_ => new Vector2(-10000, -10000));
            uiHost = new GameObject("Smaller monitor / weather UI") { hideFlags = HideFlags.HideAndDontSave }; uiHost.transform.SetParent(transform, false);
            var document = uiHost.AddComponent<UIDocument>(); document.panelSettings = panel; document.visualTreeAsset = assets.layout;
            root = document.rootVisualElement; root.style.width = assets.resolution.x; root.style.height = assets.resolution.y;
            root.style.unityFontDefinition = FontDefinition.FromFont(assets.font); root.styleSheets.Add(assets.styleSheet);
            Loc.Stamp(root);
            Refresh();
            var collider = GetComponent<MeshCollider>();
            if (!collider) collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = GetComponent<MeshFilter>().sharedMesh;
            if (!GetComponent<WeatherMonitorInteractable>()) gameObject.AddComponent<WeatherMonitorInteractable>();
        }
        void Update()
        {
            if(MoonThemedGame.Interactions.DeveloperConsole.BlockGameplayInput)return;
            if (root != null && Time.time >= refreshAt) Refresh();
            if (IsTransitioning)
            {
                transition = Mathf.Clamp01(transition + transitionDirection * Time.deltaTime / Mathf.Max(0.001f, focusDuration));
                ApplyCameraPose();
                if (transitionDirection > 0 ? transition >= 1 : transition <= 0) FinishTransition();
            }
            else if (IsFocused && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ExitFocus();
        }

        public void Focus(bool instant = false)
        {
            if (IsFocused || !Camera.main) return;
            focusedCamera = Camera.main;
            previousPosition = focusedCamera.transform.position; previousRotation = focusedCamera.transform.rotation;
            previousFov = focusedCamera.fieldOfView; previousLock = Cursor.lockState; previousCursorVisible = Cursor.visible;
            movement = focusedCamera.GetComponentInParent<FirstPersonController>();
            if (movement && !movement.enabled) movement = null;
            if (movement) movement.enabled = false;
            float tangent = Mathf.Tan(42 * 0.5f * Mathf.Deg2Rad);
            float distance = Mathf.Max(screenSize.y / (2 * tangent), screenSize.x / (2 * tangent * focusedCamera.aspect)) * 1.2f;
            var normal = transform.TransformDirection(screenNormal).normalized;
            focusPosition = transform.TransformPoint(screenCenter) + normal * distance;
            focusRotation = Quaternion.LookRotation(-normal, transform.TransformDirection(screenUp).normalized);
            IsFocused = true; transition = 0; transitionDirection = 1;
            if (instant || focusDuration <= 0) { transition = 1; FinishTransition(); }
        }

        public void ExitFocus(bool instant = false)
        {
            if (!IsFocused) return;
            transitionDirection = -1;
            if (instant || focusDuration <= 0 || !focusedCamera) { transition = 0; FinishTransition(); }
        }

        void ApplyCameraPose()
        {
            if (!focusedCamera) return;
            float t = transition * transition * (3 - 2 * transition);
            focusedCamera.transform.SetPositionAndRotation(Vector3.Lerp(previousPosition, focusPosition, t) +
                Vector3.down * (Mathf.Sin(t * Mathf.PI) * 0.06f), Quaternion.Slerp(previousRotation, focusRotation, t));
            focusedCamera.fieldOfView = Mathf.Lerp(previousFov, 42, t);
        }

        void FinishTransition()
        {
            ApplyCameraPose();
            if (transitionDirection > 0) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            else
            {
                if (movement) movement.enabled = true;
                Cursor.lockState = previousLock; Cursor.visible = previousCursorVisible;
                focusedCamera = null; movement = null; IsFocused = false;
            }
            transitionDirection = 0;
        }
        public void Refresh()
        {
            var weather = MoonWeatherSystem.Active;
            if (root == null || !weather) return;
            refreshAt = Time.time + 0.2f;
            Loc.Apply(root);
            SetIcon("current-icon", weather.Current);
            root.Q<Label>("current-name").text = Loc.T(MoonWeatherSystem.Name(weather.Current));
            root.Q<Label>("current-effect").text = Loc.T(MoonWeatherSystem.Effect(weather.Current));
            root.Q<Label>("current-time").text = Loc.F("CHANGES IN {0}", Clock(weather.Remaining));
            root.Q("progress").style.width = Length.Percent(weather.Remaining / weather.CurrentDuration * 100);
            for (int i = 0; i < 2; i++)
            {
                var next = weather.Forecast(i); SetIcon("next-icon-" + i, next.kind);
                root.Q<Label>("next-name-" + i).text = Loc.T(MoonWeatherSystem.Name(next.kind));
                root.Q<Label>("next-time-" + i).text = Loc.F("IN {0}", Clock(next.startsIn));
                root.Q<Label>("next-effect-" + i).text = Loc.T(MoonWeatherSystem.Effect(next.kind));
            }
            float multiplier = weather.Current == WeatherKind.Radiation ? 1 + (weather.toxicEnergyMultiplier - 1) * weather.Intensity : 1;
            root.Q<Label>("weather-energy").text = Loc.F("ENERGY DRAIN x{0}", multiplier.ToString("0.0", CultureInfo.InvariantCulture));
            root.Q<Label>("weather-notice").text = weather.Current == WeatherKind.MeteoriteRain
                ? Loc.F("IMPACT ZONES MARKED {0}s BEFORE", Mathf.Max(5,weather.warningSeconds).ToString("0",CultureInfo.InvariantCulture))
                : Loc.T("BASE INTERIOR / SHELTERED");
            root.Q<Label>("weather-clock").text = Loc.F("SURFACE FORECAST / T+{0}", Clock(Time.time));
        }
        void SetIcon(string id, WeatherKind kind)
        {
            var icon = root.Q<Image>(id); icon.vectorImage = assets.Icon(kind); icon.scaleMode = ScaleMode.ScaleToFit; icon.tintColor = Color.white;
        }
        public static string Clock(float seconds)
        {
            int value = Mathf.CeilToInt(Mathf.Max(0, seconds)); return $"{value / 60:00}:{value % 60:00}";
        }
        void OnDisable()
        {
            ExitFocus(true);
            var interactable = GetComponent<WeatherMonitorInteractable>();
            if (interactable) interactable.SetHighlighted(false);
            if (screenRenderer && assets && screenRenderer.sharedMaterial == material) screenRenderer.sharedMaterial = assets.screenMaterial;
            if (uiHost) Destroy(uiHost); if (panel) Destroy(panel); if (material) Destroy(material);
            if (display) { display.Release(); Destroy(display); } display = null; root = null;
        }
    }
}
