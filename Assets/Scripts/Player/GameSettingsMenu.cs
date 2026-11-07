using UnityEngine;
using UnityEngine.InputSystem;
using MoonThemedGame.Computer;
using MoonThemedGame.Weather;
using MoonThemedGame.Scavenger;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;
using System;
using System.Collections.Generic;

namespace MoonThemedGame.Interactions
{
    [DefaultExecutionOrder(-10000)]
    public sealed class GameSettingsMenu : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        static int consumedFrame = -1;
        public static bool BlockGameplayInput => IsOpen || consumedFrame == Time.frameCount || DeveloperConsole.BlockGameplayInput || OperatorWakeUp.IsPlaying || OperatorRadioBriefing.IsPlaying || StationStoryFlow.IsBlocking;
        FirstPersonController player;
        bool restorePlayer, visible, returningToMenu;
        CursorLockMode cursorLock;
        float timeScale;
        PanelSettings panel;
        UIDocument document;
        enum Page { Home, Controls, Quit }
        Page page;
        VisualElement root, homePanel, controlsPanel, quitPanel;
        Label bindStatus;
        GameCommand? capturing;
        int captureFrame;
        readonly Dictionary<GameCommand, Button> bindingButtons = new Dictionary<GameCommand, Button>();
        readonly Dictionary<GameCommand, Label> bindingNames = new Dictionary<GameCommand, Label>();
        public VisualElement Root => root;
        public PanelSettings Panel => panel;
        public bool IsCapturing => capturing.HasValue;
        public GameCommand? Capturing => capturing;
        void Awake() => BuildUI();
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
            AudioListener.volume = PlayerPrefs.GetFloat("audio.master", 0.8f);
            new GameObject("Settings / Escape priority").AddComponent<GameSettingsMenu>();
        }
        void Update()
        {
            if(StationStoryFlow.IsBlocking)return;
            if(OperatorWakeUp.IsPlaying||OperatorRadioBriefing.IsPlaying)return;
            if(DeveloperConsole.BlockGameplayInput)return;
            if(IsOpen && capturing.HasValue)
            {
                if(Time.frameCount<=captureFrame || Keyboard.current==null) return;
                if(Keyboard.current.escapeKey.wasPressedThisFrame) { HandleBack(); return; }
                foreach(var key in Keyboard.current.allKeys)
                {
                    if(!key.wasPressedThisFrame) continue;
                    AcceptRebind(key.keyCode); return;
                }
                return;
            }
            if (!GameControls.Pressed(GameCommand.Menu) && (Keyboard.current==null || !Keyboard.current.escapeKey.wasPressedThisFrame)) return;
            HandleBack();
        }
        public void HandleBack()
        {
            if(OperatorWakeUp.IsPlaying||OperatorRadioBriefing.IsPlaying)return;
            if(DeveloperConsole.BlockGameplayInput)return;
            if(IsOpen && capturing.HasValue) { CancelRebind(); return; }
            if (IsOpen)
            {
                if(page != Page.Home) ShowPage(Page.Home);
                else Close();
                return;
            }
            // Screen controllers receive Escape first; pausing never also happens on that press.
            if(MainMenuController.Active && MainMenuController.Active.IsFocused)return;
            foreach (var pc in FindObjectsByType<PcMonitorTerminal>(FindObjectsSortMode.None)) if (pc.IsFocused) return;
            foreach (var screen in FindObjectsByType<WeatherMonitorDisplay>(FindObjectsSortMode.None)) if (screen.IsFocused) return;
            foreach (var note in FindObjectsByType<NoteInspectable>(FindObjectsSortMode.None)) if (note.Viewing) return;
            foreach (var link in FindObjectsByType<ScavengerLink>(FindObjectsSortMode.None)) if (link.Connected) return;
            Open();
        }
        public void Open()
        {
            if(StationStoryFlow.IsBlocking)return;
            if(OperatorWakeUp.IsPlaying||OperatorRadioBriefing.IsPlaying)return;
            if(DeveloperConsole.BlockGameplayInput)return;
            if (IsOpen) return;
            player = FindFirstObjectByType<FirstPersonController>(); restorePlayer = player && player.enabled;
            cursorLock = Cursor.lockState; visible = Cursor.visible; timeScale = Time.timeScale;
            if (restorePlayer) player.enabled = false;
            if(root==null) BuildUI();
            if(root!=null) root.style.display=DisplayStyle.Flex;
            ShowPage(Page.Home);
            RefreshBindings();
            IsOpen = true; Time.timeScale = 0; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        public void Close()
        {
            if (!IsOpen) return;
            capturing=null;
            if(root!=null) root.style.display=DisplayStyle.None;
            ShowPage(Page.Home);
            IsOpen = false; consumedFrame = Time.frameCount; Time.timeScale = timeScale;
            if (restorePlayer && player) player.enabled = true;
            Cursor.lockState = cursorLock; Cursor.visible = visible; PlayerPrefs.Save();
        }
        static VisualElement Box(string css)
        {
            var box=new VisualElement(); box.AddToClassList(css); return box;
        }
        static Label Text(string value, string css)
        {
            var label=new Label(value); foreach(var name in css.Split(' ')) label.AddToClassList(name); return label;
        }
        static Button ActionButton(string value, Action action, string id=null)
        {
            var button=new Button(action) {text=value,name=id}; button.AddToClassList("sys-button"); return button;
        }
        void BuildUI()
        {
            if(root!=null) return;
            var assets=Resources.Load<SettingsMenuAssets>("SettingsMenuAssets");
            if(!assets) { Debug.LogError("SettingsMenuAssets is missing."); return; }
            panel=ScriptableObject.CreateInstance<PanelSettings>(); panel.hideFlags=HideFlags.HideAndDontSave;
            panel.themeStyleSheet=assets.theme; panel.scaleMode=PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution=new Vector2Int(1280,800); panel.match=1f;
            var host=new GameObject("Station terminal / settings UI"); host.transform.SetParent(transform,false);
            document=host.AddComponent<UIDocument>(); document.panelSettings=panel; document.sortingOrder=1000;
            root=document.rootVisualElement;
            // Stay hidden even if a UI dependency throws during construction.
            root.style.display=DisplayStyle.None;
            root.style.flexGrow=1;
            root.style.unityFontDefinition=FontDefinition.FromFont(assets.font); root.styleSheets.Add(assets.style);
            var scrim=Box("settings-scrim"); root.Add(scrim);
            var shell=Box("settings-shell"); scrim.Add(shell);
            var header=Box("settings-header"); shell.Add(header);
            header.Add(Text("PALE HORSE [STATION TERMINAL]","settings-title"));
            header.Add(Text("LUNAR TECHNICAL STATION 7   BUILD_RUV","station-caption"));
            var body=Box("settings-body"); shell.Add(body);
            homePanel=Box("settings-home"); homePanel.name="settings-home"; body.Add(homePanel);
            homePanel.Add(Text("SETTINGS","section-title settings-heading"));
            var audioRow=Box("audio-row"); homePanel.Add(audioRow);
            var audioLabel=Text("MASTER VOLUME","audio-label"); audioRow.Add(audioLabel);
            var volume=new Slider(0,1) {name="master-volume",value=AudioListener.volume}; volume.AddToClassList("volume-slider");
            var volumeValue=Text(Mathf.RoundToInt(volume.value*100)+"%","volume-value");
            volume.RegisterValueChangedCallback(evt=>{ AudioListener.volume=evt.newValue; PlayerPrefs.SetFloat("audio.master",evt.newValue); volumeValue.text=Mathf.RoundToInt(evt.newValue*100)+"%"; });
            audioRow.Add(volume); audioRow.Add(volumeValue);
            homePanel.Add(ActionButton("LANGUAGE: ENGLISH",Loc.Toggle,"language"));
            homePanel.Add(ActionButton("CONTROLS  >",ShowControls,"controls"));
            homePanel.Add(ActionButton("RESUME",Close,"resume"));
            var mainMenu=ActionButton("MAIN MENU",ReturnToMainMenu,"main-menu");
            mainMenu.style.display=MainMenuController.IsMenuScene?DisplayStyle.None:DisplayStyle.Flex;
            homePanel.Add(mainMenu);
            homePanel.Add(ActionButton("EXIT GAME",RequestQuit,"quit"));
            controlsPanel=Box("settings-controls"); controlsPanel.name="settings-controls"; body.Add(controlsPanel);
            controlsPanel.Add(Text("CONTROLS","section-title"));
            bindStatus=Text("Select a key, then press its replacement.","bind-status"); controlsPanel.Add(bindStatus);
            var scroll=new ScrollView(ScrollViewMode.Vertical) {name="key-bindings"}; scroll.AddToClassList("binding-scroll"); controlsPanel.Add(scroll);
            foreach(GameCommand command in Enum.GetValues(typeof(GameCommand)))
            {
                if (!GameControls.IsAvailable(command)) continue;
                var row=Box("binding-row"); scroll.Add(row);
                var nameLabel=Text(GameControls.Name(command),"binding-name"); bindingNames[command]=nameLabel; row.Add(nameLabel);
                var captured=command;
                var button=ActionButton(GameControls.Label(command),()=>BeginRebind(captured),"bind-"+command);
                button.AddToClassList("binding-key"); bindingButtons[command]=button; row.Add(button);
            }
            var controlActions=Box("text-actions"); controlsPanel.Add(controlActions);
            var reset=ActionButton("RESTORE DEFAULTS",()=>{capturing=null;GameControls.ResetBindings(); bindStatus.text=Loc.T("Default keys restored.");},"reset-keys"); reset.AddToClassList("small-action"); controlActions.Add(reset);
            var back=ActionButton("BACK",()=>ShowPage(Page.Home),"back-controls"); back.AddToClassList("small-action"); controlActions.Add(back);
            quitPanel=Box("quit-confirm"); quitPanel.name="quit-confirm"; body.Add(quitPanel);
            quitPanel.Add(Text("EXIT TO DESKTOP?","section-title"));
            var quitButtons=Box("text-actions"); quitPanel.Add(quitButtons);
            quitButtons.Add(ActionButton("CANCEL",()=>ShowPage(Page.Home),"cancel-quit"));
            quitButtons.Add(ActionButton("EXIT GAME",QuitGame,"confirm-quit"));
            shell.Add(Text("[ESC] BACK    CHANGES SAVED AUTOMATICALLY","muted footer-note"));
            ShowPage(Page.Home);
            root.style.display=DisplayStyle.None;
            GameControls.Changed+=RefreshBindings;
            Loc.Stamp(root); Loc.Apply(root);
            Loc.Changed+=Relocalize;
        }
        void Relocalize()
        {
            Loc.Apply(root);
            RefreshBindings();
        }
        void ShowPage(Page next)
        {
            if(next != Page.Controls && capturing.HasValue) CancelRebind();
            page=next;
            if(homePanel!=null) homePanel.style.display=next==Page.Home?DisplayStyle.Flex:DisplayStyle.None;
            if(controlsPanel!=null) controlsPanel.style.display=next==Page.Controls?DisplayStyle.Flex:DisplayStyle.None;
            if(quitPanel!=null) quitPanel.style.display=next==Page.Quit?DisplayStyle.Flex:DisplayStyle.None;
        }
        public void ShowControls() => ShowPage(Page.Controls);
        void RefreshBindings()
        {
            foreach(var pair in bindingButtons) pair.Value.text=GameControls.Label(pair.Key);
            foreach(var row in bindingNames) row.Value.text=GameControls.Name(row.Key);
        }
        public void BeginRebind(GameCommand command)
        {
            if (!GameControls.IsAvailable(command)) return;
            ShowControls();
            capturing=command; captureFrame=Time.frameCount;
            bindingButtons[command].Blur();
            RefreshBindings(); bindingButtons[command].text=Loc.T("PRESS KEY...");
            bindStatus.text=Loc.F("Waiting: {0}. Press ESC to cancel.", GameControls.Name(command));
        }
        public bool AcceptRebind(Key key)
        {
            if(!capturing.HasValue) return false;
            bool accepted=GameControls.SetBinding(capturing.Value,key,out string message);
            bindStatus.text=message;
            if(accepted) { capturing=null; RefreshBindings(); }
            return accepted;
        }
        void CancelRebind() { capturing=null; RefreshBindings(); bindStatus.text=Loc.T("Assignment cancelled."); }
        public void RequestQuit()
        {
            if(capturing.HasValue) CancelRebind();
            ShowPage(Page.Quit);
        }
        public void QuitGame()
        {
            Close(); PlayerPrefs.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit();
#endif
        }
        public void ReturnToMainMenu()
        {
            if(returningToMenu||MainMenuController.IsMenuScene||!Application.CanStreamedLevelBeLoaded("MENU"))return;
            returningToMenu=true;Close();PlayerPrefs.Save();
            Time.timeScale=1;FirstPersonController.LookFrozen=false;
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            SceneManager.LoadSceneAsync("MENU");
        }
        void OnDisable() { if (IsOpen) Close(); }
        void OnDestroy()
        {
            GameControls.Changed-=RefreshBindings;
            Loc.Changed-=Relocalize;
            if(panel) Destroy(panel);
        }
    }
}
