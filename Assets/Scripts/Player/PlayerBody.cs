using MoonThemedGame.Interactions;
using MoonThemedGame.Scavenger;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Repair
{
    // A suited body under the first-person camera, with a tool belt: look down at a tool and press E
    // to take it, look at its empty holster while holding it and press E to put it back.
    [DisallowMultipleComponent, RequireComponent(typeof(FirstPersonController))]
    public sealed class PlayerBody : MonoBehaviour
    {
        sealed class Slot
        {
            public Transform anchor;
            public RepairCarryable tool;
            public string name;
            public Quaternion hang;
            public Vector3 offset;
            public bool Stowed => tool && tool.Holder == anchor;
        }

        const float ToolLength = 0.3f;
        // The scene is built larger than life; 1 = a 1.65 m eye height in world units.
        public static float Scale { get; private set; } = 1;
        const float FocusAngle = 13;
        static readonly Color Suit = new Color(0.8f, 0.79f, 0.74f);
        static readonly Color Dark = new Color(0.2f, 0.2f, 0.21f);
        static readonly Color Strap = new Color(0.32f, 0.27f, 0.2f);

        Camera view;
        Transform root, leftLeg, rightLeg;
        Slot[] slots = new Slot[0];
        Slot focus;
        Vector3 lastPosition;
        float stride;
        GUIStyle promptStyle;

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
            foreach (var player in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
                if (!player.GetComponent<PlayerBody>()) player.gameObject.AddComponent<PlayerBody>();
        }

        void Start()
        {
            view = Camera.main && Camera.main.transform.IsChildOf(transform) ? Camera.main : GetComponentInChildren<Camera>();
            var controller = GetComponent<CharacterController>();
            float scale = transform.lossyScale.y;
            var feet = controller
                ? transform.TransformPoint(controller.center) - Vector3.up * (controller.height * 0.5f * scale)
                : transform.position;
            float eye = view ? view.transform.position.y - feet.y : 1.7f;
            if (eye < 0.5f || eye > 12) { eye = 1.65f; if (view) feet = view.transform.position - Vector3.up * eye; }
            Scale = eye / 1.65f;

            root = new GameObject("Player Body").transform;
            root.SetPositionAndRotation(feet, Quaternion.LookRotation(Flat(transform.forward), Vector3.up));
            root.SetParent(transform, true);
            root.localScale = Vector3.one * Scale / Mathf.Max(1e-5f, scale);
            // Keeps HeldProp from giving the body pieces colliders, which would trip the controller.
            root.gameObject.AddComponent<Rigidbody>().isKinematic = true;

            Build(1.65f);
            lastPosition = transform.position;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        void Build(float eye)
        {
            float hip = eye * 0.52f, waist = eye * 0.56f, shoulder = eye * 0.83f;
            leftLeg = Joint("Left leg", new Vector3(-0.11f, hip, -0.04f));
            rightLeg = Joint("Right leg", new Vector3(0.11f, hip, -0.04f));
            foreach (var leg in new[] { leftLeg, rightLeg })
            {
                Part(PrimitiveType.Capsule, leg, new Vector3(0, -hip * 0.5f, 0), new Vector3(0.15f, hip * 0.5f, 0.15f), Suit);
                Part(PrimitiveType.Cube, leg, new Vector3(0, -hip + 0.06f, 0.06f), new Vector3(0.13f, 0.12f, 0.28f), Dark);
                Part(PrimitiveType.Cylinder, leg, new Vector3(0, -hip * 0.45f, 0.02f), new Vector3(0.16f, 0.035f, 0.16f), Dark);
            }
            float torsoHeight = shoulder - waist + 0.18f;
            Part(PrimitiveType.Capsule, root, new Vector3(0, hip + 0.06f, -0.06f), new Vector3(0.36f, 0.1f, 0.26f), Suit);
            Part(PrimitiveType.Capsule, root, new Vector3(0, (waist + shoulder) * 0.5f, -0.09f), new Vector3(0.38f, torsoHeight * 0.5f, 0.27f), Suit);
            Part(PrimitiveType.Cube, root, new Vector3(0, eye * 0.74f, 0.05f), new Vector3(0.2f, 0.14f, 0.05f), Dark);
            Part(PrimitiveType.Cylinder, root, new Vector3(0, waist, -0.07f), new Vector3(0.41f, 0.03f, 0.3f), Strap);
            foreach (float side in new[] { -1f, 1f })
            {
                var arm = Joint(side < 0 ? "Left arm" : "Right arm", new Vector3(0.25f * side, shoulder - 0.04f, -0.1f));
                arm.localRotation = Quaternion.Euler(-8, 0, 7 * side);
                Part(PrimitiveType.Capsule, arm, new Vector3(0, -0.3f, 0), new Vector3(0.12f, 0.3f, 0.12f), Suit);
                Part(PrimitiveType.Sphere, arm, new Vector3(0, -0.64f, 0.01f), new Vector3(0.12f, 0.13f, 0.12f), Dark);
            }

            var catalog = Resources.Load<ShopCatalog>("ShopCatalog");
            slots = new[]
            {
                MakeSlot("WRENCH", catalog ? catalog.wrench : null, -48, waist, true),
                MakeSlot("SCREWDRIVER", catalog ? catalog.screwdriver : null, 0, waist, false),
                MakeSlot("HAMMER", catalog ? catalog.hammer : null, 48, waist, false)
            };
        }

        Transform Joint(string name, Vector3 local)
        {
            var joint = new GameObject(name).transform;
            joint.SetParent(root, false);
            joint.localPosition = local;
            return joint;
        }

        static void Part(PrimitiveType type, Transform parent, Vector3 local, Vector3 scale, Color color)
        {
            var piece = GameObject.CreatePrimitive(type);
            if (Application.isPlaying) Destroy(piece.GetComponent<Collider>());
            else DestroyImmediate(piece.GetComponent<Collider>());
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = local;
            piece.transform.localScale = scale;
            var renderer = piece.GetComponent<Renderer>();
            var material = new Material(renderer.sharedMaterial) { color = color };
            renderer.sharedMaterial = material;
        }

        Slot MakeSlot(string name, GameObject prefab, float angle, float waist, bool wrench)
        {
            var direction = Quaternion.Euler(0, angle, 0) * Vector3.forward;
            var anchor = Joint(name + " holster", new Vector3(0, waist - 0.03f, -0.07f) + new Vector3(direction.x * 0.22f, 0, direction.z * 0.16f));
            anchor.localRotation = Quaternion.Euler(0, angle, 0);
            Part(PrimitiveType.Cube, anchor, new Vector3(0, -0.04f, 0.015f), new Vector3(0.06f, 0.09f, 0.03f), Strap);
            var slot = new Slot { anchor = anchor, name = name };
            if (!prefab) return slot;

            var instance = Instantiate(prefab);
            instance.name = prefab.name;
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                if (largest > 1e-4f) instance.transform.localScale *= ToolLength * Scale / largest;
            }
            RepairCarryable tool = wrench ? instance.AddComponent<RepairWrench>() : instance.AddComponent<RepairTool>();
            if (tool is RepairTool generic) generic.displayName = name;
            slot.tool = tool;
            // Hang head-up with the long side pointing down the leg.
            slot.hang = Quaternion.FromToRotation(tool.LongAxis(), Vector3.down);
            slot.offset = new Vector3(0, -0.1f, 0.045f);
            Stow(slot);
            return slot;
        }

        static void Stow(Slot slot) => slot.tool.Holster(slot.anchor, slot.offset, slot.hang);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() => Scale = 1;

        void Update()
        {
            if (!root) return;
            AnimateLegs();
            var previous = focus;
            focus = FindFocus();
            if (previous != focus && previous != null && previous.tool) previous.tool.SetHighlighted(false);
            if (focus != null && focus.tool) focus.tool.SetHighlighted(focus.Stowed && !RepairCarryable.Current);
            if (focus == null || !GameControls.Pressed(GameCommand.Interact)) return;
            var carried = RepairCarryable.Current;
            if (focus.Stowed && !carried && !HeldProp.Holding) focus.tool.Unholster();
            else if (!focus.Stowed && carried && carried == focus.tool) Stow(focus);
        }

        Slot FindFocus()
        {
            if (!view || !view.isActiveAndEnabled || GameSettingsMenu.BlockGameplayInput || Cursor.lockState != CursorLockMode.Locked) return null;
            var player = GetComponent<FirstPersonController>();
            if (!player || !player.enabled || view.transform.forward.y > -0.4f) return null;
            Slot best = null;
            float bestAngle = FocusAngle;
            foreach (var slot in slots)
            {
                float angle = Vector3.Angle(view.transform.forward, slot.anchor.position - view.transform.position);
                if (angle < bestAngle) { bestAngle = angle; best = slot; }
            }
            return best;
        }

        void AnimateLegs()
        {
            var delta = transform.position - lastPosition;
            delta.y = 0;
            lastPosition = transform.position;
            float speed = Time.deltaTime > 0 ? delta.magnitude / Time.deltaTime : 0;
            float amount = Mathf.Clamp01(speed / 3);
            stride += Time.deltaTime * speed * 2.8f;
            float swing = Mathf.Sin(stride) * 28 * amount;
            leftLeg.localRotation = Quaternion.Euler(swing, 0, 0);
            rightLeg.localRotation = Quaternion.Euler(-swing, 0, 0);
        }

        void OnGUI()
        {
            if (focus == null || GameSettingsMenu.IsOpen) return;
            if (promptStyle == null) promptStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, wordWrap = true };
            var carried = RepairCarryable.Current;
            string name = Loc.T(focus.name);
            string prompt = focus.Stowed
                ? (carried || HeldProp.Holding ? Loc.T("Hands full") : Loc.F("E — Take {0}", name))
                : carried && carried == focus.tool ? Loc.F("E — Put {0} back on the belt", name)
                : Loc.F("{0} holster - empty", name);
            GUI.Label(new Rect(Screen.width / 2f - 200, Screen.height / 2f + 18, 400, 50), GameControls.FormatPrompt(prompt), promptStyle);
        }
    }
}
