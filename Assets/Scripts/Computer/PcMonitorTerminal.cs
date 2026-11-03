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
        [Tooltip("Seconds the camera takes to move into the screen and back.")]
        [Min(0)] public float focusDuration = 0.9f;
        [Min(1)] public float aimDotSize = 6;

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
        bool focusCursorVisible, aimed;
        // Camera move: 0 = standing, 1 = at the screen; direction +1 entering, -1 leaving.
        float transition;
        int transitionDirection;
        Vector3 focusPosition;
        Quaternion focusRotation;
        const float FocusFov = 42;

        public RenderTexture DisplayTexture => display;
        public Material ScreenMaterial => material;
        public bool IsFocused => focused;
        public bool IsTransitioning => transitionDirection != 0;
        public bool IsAimed => aimed;
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
            bool interact = false, escape = false;
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 position = center;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                interact = Keyboard.current.eKey.wasPressedThisFrame;
                escape = Keyboard.current.escapeKey.wasPressedThisFrame;
            }
            if (Mouse.current != null)
            {
                position = Mouse.current.position.ReadValue();
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            interact = Input.GetKeyDown(KeyCode.E);
            escape = Input.GetKeyDown(KeyCode.Escape);
            position = Input.mousePosition;
#endif
            aimed = false;
            if (IsTransitioning) { AdvanceTransition(); return; }
            var camera = focusedCamera ? focusedCamera : Camera.main;
            if (focused)
            {
                if (escape) { ExitFocus(); return; }
                if (camera && TryGetPanelPosition(camera, position, out var panelPoint)) SetPointer(panelPoint);
                else HidePointer();
                return;
            }
            // Standing in the room: the screen only reacts to the crosshair from up close.
            HidePointer();
            aimed = camera && TryGetPanelPosition(camera, center, out _);
            if (aimed && interact) Focus();
        }

        void OnGUI()
        {
            if (!aimed || focused) return;
            float size = aimDotSize;
            var previous = GUI.color;
            GUI.color = new Color(1, 1, 1, 0.9f);
            GUI.DrawTexture(new Rect((Screen.width - size) * 0.5f, (Screen.height - size) * 0.5f, size, size), DotTexture);
            GUI.color = previous;
        }

        static Texture2D dotTexture;
        static Texture2D DotTexture
        {
            get
            {
                if (dotTexture) return dotTexture;
                const int size = 32;
                dotTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float d = new Vector2(x + 0.5f - size * 0.5f, y + 0.5f - size * 0.5f).magnitude;
                        dotTexture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(size * 0.5f - d)));
                    }
                dotTexture.Apply();
                return dotTexture;
            }
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

        // Moves the camera into the screen over focusDuration; instant skips the move.
        public void Focus(bool instant = false)
        {
            if (focused || !Camera.main) return;
            focusedCamera = Camera.main;
            previousPosition = focusedCamera.transform.position;
            previousRotation = focusedCamera.transform.rotation;
            previousFov = focusedCamera.fieldOfView;
            previousLock = Cursor.lockState;
            focusCursorVisible = hovering ? previousCursorVisible : Cursor.visible;
            float tangent = Mathf.Tan(FocusFov * 0.5f * Mathf.Deg2Rad);
            float distance = Mathf.Max(screenSize.y / (2 * tangent), screenSize.x / (2 * tangent * focusedCamera.aspect)) * 1.2f;
            var normal = transform.TransformDirection(screenNormal).normalized;
            var up = transform.TransformDirection(screenUp).normalized;
            focusPosition = transform.TransformPoint(screenCenter) + normal * distance;
            focusRotation = Quaternion.LookRotation(-normal, up);
            focused = true;
            transition = 0;
            transitionDirection = 1;
            if (instant || focusDuration <= 0) { transition = 1; FinishTransition(); }
        }

        // Moves the camera back to where it stood; instant skips the move.
        public void ExitFocus(bool instant = false)
        {
            if (!focused) return;
            HidePointer();
            transitionDirection = -1;
            if (instant || focusDuration <= 0 || !focusedCamera) { transition = 0; FinishTransition(); }
        }

        void AdvanceTransition()
        {
            transition = Mathf.Clamp01(transition + transitionDirection * Time.deltaTime / focusDuration);
            ApplyCameraPose();
            if (transitionDirection > 0 ? transition >= 1 : transition <= 0) FinishTransition();
        }

        void ApplyCameraPose()
        {
            if (!focusedCamera) return;
            float t = transition * transition * (3 - 2 * transition); // ease in and out
            // A slight dip mid-move reads as leaning in to the desk.
            var position = Vector3.Lerp(previousPosition, focusPosition, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI) * 0.06f);
            focusedCamera.transform.SetPositionAndRotation(position, Quaternion.Slerp(previousRotation, focusRotation, t));
            focusedCamera.fieldOfView = Mathf.Lerp(previousFov, FocusFov, t);
        }

        void FinishTransition()
        {
            ApplyCameraPose();
            if (transitionDirection > 0)
            {
                Cursor.lockState = CursorLockMode.None;
            }
            else
            {
                Cursor.lockState = previousLock;
                Cursor.visible = focusCursorVisible;
                focusedCamera = null;
                focused = false;
            }
            transitionDirection = 0;
        }

        void OnDisable()
        {
            ExitFocus(true);
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
