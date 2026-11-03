using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MoonThemedGame.Computer
{
    public enum TerminalLanguage { English, Russian }

    [DisallowMultipleComponent]
    public sealed class PcMonitorTerminal : MonoBehaviour
    {
        [Serializable] sealed class Entry { public string key; public string en; public string ru; }
        [Serializable] sealed class Catalog { public Entry[] entries; }

        public PcMonitorAssets assets;
        public MeshRenderer screenRenderer;
        public MeshCollider screenCollider;
        public Vector3 screenCenter;
        public Vector3 screenNormal = Vector3.back;
        public Vector3 screenUp = Vector3.up;
        public Vector2 screenSize = new Vector2(1.7f, 0.9f);
        public TerminalLanguage language;
        [SerializeField, Min(0)] int credits = 20;
        [Min(1)] public float interactionDistance = 5;

        RenderTexture display;
        Material material;
        PanelSettings panelSettings;
        GameObject uiHost;
        UIDocument document;
        VisualElement root, cursor;
        VisualElement[] icons;
        Catalog catalog;
        bool hovering, previousCursorVisible, focused;
        Camera focusedCamera;
        Vector3 previousPosition;
        Quaternion previousRotation;
        float previousFov;
        CursorLockMode previousLock;
        bool focusCursorVisible;

        public RenderTexture DisplayTexture => display;
        public Material ScreenMaterial => material;
        public bool IsFocused => focused;
        public string HoveredIcon { get; private set; }
        public int Credits => credits;

        void OnEnable()
        {
            if (Application.isPlaying) BuildDisplay();
        }

        public void BuildDisplay()
        {
            if (display || !assets || !screenRenderer) return;
            catalog = assets.translations ? JsonUtility.FromJson<Catalog>(assets.translations.text) : null;
            display = new RenderTexture(assets.resolution.x, assets.resolution.y, 24, RenderTextureFormat.ARGB32)
            {
                name = "PC MONITOR / live desktop", filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
            };
            display.Create();
            material = new Material(assets.screenMaterial) { name = "PC MONITOR / CRT (instance)", hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_BaseMap", display);
            screenRenderer.sharedMaterial = material;
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "PC MONITOR / offscreen panel";
            panelSettings.hideFlags = HideFlags.HideAndDontSave;
            panelSettings.themeStyleSheet = assets.theme;
            panelSettings.targetTexture = display;
            panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            panelSettings.scale = 1;
            panelSettings.clearColor = true;
            panelSettings.colorClearValue = new Color(0.006f, 0.009f, 0.012f, 1);
            // Pointer ownership stays with this physical screen. No overlay panel
            // is allowed to intercept global mouse events or gameplay UI.
            panelSettings.SetScreenToPanelSpaceFunction(_ => new Vector2(-10000, -10000));
            uiHost = new GameObject("PC MONITOR / UI surface") { hideFlags = HideFlags.HideAndDontSave };
            uiHost.transform.SetParent(transform, false);
            document = uiHost.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = assets.layout;
            root = document.rootVisualElement;
            root.style.width = assets.resolution.x;
            root.style.height = assets.resolution.y;
            root.style.unityFontDefinition = FontDefinition.FromFont(assets.font);
            root.styleSheets.Add(assets.styleSheet);
            SetIcon("shop-image", assets.shopIcon);
            SetIcon("scavenger-image", assets.scavengerIcon);
            SetIcon("messages-image", assets.messagesIcon);
            SetIcon("credit-image", assets.coinIcon);
            SetIcon("screen-cursor", assets.cursorIcon);
            cursor = root.Q("screen-cursor");
            cursor.style.display = DisplayStyle.None;
            cursor.pickingMode = PickingMode.Ignore;
            icons = new[] { root.Q("shop"), root.Q("scavenger"), root.Q("messages") };
            ApplyLanguage();
            SetCredits(credits);
        }

        public void SetCredits(int amount)
        {
            credits = Mathf.Max(0, amount);
            var label = root?.Q<Label>("credit-value");
            if (label != null) label.text = credits.ToString(CultureInfo.InvariantCulture);
        }

        void SetIcon(string name, VectorImage icon)
        {
            var image = root.Q<Image>(name);
            image.vectorImage = icon;
            image.scaleMode = ScaleMode.ScaleToFit;
            image.tintColor = Color.white;
        }

        string Text(string key)
        {
            if (catalog?.entries != null)
                foreach (var entry in catalog.entries)
                    if (entry.key == key) return language == TerminalLanguage.Russian ? entry.ru : entry.en;
            return key;
        }

        public void SetLanguage(TerminalLanguage value) { language = value; ApplyLanguage(); }

        void ApplyLanguage()
        {
            if (root == null) return;
            foreach (var label in root.Query<Label>().ToList())
                if (!string.IsNullOrEmpty(label.viewDataKey)) label.text = Text(label.viewDataKey);
        }

        void Update()
        {
            if (!Application.isPlaying || root == null) return;
            bool focusKey = false, escape = false;
            Vector2 position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                focusKey = Keyboard.current.f7Key.wasPressedThisFrame;
                escape = Keyboard.current.escapeKey.wasPressedThisFrame;
            }
            if (Mouse.current != null)
            {
                position = Mouse.current.position.ReadValue();
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            focusKey = Input.GetKeyDown(KeyCode.F7);
            escape = Input.GetKeyDown(KeyCode.Escape);
            position = Input.mousePosition;
#endif
            if (focusKey) { if (focused) ExitFocus(); else Focus(); }
            if (escape) ExitFocus();
            var camera = focusedCamera ? focusedCamera : Camera.main;
            if (camera && TryGetPanelPosition(camera, position, out var panelPoint)) SetPointer(panelPoint);
            else HidePointer();
        }

        public bool TryGetPanelPosition(Camera camera, Vector2 screenPoint, out Vector2 panelPoint)
        {
            panelPoint = default;
            if (!screenCollider || !material) return false;
            var ray = camera.ScreenPointToRay(screenPoint);
            if (!screenCollider.Raycast(ray, out var hit, focused ? 100 : interactionDistance)) return false;
            if (Vector3.Dot(ray.direction, transform.TransformDirection(screenNormal)) >= 0) return false;
            var uv = WarpUV(hit.textureCoord, Time.time, material);
            if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return false;
            panelPoint = new Vector2(uv.x * assets.resolution.x, (1 - uv.y) * assets.resolution.y);
            return true;
        }

        // Same mapping as PCMonitorCRT.hlsl: hit testing follows curved text
        // and both moving waves, so hovering follows the visible icon.
        public static Vector2 WarpUV(Vector2 uv, float time, Material screen)
        {
            var p = uv * 2 - Vector2.one;
            uv = Vector2.one * 0.5f + p * (1 + screen.GetFloat("_Curvature") * p.sqrMagnitude) * 0.5f;
            float speed = screen.GetFloat("_WaveSpeed");
            uv.x += (Mathf.Sin(uv.y * 52 + time * speed) + 0.35f * Mathf.Sin(uv.y * 121 - time * speed * 0.63f)) * screen.GetFloat("_WaveStrength");
            uv.y += Mathf.Sin(time * 0.83f + uv.x * 7) * screen.GetFloat("_VerticalDrift");
            return uv;
        }

        public void SetPointer(Vector2 point)
        {
            if (root == null) return;
            if (!hovering) { previousCursorVisible = Cursor.visible; Cursor.visible = false; hovering = true; }
            cursor.style.display = DisplayStyle.Flex;
            cursor.style.left = Mathf.Clamp(point.x, 0, assets.resolution.x - 20);
            cursor.style.top = Mathf.Clamp(point.y, 0, assets.resolution.y - 30);
            HoveredIcon = null;
            foreach (var icon in icons)
            {
                bool over = icon.worldBound.Contains(point);
                icon.EnableInClassList("pointer-over", over);
                if (over) HoveredIcon = icon.name;
            }
        }

        public void HidePointer()
        {
            if (cursor != null) cursor.style.display = DisplayStyle.None;
            if (icons != null) foreach (var icon in icons) icon.RemoveFromClassList("pointer-over");
            HoveredIcon = null;
            if (!hovering) return;
            Cursor.visible = previousCursorVisible;
            hovering = false;
        }

        public void Focus()
        {
            if (focused || !Camera.main) return;
            focusedCamera = Camera.main;
            previousPosition = focusedCamera.transform.position;
            previousRotation = focusedCamera.transform.rotation;
            previousFov = focusedCamera.fieldOfView;
            previousLock = Cursor.lockState;
            focusCursorVisible = hovering ? previousCursorVisible : Cursor.visible;
            focusedCamera.fieldOfView = 42;
            float tangent = Mathf.Tan(21 * Mathf.Deg2Rad);
            float distance = Mathf.Max(screenSize.y / (2 * tangent), screenSize.x / (2 * tangent * focusedCamera.aspect)) * 1.2f;
            var normal = transform.TransformDirection(screenNormal).normalized;
            var up = transform.TransformDirection(screenUp).normalized;
            focusedCamera.transform.SetPositionAndRotation(transform.TransformPoint(screenCenter) + normal * distance, Quaternion.LookRotation(-normal, up));
            Cursor.lockState = CursorLockMode.None;
            focused = true;
        }

        public void ExitFocus()
        {
            if (!focused) return;
            HidePointer();
            if (focusedCamera)
            {
                focusedCamera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                focusedCamera.fieldOfView = previousFov;
            }
            Cursor.lockState = previousLock;
            Cursor.visible = focusCursorVisible;
            focusedCamera = null;
            focused = false;
        }

        void OnDisable()
        {
            ExitFocus();
            HidePointer();
            if (screenRenderer && assets && screenRenderer.sharedMaterial == material) screenRenderer.sharedMaterial = assets.screenMaterial;
            if (uiHost) Destroy(uiHost);
            if (panelSettings) Destroy(panelSettings);
            if (material) Destroy(material);
            if (display) { display.Release(); Destroy(display); }
            display = null;
            material = null;
            root = null;
        }
    }
}
