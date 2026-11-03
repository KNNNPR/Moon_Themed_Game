using System.Collections;
using MoonThemedGame.Computer;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cursor = UnityEngine.Cursor;

namespace MoonThemedGame.Scavenger
{
    // Connects the PC desktop to the robot: clicking SCAVENGER (or pressing E
    // over it) switches the view to the robot's camera, Escape returns to the PC.
    // Installs itself on the PC monitor at runtime, so the Operator scene file
    // does not need to change.
    [DisallowMultipleComponent]
    public sealed class ScavengerLink : MonoBehaviour
    {
        public const string RobotName = "SCAV_BOT";
        const string ScavengerIcon = "scavenger";

        public PcMonitorTerminal terminal;
        public ScavengerRobot robot;
        public Camera robotCamera;

        Camera operatorCamera;
        AudioListener operatorListener;
        CursorLockMode previousLock;
        bool previousCursorVisible, connected;
        GUIStyle style;

        public bool Connected => connected;

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
            if (terminal && !terminal.GetComponent<ScavengerLink>()) terminal.gameObject.AddComponent<ScavengerLink>();
        }

        void Awake()
        {
            if (!terminal) terminal = GetComponent<PcMonitorTerminal>();
        }

        void Start()
        {
            if (!robot) robot = FindAnyObjectByType<ScavengerRobot>();
            if (!robot)
            {
                var model = GameObject.Find(RobotName);
                if (model) robot = model.AddComponent<ScavengerRobot>();
            }
            if (!robot) Debug.LogWarning($"[Scavenger] No '{RobotName}' found in the scene; SCAVENGER will do nothing.", this);
            else if (!robotCamera) robotCamera = CreateCamera(robot);
        }

        static Camera CreateCamera(ScavengerRobot robot)
        {
            var host = new GameObject($"{RobotName} / Feed Camera");
            SceneManager.MoveGameObjectToScene(host, robot.gameObject.scene);
            var camera = host.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.fieldOfView = 65;
            host.AddComponent<AudioListener>().enabled = false;
            // Distance follows the model's size so any scale of SCAV_BOT frames well.
            float size = Mathf.Max(robot.Bounds.extents.x, robot.Bounds.extents.y, robot.Bounds.extents.z);
            var follow = host.AddComponent<ScavengerCamera>();
            follow.target = robot;
            follow.offset = new Vector3(0, size * 1.3f, -size * 3.2f);
            follow.lookHeight = size * 0.6f;
            follow.clearance = size * 0.1f;
            follow.enabled = false;
            camera.enabled = false;
            return camera;
        }

        void Update()
        {
            if (!robot || !terminal) return;
            if (connected)
            {
                if (ScavengerInput.Exit) Disconnect();
            }
            else if (terminal.IsFocused && terminal.HoveredIcon == ScavengerIcon && (ScavengerInput.Click || ScavengerInput.Interact))
            {
                Connect();
            }
        }

        public void Connect()
        {
            if (connected || !robot || !robotCamera) return;
            terminal.ExitFocus(true); // puts the operator camera back where it was
            operatorCamera = Camera.main;
            if (operatorCamera)
            {
                operatorCamera.enabled = false;
                operatorListener = operatorCamera.GetComponent<AudioListener>();
                if (operatorListener) operatorListener.enabled = false;
            }
            var follow = robotCamera.GetComponent<ScavengerCamera>();
            follow.enabled = true;
            follow.Snap();
            robotCamera.enabled = true;
            robotCamera.GetComponent<AudioListener>().enabled = true;
            previousLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            robot.Connected = true;
            connected = true;
        }

        public void Disconnect()
        {
            if (!connected) return;
            if (robot) robot.Connected = false;
            if (robotCamera)
            {
                robotCamera.enabled = false;
                robotCamera.GetComponent<AudioListener>().enabled = false;
                robotCamera.GetComponent<ScavengerCamera>().enabled = false;
            }
            if (operatorCamera) operatorCamera.enabled = true;
            if (operatorListener) operatorListener.enabled = true;
            Cursor.lockState = previousLock;
            Cursor.visible = previousCursorVisible;
            connected = false;
            if (isActiveAndEnabled) StartCoroutine(RefocusTerminal());
        }

        // The terminal also reads Escape and would drop focus again this frame.
        IEnumerator RefocusTerminal()
        {
            yield return null;
            if (!connected) terminal.Focus(true);
        }

        void OnGUI()
        {
            if (!connected) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height / 40f) };
            style.normal.textColor = Color.white;
            float pad = Screen.height / 50f;
            GUI.Label(new Rect(pad, pad, Screen.width, style.fontSize * 2), $"{RobotName} // LIVE FEED", style);
            var focus = robot.Focus ? robot.Focus.GetPrompt(robot) : null;
            if (focus != null)
                GUI.Label(new Rect(Screen.width * 0.5f - 200, Screen.height * 0.7f, 400, style.fontSize * 2), $"[E] {focus}", style);
            GUI.Label(new Rect(pad, Screen.height - pad - style.fontSize * 1.5f, Screen.width, style.fontSize * 2), "WASD — DRIVE   E — INTERACT   ESC — BACK TO PC", style);
        }

        void OnDisable() => Disconnect();
    }
}
