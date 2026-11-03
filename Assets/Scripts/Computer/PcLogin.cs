using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MoonThemedGame.Computer
{
    // Operator login over the desktop until the first successful log in of the session.
    [DisallowMultipleComponent]
    public sealed class PcLogin : MonoBehaviour
    {
        enum Stage { Waiting, Verifying, Granted, Done }

        public string user = "operator";
        [Min(1)] public int maxPasswordLength = 12;
        public float verifyTime = 1.1f;
        public float grantedTime = 0.9f;

        static readonly Color Text = new Color(0.94f, 0.96f, 0.97f);
        static readonly Color Muted = new Color(0.58f, 0.64f, 0.67f);
        static readonly Color Good = new Color(0.55f, 1f, 0.6f);
        static readonly Color Alarm = new Color(1f, 0.38f, 0.3f);

        PcMonitorTerminal terminal;
        VisualElement overlay, cursor;
        Label password, status;
        Stage stage;
        string typed = "";
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
            if (!Accepting || char.IsControl(c) || typed.Length >= maxPasswordLength) return;
            typed += c;
            status.text = "";
        }

        void Update()
        {
            if (stage == Stage.Done || !terminal) return;
            if (overlay == null && terminal.Root != null) Build();
            if (overlay == null) return;
            overlay.BringToFront();
            cursor?.BringToFront();

            bool enter = false, backspace = false;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                enter = Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame;
                backspace = Keyboard.current.backspaceKey.wasPressedThisFrame;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            backspace = Input.GetKeyDown(KeyCode.Backspace);
            if (Accepting) foreach (char c in Input.inputString) OnText(c);
#endif
            stageTime += Time.deltaTime;
            switch (stage)
            {
                case Stage.Waiting:
                    if (Accepting && backspace && typed.Length > 0) typed = typed.Substring(0, typed.Length - 1);
                    if (Accepting && enter)
                    {
                        if (typed.Length == 0) SetStatus("PASSWORD REQUIRED", Alarm);
                        else { SetStage(Stage.Verifying); SetStatus("VERIFYING", Muted); }
                    }
                    break;
                case Stage.Verifying:
                    status.text = "VERIFYING" + new string('.', 1 + Mathf.FloorToInt(stageTime * 4) % 3);
                    if (stageTime >= verifyTime) { SetStage(Stage.Granted); SetStatus($"ACCESS GRANTED - WELCOME, {user.ToUpperInvariant()}", Good); }
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
            password.text = new string('*', typed.Length) + (caret ? "_" : " ");
        }

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
            overlay.Add(Field("USER", MakeLabel(user, 18, Text)));
            password = MakeLabel("_", 18, Text);
            overlay.Add(Field("PASSWORD", password));
            overlay.Add(Space(18));
            overlay.Add(MakeLabel("TYPE PASSWORD, PRESS [ENTER]", 13, Muted));
            overlay.Add(Space(10));
            status = MakeLabel("", 16, Muted);
            overlay.Add(status);
            desktop.Add(overlay);
        }

        static VisualElement Field(string name, Label value)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 6;
            var label = MakeLabel(name, 18, Muted);
            label.style.width = 130;
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
