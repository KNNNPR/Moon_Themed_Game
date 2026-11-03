using System;
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
    public enum LinkPhase { Off, Connecting, Live, Terminating }

    [DisallowMultipleComponent]
    public sealed class ScavengerLink : MonoBehaviour
    {
        public const string RobotName = "SCAV_BOT";
        const string ScavengerIcon = "scavenger";
        const string InterferenceMaterial = "ScavengerInterference";
        static readonly int InterferenceId = Shader.PropertyToID("_Interference");
        static readonly int SignalLossId = Shader.PropertyToID("_SignalLoss");
        static readonly int CollapseId = Shader.PropertyToID("_Collapse");
        const int OverlayLayer = 31;

        public PcMonitorTerminal terminal;
        public Transform robot;
        public Camera robotCamera;
        public ScavengerRobotState state;
        public VisualStyle robotStyle = VisualStyle.ColorAscii;
        [Min(0)] public float handshakeDuration = 1.6f;
        [Min(0)] public float connectDuration = 2.4f;
        [Min(0.01f)] public float terminateDuration = 1.3f;
        public const string StationName = "Pale_Horse";
        [Tooltip("0 = new terrain every session.")]
        public int surfaceSeed;

        Camera operatorCamera;
        AudioListener operatorListener;
        Behaviour playerController;
        Renderer interference;
        Material interferenceMaterial;
        VisualStyle stationStyle;
        CursorLockMode previousLock;
        bool previousCursorVisible;
        float phaseStart;
        int sceneMask;
        CameraClearFlags sceneClear;
        ScavengerFeedHud hud;
        Collider[] robotColliders;
        Vector3 antenna, lastSignalPoint;
        float cameraSize, cameraYaw, nextBreadcrumb;
        Vector3 homePosition;
        float homeYaw;
        bool failsafe;
        Light sun;

        public LinkPhase Phase { get; private set; }
        public SatelliteNetwork Network { get; private set; }
        public UplinkStatus Uplink { get; private set; } = UplinkStatus.Ok;
        public float UplinkQuality { get; private set; } = 1;
        public bool Powered => !state || state.HasSignal;
        public LunarSurface Surface { get; private set; }
        public ScavengerDrive Drive { get; private set; }
        // Near the station the feed goes through the base antenna, not a satellite.
        public bool ViaRelay { get; private set; }
        public event Action<UplinkStatus> UplinkChanged;
        public float PhaseTime => Time.time - phaseStart;
        public bool Connected => Phase != LinkPhase.Off;

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
            if (!terminal) return;
            if (!terminal.GetComponent<ScavengerLink>()) terminal.gameObject.AddComponent<ScavengerLink>();
            if (!terminal.GetComponent<SatelliteConsoleApp>()) terminal.gameObject.AddComponent<SatelliteConsoleApp>();
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
            var bounds = RobotBounds(robot);
            cameraSize = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            if (!robotCamera) robotCamera = CreateCamera();
            sceneMask = robotCamera.cullingMask | 1 << OverlayLayer;
            sceneClear = CameraClearFlags.SolidColor;
            robotCamera.backgroundColor = Color.black;
            if (!state && !robot.TryGetComponent(out state)) state = robot.gameObject.AddComponent<ScavengerRobotState>();
            state.StatusChanged += OnStatusChanged;
            robotColliders = robot.GetComponentsInChildren<Collider>(true);
            antenna = robot.InverseTransformPoint(bounds.center + Vector3.up * (bounds.extents.y + 0.05f));
            Network = SatelliteNetwork.Instance;

            var forward = DetectForward(robot, bounds.center);
            if (!robot.TryGetComponent(out ScavengerDrive drive)) drive = robot.gameObject.AddComponent<ScavengerDrive>();
            Drive = drive;
            float unit = Mathf.Max(bounds.size.x, bounds.size.z);
            var station = StationBounds(bounds);
            var origin = new Vector3(station.center.x, bounds.min.y - 0.01f * unit, station.center.z);
            Surface = LunarSurface.Create(origin, unit, Mathf.Min(Mathf.Max(station.extents.x, station.extents.z) + 2 * unit, 30 * unit), surfaceSeed);
            Drive.Setup(Surface, forward);
            homePosition = robot.position;
            homeYaw = Quaternion.LookRotation(Drive.Heading).eulerAngles.y;
            Drive.PlaceAt(homePosition, homeYaw);
            lastSignalPoint = homePosition;
            sun = CreateSun();
            CreateInterference();
        }

        static Bounds RobotBounds(Transform robot)
        {
            var renderers = robot.GetComponentsInChildren<Renderer>();
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(robot.position, Vector3.one);
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        Camera CreateCamera()
        {
            var host = new GameObject($"{RobotName} / Feed Camera");
            SceneManager.MoveGameObjectToScene(host, robot.gameObject.scene);
            var camera = host.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 3000;
            camera.fieldOfView = 65;
            camera.enabled = false;
            camera.GetUniversalAdditionalCameraData().requiresColorOption = CameraOverrideOption.On;
            host.AddComponent<AudioListener>().enabled = false;
            return camera;
        }

        static Light CreateSun()
        {
            var sun = new GameObject("Lunar Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(28, -35, 0);
            sun.color = new Color(1f, 0.97f, 0.92f);
            sun.intensity = 0.9f;
            sun.shadows = LightShadows.Soft;
            sun.enabled = false;
            return sun;
        }

        // Chase view: follows the robot's heading with a little lag, ignores its tilt.
        void LateUpdate()
        {
            if (!Connected || !robot || !Drive) return;
            var target = Quaternion.LookRotation(Drive.Heading).eulerAngles.y;
            cameraYaw = Mathf.LerpAngle(cameraYaw, target, 1 - Mathf.Exp(-4 * Time.deltaTime));
            PlaceCamera(Mathf.Exp(-8 * Time.deltaTime));
        }

        void PlaceCamera(float keep)
        {
            var center = RobotBounds(robot).center;
            var wanted = center + Quaternion.Euler(0, cameraYaw, 0) * new Vector3(0, cameraSize * 1.2f, -cameraSize * 3);
            if (Surface) wanted.y = Mathf.Max(wanted.y, Surface.WorldHeight(wanted) + cameraSize * 0.4f);
            var t = robotCamera.transform;
            t.position = Vector3.Lerp(wanted, t.position, keep);
            t.rotation = Quaternion.LookRotation(center + Vector3.up * cameraSize * 0.3f - t.position, Vector3.up);
        }

        Bounds StationBounds(Bounds fallback)
        {
            var station = GameObject.Find(StationName);
            if (!station) return fallback;
            Bounds? result = null;
            foreach (var r in station.GetComponentsInChildren<Renderer>())
            {
                if (r.transform.IsChildOf(robot)) continue;
                if (result is Bounds b) { b.Encapsulate(r.bounds); result = b; }
                else result = r.bounds;
            }
            return result ?? fallback;
        }

        void OnStatusChanged(RobotStatus status)
        {
            if (status != RobotStatus.Online || !Drive) return;
            Drive.PlaceAt(homePosition, homeYaw);
            lastSignalPoint = homePosition;
        }

        // Under cover the robot cannot hear commands, so it backs out to where it last had signal.
        void UpdateDrive()
        {
            bool live = Phase == LinkPhase.Live && Powered;
            Drive.controllable = live && Uplink == UplinkStatus.Ok;
            if (live && Uplink == UplinkStatus.Ok && Time.time >= nextBreadcrumb)
            {
                nextBreadcrumb = Time.time + 0.5f;
                lastSignalPoint = robot.position;
            }
            bool backOut = live && Uplink == UplinkStatus.Blocked;
            if (backOut && !failsafe) hud.Notify("FAILSAFE: NO SKY - REVERSING TO LAST SIGNAL POINT");
            failsafe = backOut;
            Drive.autopilotTarget = backOut ? lastSignalPoint : null;
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

        // Full-screen quad on its own layer, so it still draws while the scene is blacked out.
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
            quad.layer = OverlayLayer;
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
            int satelliteKey = -1;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                interact = Keyboard.current.eKey.wasPressedThisFrame;
                escape = Keyboard.current.escapeKey.wasPressedThisFrame;
                if (Keyboard.current.digit1Key.wasPressedThisFrame) satelliteKey = 0;
                else if (Keyboard.current.digit2Key.wasPressedThisFrame) satelliteKey = 1;
                else if (Keyboard.current.digit3Key.wasPressedThisFrame) satelliteKey = 2;
            }
            if (Mouse.current != null) click = Mouse.current.leftButton.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            interact = Input.GetKeyDown(KeyCode.E);
            escape = Input.GetKeyDown(KeyCode.Escape);
            if (Input.GetKeyDown(KeyCode.Alpha1)) satelliteKey = 0;
            else if (Input.GetKeyDown(KeyCode.Alpha2)) satelliteKey = 1;
            else if (Input.GetKeyDown(KeyCode.Alpha3)) satelliteKey = 2;
            click = Input.GetMouseButtonDown(0);
#endif
            if (state) state.Active = Phase == LinkPhase.Live && state.Status == RobotStatus.Online;
            if (Connected) RefreshUplink();
            switch (Phase)
            {
                case LinkPhase.Off:
                    if (terminal.IsFocused && PcLogin.IsUnlocked(terminal) && terminal.HoveredIcon == ScavengerIcon && (click || interact)) Connect();
                    return;
                case LinkPhase.Connecting:
                    if (escape) Terminate();
                    else if (PhaseTime >= connectDuration) SetPhase(LinkPhase.Live);
                    break;
                case LinkPhase.Live:
                    if (escape) Terminate();
                    else if (interact && state && state.Status == RobotStatus.Offline) TryEvacuate();
                    else if (satelliteKey >= 0 && Network) Network.Select(satelliteKey);
                    break;
                case LinkPhase.Terminating:
                    if (PhaseTime >= terminateDuration) { Disconnect(); return; }
                    break;
            }
            UpdateFeed();
            if (Drive) UpdateDrive();
        }

        void SetPhase(LinkPhase phase)
        {
            Phase = phase;
            phaseStart = Time.time;
        }

        public void Terminate()
        {
            if (Phase == LinkPhase.Connecting || Phase == LinkPhase.Live) SetPhase(LinkPhase.Terminating);
        }

        void TryEvacuate()
        {
            if (state.OrderEvacuation(terminal)) hud.Notify($"EVACUATION ORDERED  -{state.evacuationCost} CR");
            else hud.Notify($"NOT ENOUGH CREDITS  ({terminal.Credits}/{state.evacuationCost} CR)");
        }

        public void RefreshUplink()
        {
            if (!robot || !Network) return;
            var offset = robot.position - (Surface ? Surface.transform.position : robot.position);
            offset.y = 0;
            bool relay = Surface && offset.magnitude < Surface.flatRadius;
            float quality = 1;
            var status = relay ? UplinkStatus.Ok : Network.Evaluate(robot.TransformPoint(antenna), robotColliders, out quality);
            UplinkQuality = quality;
            if (status == Uplink && relay == ViaRelay) return;
            Uplink = status;
            ViaRelay = relay;
            UplinkChanged?.Invoke(status);
        }

        bool HandshakeDone => Phase != LinkPhase.Connecting || PhaseTime >= handshakeDuration;
        public bool HasPicture => Powered && Uplink == UplinkStatus.Ok && HandshakeDone;

        void UpdateFeed()
        {
            float k = state ? state.Interference : 0;
            if (Uplink == UplinkStatus.Ok) k = Mathf.Max(k, Mathf.Clamp01(0.5f - UplinkQuality) * 0.8f);
            bool picture = HasPicture;
            float loss = Powered && Uplink != UplinkStatus.Ok && HandshakeDone ? 1 : 0, collapse = 0;
            if (Phase == LinkPhase.Connecting && picture)
            {
                float u = Mathf.InverseLerp(handshakeDuration, connectDuration, PhaseTime);
                loss = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.25f, 1, u));
                k = Mathf.Max(k, 1 - u);
            }
            else if (Phase == LinkPhase.Terminating)
            {
                float u = PhaseTime / terminateDuration;
                k = Mathf.Max(k, Mathf.Clamp01(u * 4));
                if (picture) loss = Mathf.Max(loss, Mathf.Sin(Mathf.Clamp01(u / 0.4f) * Mathf.PI) * (Mathf.Repeat(Time.time * 9, 1) < 0.5f ? 0.9f : 0.4f));
                collapse = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.4f, 1, u));
            }
            robotCamera.cullingMask = picture ? sceneMask : 1 << OverlayLayer;
            robotCamera.clearFlags = picture ? sceneClear : CameraClearFlags.SolidColor;
            if (!interferenceMaterial) return;
            interference.enabled = Connected && (k > 0 || loss > 0 || collapse > 0);
            interferenceMaterial.SetFloat(InterferenceId, picture ? k : 0);
            interferenceMaterial.SetFloat(SignalLossId, loss);
            interferenceMaterial.SetFloat(CollapseId, collapse);
        }

        public void Connect()
        {
            if (Connected || !robotCamera) return;
            terminal.ExitFocus(true);
            operatorCamera = Camera.main;
            if (operatorCamera)
            {
                operatorCamera.enabled = false;
                operatorListener = operatorCamera.GetComponent<AudioListener>();
                if (operatorListener) operatorListener.enabled = false;
                var controller = operatorCamera.GetComponentInParent<FirstPersonController>();
                if (!controller) controller = FindAnyObjectByType<FirstPersonController>();
                playerController = controller && controller.enabled ? controller : null;
                if (playerController) playerController.enabled = false;
            }
            if (sun) sun.enabled = true;
            if (Drive) cameraYaw = Quaternion.LookRotation(Drive.Heading).eulerAngles.y;
            PlaceCamera(0);
            robotCamera.enabled = true;
            robotCamera.GetComponent<AudioListener>().enabled = true;
            stationStyle = VisualStyleSwitcher.CurrentStyle;
            VisualStyleSwitcher.SetStyle(robotStyle);
            previousLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            SetPhase(LinkPhase.Connecting);
            RefreshUplink();
            hud.OnConnected();
            UpdateFeed();
        }

        public void Disconnect()
        {
            if (!Connected) return;
            if (robotCamera)
            {
                robotCamera.enabled = false;
                robotCamera.GetComponent<AudioListener>().enabled = false;
            }
            if (interference) interference.enabled = false;
            if (sun) sun.enabled = false;
            if (Drive)
            {
                Drive.controllable = false;
                Drive.autopilotTarget = null;
            }
            VisualStyleSwitcher.SetStyle(stationStyle);
            if (operatorCamera) operatorCamera.enabled = true;
            if (operatorListener) operatorListener.enabled = true;
            Cursor.lockState = previousLock;
            Cursor.visible = previousCursorVisible;
            if (playerController) playerController.enabled = true;
            playerController = null;
            Phase = LinkPhase.Off;
            if (state) state.Active = false;
            if (isActiveAndEnabled) StartCoroutine(RefocusTerminal());
        }

        // The terminal also reads Escape and would drop focus again this frame.
        IEnumerator RefocusTerminal()
        {
            yield return null;
            if (!Connected) terminal.Focus(true);
        }

        void OnDisable() => Disconnect();
    }
}
