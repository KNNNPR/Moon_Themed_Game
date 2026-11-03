using MoonThemedGame.Interactions;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MoonThemedGame.Computer
{
    // E on the credentials note brings the camera up to the paper; E, Esc or right click puts it down.
    [DisallowMultipleComponent, RequireComponent(typeof(NotesCredentials))]
    public sealed class NoteInspectable : Interactable
    {
        const float ViewFov = 40;

        [Min(0)] public float duration = 0.6f;
        [Range(0, 15)] public float lookAround = 5;

        NotesCredentials notes;
        Camera viewCamera;
        FirstPersonController player;
        Vector3 previousPosition, viewPosition;
        Quaternion previousRotation, viewRotation;
        float previousFov, transition, direction;
        Vector2 look;
        CursorLockMode previousLock;
        bool previousVisible, viewing;
        int closedFrame = -1;
        GUIStyle hintStyle;

        public override string Prompt => "E — Read note";
        public bool Viewing => viewing;

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
            foreach (var notes in FindObjectsByType<NotesCredentials>(FindObjectsSortMode.None))
                if (!notes.GetComponent<NoteInspectable>()) notes.gameObject.AddComponent<NoteInspectable>();
        }

        void Awake()
        {
            notes = GetComponent<NotesCredentials>();
            interactionDistance = 2.2f;
            if (GetComponentInChildren<Collider>()) return;
            // The paper has no collider of its own; a thin box lets the player's ray find it.
            var filter = GetComponent<MeshFilter>();
            var box = gameObject.AddComponent<BoxCollider>();
            if (!filter || !filter.sharedMesh) return;
            var bounds = filter.sharedMesh.bounds;
            var size = bounds.size;
            var scale = transform.lossyScale;
            for (int i = 0; i < 3; i++) size[i] = Mathf.Max(size[i], 0.01f / Mathf.Max(1e-4f, Mathf.Abs(scale[i])));
            box.center = bounds.center;
            box.size = size;
        }

        public override bool CanInteract(Camera camera) => base.CanInteract(camera) && !viewing && Time.frameCount != closedFrame;

        public override void Interact()
        {
            viewCamera = Camera.main;
            if (viewing || !viewCamera) return;
            previousPosition = viewCamera.transform.position;
            previousRotation = viewCamera.transform.rotation;
            previousFov = viewCamera.fieldOfView;
            previousLock = Cursor.lockState;
            previousVisible = Cursor.visible;
            player = viewCamera.GetComponentInParent<FirstPersonController>();
            if (!player) player = FindAnyObjectByType<FirstPersonController>();
            if (player && player.enabled) player.enabled = false; else player = null;

            ComputeView();
            look = Vector2.zero;
            viewing = true;
            direction = 1;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // Face the printed side, far enough for the whole sheet to fit the view.
        void ComputeView()
        {
            var text = notes && notes.lettering ? notes.lettering.transform : transform;
            var paper = GetComponent<Renderer>();
            var bounds = paper ? paper.bounds : new Bounds(transform.position, Vector3.one * 0.1f);
            var normal = -text.forward;
            float radius = Mathf.Max(0.02f, bounds.extents.magnitude);
            float tangent = Mathf.Tan(ViewFov * 0.5f * Mathf.Deg2Rad);
            float distance = radius / Mathf.Min(tangent, tangent * viewCamera.aspect) * 1.05f;
            distance = Mathf.Max(distance, viewCamera.nearClipPlane * 3);
            viewPosition = bounds.center + normal * distance;
            viewRotation = Quaternion.LookRotation(-normal, text.up);
        }

        void Update()
        {
            if (!viewing || !viewCamera) return;
            bool close = false;
            Vector2 mouse = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
                close = GameControls.Pressed(GameCommand.Interact) || Keyboard.current.escapeKey.wasPressedThisFrame;
            if (Mouse.current != null)
            {
                close |= Mouse.current.rightButton.wasPressedThisFrame;
                mouse = Mouse.current.delta.ReadValue() * 0.05f;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            close = Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1);
            mouse = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
            // E opened the note this frame inside PlayerInteractor's LateUpdate, after this Update.
            if (close && direction > 0 && transition > 0) direction = -1;
            if (direction > 0 && transition >= 1)
                look = Vector2.ClampMagnitude(look + new Vector2(mouse.x, -mouse.y), lookAround);
            else look = Vector2.Lerp(look, Vector2.zero, Time.deltaTime * 8);

            transition = duration > 0 ? Mathf.Clamp01(transition + direction * Time.deltaTime / duration) : (direction > 0 ? 1 : 0);
            Apply();
            if (direction < 0 && transition <= 0) Finish();
        }

        void Apply()
        {
            float t = transition * transition * (3 - 2 * transition);
            var rotation = viewRotation * Quaternion.Euler(look.y, look.x, 0);
            var position = Vector3.Lerp(previousPosition, viewPosition, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI) * 0.04f);
            viewCamera.transform.SetPositionAndRotation(position, Quaternion.Slerp(previousRotation, rotation, t));
            viewCamera.fieldOfView = Mathf.Lerp(previousFov, ViewFov, t);
        }

        void Finish()
        {
            viewCamera.transform.SetPositionAndRotation(previousPosition, previousRotation);
            viewCamera.fieldOfView = previousFov;
            Cursor.lockState = previousLock;
            Cursor.visible = previousVisible;
            if (player) player.enabled = true;
            player = null;
            viewing = false;
            direction = 0;
            closedFrame = Time.frameCount;
        }

        void OnGUI()
        {
            if (!viewing || direction <= 0 || transition < 1) return;
            if (hintStyle == null)
                hintStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15 };
            GUI.color = new Color(1, 1, 1, 0.8f);
            GUI.Label(new Rect(0, Screen.height - 48, Screen.width, 30), GameControls.Label(GameCommand.Interact)+" / ESC — Put down", hintStyle);
            GUI.color = Color.white;
        }

        protected override void OnDisable()
        {
            if (viewing && viewCamera) { transition = 0; Finish(); }
            base.OnDisable();
        }
    }
}
