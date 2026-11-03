using UnityEngine;
using UnityEngine.InputSystem;
using MoonThemedGame.Computer;
using MoonThemedGame.Weather;
using MoonThemedGame.Scavenger;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Interactions
{
    [DefaultExecutionOrder(-10000)]
    public sealed class GameSettingsMenu : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        static int consumedFrame = -1;
        public static bool BlockGameplayInput => IsOpen || consumedFrame == Time.frameCount;
        FirstPersonController player;
        bool restorePlayer, visible;
        CursorLockMode cursorLock;
        float timeScale;
        GUIStyle title, label;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= Loaded;
            SceneManager.sceneLoaded += Loaded;
            EnsureInstalled();
        }
        static void Loaded(Scene scene, LoadSceneMode mode) => EnsureInstalled();
        static void EnsureInstalled()
        {
            if (FindAnyObjectByType<GameSettingsMenu>()) return;
            IsOpen = false; consumedFrame = -1;
            new GameObject("Settings / Escape priority").AddComponent<GameSettingsMenu>();
            AudioListener.volume = PlayerPrefs.GetFloat("audio.master", 0.8f);
        }
        void Update()
        {
            if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
            if (IsOpen) { Close(); return; }
            // Run before the focus controllers: this Escape belongs to the active screen/view.
            foreach (var pc in FindObjectsByType<PcMonitorTerminal>(FindObjectsSortMode.None)) if (pc.IsFocused) return;
            foreach (var screen in FindObjectsByType<WeatherMonitorDisplay>(FindObjectsSortMode.None)) if (screen.IsFocused) return;
            foreach (var note in FindObjectsByType<NoteInspectable>(FindObjectsSortMode.None)) if (note.Viewing) return;
            foreach (var link in FindObjectsByType<ScavengerLink>(FindObjectsSortMode.None)) if (link.Connected) return;
            Open();
        }
        public void Open()
        {
            if (IsOpen) return;
            player = FindFirstObjectByType<FirstPersonController>(); restorePlayer = player && player.enabled;
            cursorLock = Cursor.lockState; visible = Cursor.visible; timeScale = Time.timeScale;
            if (restorePlayer) player.enabled = false;
            IsOpen = true; Time.timeScale = 0; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false; consumedFrame = Time.frameCount; Time.timeScale = timeScale;
            if (restorePlayer && player) player.enabled = true;
            Cursor.lockState = cursorLock; Cursor.visible = visible; PlayerPrefs.Save();
        }
        void OnGUI()
        {
            if (!IsOpen) return;
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
                label = new GUIStyle(GUI.skin.label) { fontSize = 17 };
            }
            var area = new Rect((Screen.width - 420) / 2f, (Screen.height - 240) / 2f, 420, 240);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 24, area.y + 20, area.width - 48, area.height - 40));
            GUILayout.Label("SETTINGS", title); GUILayout.Space(24);
            GUILayout.Label("MASTER VOLUME  " + Mathf.RoundToInt(AudioListener.volume * 100) + "%", label);
            float volume = GUILayout.HorizontalSlider(AudioListener.volume, 0, 1);
            if (!Mathf.Approximately(volume, AudioListener.volume)) { AudioListener.volume = volume; PlayerPrefs.SetFloat("audio.master", volume); }
            GUILayout.Space(28);
            if (GUILayout.Button("RESUME  [ESC]", GUILayout.Height(35))) Close();
            GUILayout.EndArea();
        }
        void OnDisable() { if (IsOpen) Close(); }
    }
}
