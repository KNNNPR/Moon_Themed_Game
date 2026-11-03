using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MoonThemedGame.Rendering
{
    // Public methods can also be connected to an options-menu button.
    public sealed class VisualStyleSwitcher : MonoBehaviour
    {
        static VisualStyleSettings settings;
        public static VisualStyleSettings Settings => settings ? settings : settings = Resources.Load<VisualStyleSettings>("VisualStyleSettings");
        public static VisualStyle CurrentStyle => Settings ? Settings.CurrentStyle : VisualStyle.Retro;
        public static void SetStyle(VisualStyle style) { if (Settings) Settings.SetStyle(style); }
        public static void ToggleStyle() { if (Settings) Settings.Toggle(); }
        public static void OverrideAscii(Material material) { if (Settings) Settings.OverrideAscii(material); }
        public static Material AsciiMaterial => Settings ? Settings.AsciiMaterial : null;
        public void UseRetro() => SetStyle(VisualStyle.Retro);
        public void UseColorAscii() => SetStyle(VisualStyle.ColorAscii);
        public void Toggle() => ToggleStyle();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState()
        {
            settings = null;
            if (Settings) Settings.ResetSession();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InstallHotkey()
        {
            if (!Settings || FindFirstObjectByType<VisualStyleSwitcher>()) return;
            var host = new GameObject("Visual Style Switcher (F6)");
            host.AddComponent<VisualStyleSwitcher>();
            DontDestroyOnLoad(host);
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.f6Key.wasPressedThisFrame) ToggleStyle();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.F6)) ToggleStyle();
#endif
        }
    }
}
