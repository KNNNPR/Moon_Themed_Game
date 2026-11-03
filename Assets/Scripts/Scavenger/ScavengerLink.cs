using System.Collections;
using MoonThemedGame.Computer;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cursor = UnityEngine.Cursor;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MoonThemedGame.Scavenger
{
    // Connects the PC desktop to the robot: clicking SCAVENGER (or pressing E
    // over it) switches the view to a camera on SCAV_BOT, Escape returns to the PC.
    // Driving is not implemented yet. Installs itself on the PC monitor at
    // runtime, so the Operator scene file does not need to change.
    [DisallowMultipleComponent]
    public sealed class ScavengerLink : MonoBehaviour
    {
        public const string RobotName = "SCAV_BOT";
        const string ScavengerIcon = "scavenger";

        public PcMonitorTerminal terminal;
        public Transform robot;
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
            if (!robot)
            {
                var model = GameObject.Find(RobotName);
                if (model) robot = model.transform;
            }
            if (!robot) Debug.LogWarning($"[Scavenger] No '{RobotName}' found in the scene; SCAVENGER will do nothing.", this);
            else if (!robotCamera) robotCamera = CreateCamera(robot);
        }

        // A camera behind and above the robot, parented to it so it stays put
        // relative to the model. Distance follows the model's size.
        static Camera CreateCamera(Transform robot)
        {
            var renderers = robot.GetComponentsInChildren<Renderer>();
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(robot.position, Vector3.one);
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            float size = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            var forward = robot.TransformDirection(DetectForward(robot, bounds.center));
            forward.y = 0;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;

            var host = new GameObject($"{RobotName} / Feed Camera");
            host.transform.SetParent(robot, false);
            var look = bounds.center + Vector3.up * (size * 0.3f);
            host.transform.position = bounds.center - forward * (size * 3.2f) + Vector3.up * (size * 1.3f);
            host.transform.rotation = Quaternion.LookRotation(look - host.transform.position, Vector3.up);
            var camera = host.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.fieldOfView = 65;
            camera.enabled = false;
            host.AddComponent<AudioListener>().enabled = false;
            return camera;
        }

        // The headlights sit at the front of the model; snap to the closest local axis.
        static Vector3 DetectForward(Transform robot, Vector3 center)
        {
            foreach (var child in robot.GetComponentsInChildren<Transform>())
            {
                if (child.name != "Headlights") continue;
                var lightRenderer = child.GetComponentInChildren<Renderer>();
                var local = robot.InverseTransformDirection((lightRenderer ? lightRenderer.bounds.center : child.position) - center);
                local.y = 0;
                return Mathf.Abs(local.x) > Mathf.Abs(local.z) ? new Vector3(Mathf.Sign(local.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(local.z));
            }
            return Vector3.forward;
        }

        void Update()
        {
            if (!robotCamera || !terminal) return;
            bool interact = false, click = false, escape = false;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                interact = Keyboard.current.eKey.wasPressedThisFrame;
                escape = Keyboard.current.escapeKey.wasPressedThisFrame;
            }
            if (Mouse.current != null) click = Mouse.current.leftButton.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            interact = Input.GetKeyDown(KeyCode.E);
            escape = Input.GetKeyDown(KeyCode.Escape);
            click = Input.GetMouseButtonDown(0);
#endif
            if (connected)
            {
                if (escape) Disconnect();
            }
            else if (terminal.IsFocused && terminal.HoveredIcon == ScavengerIcon && (click || interact))
            {
                Connect();
            }
        }

        public void Connect()
        {
            if (connected || !robotCamera) return;
            terminal.ExitFocus(true); // puts the operator camera back where it was
            operatorCamera = Camera.main;
            if (operatorCamera)
            {
                operatorCamera.enabled = false;
                operatorListener = operatorCamera.GetComponent<AudioListener>();
                if (operatorListener) operatorListener.enabled = false;
            }
            robotCamera.enabled = true;
            robotCamera.GetComponent<AudioListener>().enabled = true;
            previousLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            connected = true;
        }

        public void Disconnect()
        {
            if (!connected) return;
            if (robotCamera)
            {
                robotCamera.enabled = false;
                robotCamera.GetComponent<AudioListener>().enabled = false;
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
            GUI.Label(new Rect(pad, Screen.height - pad - style.fontSize * 1.5f, Screen.width, style.fontSize * 2), "ESC — BACK TO PC", style);
        }

        void OnDisable() => Disconnect();
    }
}
