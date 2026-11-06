using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MoonThemedGame.Computer
{
    // Operator login over the desktop until the first successful log in of the session.
    // The credentials are the ones printed on the note next to the monitor.
    [DisallowMultipleComponent]
    public sealed class PcLogin : MonoBehaviour
    {
        enum Stage { Waiting, Verifying, Granted, Done }

        [Min(1)] public int maxLength = 16;
        public float verifyTime = 1.1f;
        public float grantedTime = 0.9f;

        static readonly Color Text = new Color(0.94f, 0.96f, 0.97f);
        static readonly Color Muted = new Color(0.58f, 0.64f, 0.67f);
        static readonly Color Good = new Color(0.55f, 1f, 0.6f);
        static readonly Color Alarm = new Color(1f, 0.38f, 0.3f);

        PcMonitorTerminal terminal;
        VisualElement overlay, cursor;
        Label userField, password, status;
        Stage stage;
        string user = "", typed = "";
        bool editingPassword;
        float stageTime;

        public bool Unlocked => stage == Stage.Done;

        public static bool IsUnlocked(PcMonitorTerminal terminal) =>
            !terminal || !terminal.TryGetComponent(out PcLogin login) || login.Unlocked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallOnLoad()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Install();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install();

        static void Install()
        {
            var terminal = FindAnyObjectByType<PcMonitorTerminal>();
            if (terminal && !terminal.GetComponent<PcLogin>()) terminal.gameObject.AddComponent<PcLogin>();
        }

        void Awake() => terminal = GetComponent<PcMonitorTerminal>();

        void Start()
        {
            Loc.Changed += Relocalize;
            Relocalize();
        }

        void OnDestroy() => Loc.Changed -= Relocalize;

        // Also switches the teammate's desktop labels (SHOP / SCAVENGER / ...) with the game language.
        void Relocalize()
        {
            if (terminal) terminal.SetLanguage(Loc.Russian ? TerminalLanguage.Russian : TerminalLanguage.English);
            if (overlay != null && stage == Stage.Waiting) Loc.Apply(overlay);
        }

#if ENABLE_INPUT_SYSTEM
        void OnEnable()
        {
            if (Keyboard.current != null) Keyboard.current.onTextInput += OnText;
        }

        void OnDisable()
        {
            if (Keyboard.current != null) Keyboard.current.onTextInput -= OnText;
        }
#endif

        bool Accepting => stage == Stage.Waiting && overlay != null && terminal && terminal.IsFocused && !terminal.IsTransitioning;

        void OnText(char c)
        {
            if (!Accepting || char.IsControl(c)) return;
            if (editingPassword) { if (typed.Length < maxLength) typed += c; }
            else if (user.Length < maxLength) user += c;
            status.text = "";
        }

        void Update()
        {
            if(MoonThemedGame.Interactions.DeveloperConsole.BlockGameplayInput)return;
            if (stage == Stage.Done || !terminal) return;
            if (overlay == null && terminal.Root != null) Build();
            if (overlay == null) return;
            overlay.BringToFront();
            cursor?.BringToFront();

            bool enter = false, backspace = false, tab = false;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                enter = Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame;
                backspace = Keyboard.current.backspaceKey.wasPressedThisFrame;
                tab = Keyboard.current.tabKey.wasPressedThisFrame;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            backspace = Input.GetKeyDown(KeyCode.Backspace);
            tab = Input.GetKeyDown(KeyCode.Tab);
            if (Accepting) foreach (char c in Input.inputString) OnText(c);
#endif
            stageTime += Time.deltaTime;
            switch (stage)
            {
                case Stage.Waiting:
                    if (!Accepting) break;
                    if (backspace)
                    {
                        if (editingPassword && typed.Length > 0) typed = typed.Substring(0, typed.Length - 1);
                        else if (editingPassword) editingPassword = false;
                        else if (user.Length > 0) user = user.Substring(0, user.Length - 1);
                    }
                    if (tab) editingPassword = !editingPassword;
                    else if (enter && !editingPassword)
                    {
                        if (user.Length == 0) SetStatus(Loc.T("USER REQUIRED"), Alarm);
                        else editingPassword = true;
                    }
                    else if (enter)
                    {
                        if (user.Length == 0) { editingPassword = false; SetStatus(Loc.T("USER REQUIRED"), Alarm); }
                        else if (typed.Length == 0) SetStatus(Loc.T("PASSWORD REQUIRED"), Alarm);
                        else { SetStage(Stage.Verifying); SetStatus(Loc.T("VERIFYING"), Muted); }
                    }
                    break;
                case Stage.Verifying:
                    status.text = Loc.T("VERIFYING") + new string('.', 1 + Mathf.FloorToInt(stageTime * 4) % 3);
                    if (stageTime < verifyTime) break;
                    if (Valid) { SetStage(Stage.Granted); SetStatus(Loc.F("ACCESS GRANTED - WELCOME, {0}", user.ToUpperInvariant()), Good); }
                    else
                    {
                        SetStage(Stage.Waiting);
                        SetStatus(Loc.T("ACCESS DENIED - INVALID USER OR PASSWORD"), Alarm);
                        typed = "";
                        editingPassword = true;
                    }
                    break;
                case Stage.Granted:
                    if (stageTime >= grantedTime)
                    {
                        SetStage(Stage.Done);
                        overlay.RemoveFromHierarchy();
                        return;
                    }
                    break;
            }
            bool caret = stage == Stage.Waiting && Mathf.Repeat(Time.time * 2, 1) < 0.5f;
            userField.text = user + (caret && !editingPassword ? "_" : " ");
            password.text = new string('*', typed.Length) + (caret && editingPassword ? "_" : " ");
            userField.style.color = editingPassword ? Muted : Text;
            password.style.color = editingPassword ? Text : Muted;
        }

        // Login is not case sensitive, the password is.
        bool Valid => string.Equals(user.Trim(), NotesCredentials.Login, System.StringComparison.OrdinalIgnoreCase) &&
            typed == NotesCredentials.Password;

        void SetStage(Stage value)
        {
            stage = value;
            stageTime = 0;
        }

        void SetStatus(string value, Color color)
        {
            status.text = value;
            status.style.color = color;
        }

        void Build()
        {
            cursor = terminal.Root.Q("screen-cursor");
            var desktop = cursor != null ? cursor.parent : terminal.Root;
            overlay = new VisualElement { name = "login", pickingMode = PickingMode.Ignore };
            var s = overlay.style;
            s.position = Position.Absolute;
            s.left = s.top = s.right = s.bottom = 0;
            s.backgroundColor = new Color(0.012f, 0.024f, 0.031f, 1);
            s.paddingLeft = 120;
            s.paddingTop = 92;
            overlay.Add(MakeLabel("PALE HORSE // STATION TERMINAL", 22, Text));
            overlay.Add(MakeLabel("LUNAR TECHNICAL STATION 7   BUILD_RUV", 13, Muted));
            overlay.Add(Space(36));
            overlay.Add(MakeLabel("OPERATOR LOGIN", 18, Text));
            overlay.Add(Space(14));
            userField = MakeLabel("_", 18, Text);
            overlay.Add(Field("USER", userField));
            password = MakeLabel(" ", 18, Muted);
            overlay.Add(Field("PASSWORD", password));
            overlay.Add(Space(18));
            overlay.Add(MakeLabel("[ENTER] NEXT / LOG IN    [TAB] SWITCH FIELD", 13, Muted));
            overlay.Add(Space(10));
            status = MakeLabel("", 16, Muted);
            overlay.Add(status);
            desktop.Add(overlay);
            Loc.Stamp(overlay);
            Loc.Apply(overlay);
        }

        static VisualElement Field(string name, Label value)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 6;
            var label = MakeLabel(name, 18, Muted);
            label.style.width = 150;
            row.Add(label);
            row.Add(value);
            return row;
        }

        static VisualElement Space(float height)
        {
            var space = new VisualElement { pickingMode = PickingMode.Ignore };
            space.style.height = height;
            return space;
        }

        static Label MakeLabel(string value, int size, Color color)
        {
            var label = new Label(value) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
            label.style.paddingLeft = label.style.paddingRight = label.style.paddingTop = label.style.paddingBottom = 0;
            return label;
        }
    }
}
