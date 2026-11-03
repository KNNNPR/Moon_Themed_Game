using System.Collections;
using MoonThemedGame.Computer;
using MoonThemedGame.Rendering;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Cursor = UnityEngine.Cursor;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MoonThemedGame.Scavenger
{
    // Clicking SCAVENGER (or E over it) on the PC switches to the SCAV_BOT camera and robotStyle,
    // Escape returns to the PC. Installs itself at runtime, so Operator.unity stays untouched.
    [DisallowMultipleComponent]
    public sealed class ScavengerLink : MonoBehaviour
    {
        public const string RobotName = "SCAV_BOT";
        const string ScavengerIcon = "scavenger";
        const string InterferenceMaterial = "ScavengerInterference";
        static readonly int InterferenceId = Shader.PropertyToID("_Interference");
        static readonly int SignalLossId = Shader.PropertyToID("_SignalLoss");

        public PcMonitorTerminal terminal;
        public Transform robot;
        public Camera robotCamera;
        public ScavengerRobotState state;
        public VisualStyle robotStyle = VisualStyle.ColorAscii;

        Camera operatorCamera;
        AudioListener operatorListener;
        Renderer interference;
        Material interferenceMaterial;
        VisualStyle stationStyle;
        CursorLockMode previousLock;
        bool previousCursorVisible, connected;
        float signalLoss;
        ScavengerFeedHud hud;

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
            if (!TryGetComponent(out hud)) hud = gameObject.AddComponent<ScavengerFeedHud>();
        }

        void Start()
        {
            if (!robot)
            {
                var model = GameObject.Find(RobotName);
                if (model) robot = model.transform;
            }
            if (!robot)
            {
                Debug.LogWarning($"[Scavenger] No '{RobotName}' found in the scene; SCAVENGER will do nothing.", this);
                return;
            }
            if (!robotCamera) robotCamera = CreateCamera(robot);
            if (!state && !robot.TryGetComponent(out state)) state = robot.gameObject.AddComponent<ScavengerRobotState>();
            CreateInterference();
        }

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
            camera.GetUniversalAdditionalCameraData().requiresColorOption = CameraOverrideOption.On;
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

        // Full-screen quad, drawn only while connected and the signal is failing.
        void CreateInterference()
        {
            var source = Resources.Load<Material>(InterferenceMaterial);
            var shader = source ? source.shader : Shader.Find("Moon Game/Scavenger Interference");
            if (!shader)
            {
                Debug.LogWarning("[Scavenger] Interference shader is missing.", this);
                return;
            }
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"{RobotName} / Interference";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(robotCamera.transform, false);
            quad.transform.localPosition = Vector3.forward * (robotCamera.nearClipPlane + 0.05f);
            quad.transform.localScale = Vector3.one * 0.05f;
            interferenceMaterial = source ? new Material(source) : new Material(shader);
            interference = quad.GetComponent<Renderer>();
            interference.sharedMaterial = interferenceMaterial;
            interference.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            interference.receiveShadows = false;
            interference.enabled = false;
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
            if (state) state.Active = connected && state.Status == RobotStatus.Online;
            if (connected)
            {
                if (escape) Disconnect();
                else if (interact && state && state.Status == RobotStatus.Offline) TryEvacuate();
                UpdateInterference();
            }
            else if (terminal.IsFocused && terminal.HoveredIcon == ScavengerIcon && (click || interact))
            {
                Connect();
            }
        }

        void TryEvacuate()
        {
            if (state.OrderEvacuation(terminal)) hud.Notify($"EVACUATION ORDERED  -{state.evacuationCost} CR");
            else hud.Notify($"NOT ENOUGH CREDITS  ({terminal.Credits}/{state.evacuationCost} CR)");
        }

        void UpdateInterference()
        {
            if (!interferenceMaterial || !state) return;
            signalLoss = Mathf.MoveTowards(signalLoss, state.HasSignal ? 0 : 1, Time.deltaTime * 3);
            float amount = state.Interference;
            interference.enabled = connected && (amount > 0 || signalLoss > 0);
            interferenceMaterial.SetFloat(InterferenceId, amount);
            interferenceMaterial.SetFloat(SignalLossId, signalLoss);
        }

        public void Connect()
        {
            if (connected || !robotCamera) return;
            terminal.ExitFocus(true);
            operatorCamera = Camera.main;
            if (operatorCamera)
            {
                operatorCamera.enabled = false;
                operatorListener = operatorCamera.GetComponent<AudioListener>();
                if (operatorListener) operatorListener.enabled = false;
            }
            robotCamera.enabled = true;
            robotCamera.GetComponent<AudioListener>().enabled = true;
            stationStyle = VisualStyleSwitcher.CurrentStyle;
            VisualStyleSwitcher.SetStyle(robotStyle);
            signalLoss = state && !state.HasSignal ? 1 : 0;
            previousLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            connected = true;
            hud.OnConnected();
            UpdateInterference();
        }

        public void Disconnect()
        {
            if (!connected) return;
            if (robotCamera)
            {
                robotCamera.enabled = false;
                robotCamera.GetComponent<AudioListener>().enabled = false;
            }
            if (interference) interference.enabled = false;
            VisualStyleSwitcher.SetStyle(stationStyle);
            if (operatorCamera) operatorCamera.enabled = true;
            if (operatorListener) operatorListener.enabled = true;
            Cursor.lockState = previousLock;
            Cursor.visible = previousCursorVisible;
            connected = false;
            if (state) state.Active = false;
            if (isActiveAndEnabled) StartCoroutine(RefocusTerminal());
        }

        // The terminal also reads Escape and would drop focus again this frame.
        IEnumerator RefocusTerminal()
        {
            yield return null;
            if (!connected) terminal.Focus(true);
        }

        void OnDisable() => Disconnect();
    }
}
