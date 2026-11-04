using MoonThemedGame.Interactions;
using MoonThemedGame.Scavenger;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Repair
{
    // A suited body under the first-person camera, with a tool belt. Holding the mouse wheel freezes
    // the view and tilts it down to the belt; moving the mouse picks a tool, releasing takes it or puts it back.
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
        const float SelectStep = 140;
        const float LookDuration = 0.18f;
        static readonly Color Suit = new Color(0.8f, 0.79f, 0.74f);
        static readonly Color Dark = new Color(0.2f, 0.2f, 0.21f);
        static readonly Color Strap = new Color(0.32f, 0.27f, 0.2f);

        Camera view;
        Transform root, leftLeg, rightLeg;
        Slot[] slots = new Slot[0];
        FirstPersonController controller;
        bool open;
        int selected = 1, startIndex = 1;
        float selectX, blend, stride, messageUntil;
        Vector3 lastPosition, beltCenter;
        string message;
        GUIStyle style, selectedStyle;

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
            controller = GetComponent<FirstPersonController>();
            var capsule = GetComponent<CharacterController>();
            float scale = transform.lossyScale.y;
            var feet = capsule
                ? transform.TransformPoint(capsule.center) - Vector3.up * (capsule.height * 0.5f * scale)
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
                Part(PrimitiveType.Capsule, leg, new Vector3(0, -hip * 0.5f, 0), Quaternion.identity, new Vector3(0.15f, hip * 0.5f, 0.15f), Suit);
                Part(PrimitiveType.Capsule, leg, new Vector3(0, -hip + 0.06f, 0.06f), Quaternion.Euler(90, 0, 0), new Vector3(0.13f, 0.15f, 0.11f), Dark);
                Part(PrimitiveType.Cylinder, leg, new Vector3(0, -hip * 0.45f, 0.02f), Quaternion.identity, new Vector3(0.16f, 0.035f, 0.16f), Dark);
            }
            float torsoHeight = shoulder - waist + 0.18f;
            Part(PrimitiveType.Capsule, root, new Vector3(0, hip + 0.06f, -0.06f), Quaternion.identity, new Vector3(0.36f, 0.1f, 0.26f), Suit);
            Part(PrimitiveType.Capsule, root, new Vector3(0, (waist + shoulder) * 0.5f, -0.09f), Quaternion.identity, new Vector3(0.38f, torsoHeight * 0.5f, 0.27f), Suit);
            Part(PrimitiveType.Cylinder, root, new Vector3(0, waist, -0.07f), Quaternion.identity, new Vector3(0.41f, 0.03f, 0.3f), Strap);
            foreach (float side in new[] { -1f, 1f })
            {
                var arm = Joint(side < 0 ? "Left arm" : "Right arm", new Vector3(0.25f * side, shoulder - 0.04f, -0.1f));
                arm.localRotation = Quaternion.Euler(-8, 0, 7 * side);
                Part(PrimitiveType.Capsule, arm, new Vector3(0, -0.3f, 0), Quaternion.identity, new Vector3(0.12f, 0.3f, 0.12f), Suit);
                Part(PrimitiveType.Sphere, arm, new Vector3(0, -0.64f, 0.01f), Quaternion.identity, new Vector3(0.12f, 0.13f, 0.12f), Dark);
            }
            beltCenter = new Vector3(0, waist - 0.1f, 0.12f);

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

        static void Part(PrimitiveType type, Transform parent, Vector3 local, Quaternion rotation, Vector3 scale, Color color)
        {
            var piece = GameObject.CreatePrimitive(type);
            if (Application.isPlaying) Destroy(piece.GetComponent<Collider>());
            else DestroyImmediate(piece.GetComponent<Collider>());
            piece.transform.SetParent(parent, false);
            piece.transform.SetLocalPositionAndRotation(local, rotation);
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
            // Hang with the long side pointing down the leg.
            slot.hang = Quaternion.FromToRotation(tool.LongAxis(), Vector3.down);
            slot.offset = new Vector3(0, -0.1f, 0.045f);
            Stow(slot);
            return slot;
        }

        static void Stow(Slot slot) => slot.tool.Holster(slot.anchor, slot.offset, slot.hang);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState()
        {
            Scale = 1;
            FirstPersonController.LookFrozen = false;
        }

        bool CanOpen()
        {
            if (!view || !view.isActiveAndEnabled || GameSettingsMenu.BlockGameplayInput || Cursor.lockState != CursorLockMode.Locked) return false;
            return controller && controller.enabled;
        }

        void Update()
        {
            if (!root) return;
            AnimateLegs();
            var mouse = Mouse.current;
            if (!open && mouse != null && mouse.middleButton.wasPressedThisFrame && CanOpen()) Open();
            if (open)
            {
                if (!CanOpen() || mouse == null) Close(false);
                else
                {
                    selectX += mouse.delta.ReadValue().x;
                    int index = Mathf.Clamp(startIndex + Mathf.RoundToInt(selectX / SelectStep), 0, slots.Length - 1);
                    if (index != selected) { Highlight(selected, false); selected = index; Highlight(selected, true); }
                    if (!mouse.middleButton.isPressed) Close(true);
                }
            }
            blend = Mathf.MoveTowards(blend, open ? 1 : 0, Time.deltaTime / LookDuration);
            FirstPersonController.LookFrozen = open || blend > 0;
        }

        void LateUpdate()
        {
            if (!controller || !controller.CameraRoot || blend <= 0) return;
            float t = blend * blend * (3 - 2 * blend);
            controller.CameraRoot.localRotation = Quaternion.Euler(Mathf.Lerp(controller.Pitch, BeltPitch(), t), 0, 0);
        }

        float BeltPitch()
        {
            var offset = root.TransformPoint(beltCenter) - controller.CameraRoot.position;
            float ahead = Vector3.Dot(offset, Flat(transform.forward));
            return Mathf.Clamp(Mathf.Atan2(-offset.y, Mathf.Max(0.01f, ahead)) * Mathf.Rad2Deg, 35, 84);
        }

        void Open()
        {
            open = true;
            selectX = 0;
            startIndex = 1;
            var carried = RepairCarryable.Current;
            for (int i = 0; i < slots.Length; i++) if (carried && slots[i].tool == carried) startIndex = i;
            selected = startIndex;
            Highlight(selected, true);
        }

        void Close(bool confirm)
        {
            open = false;
            Highlight(selected, false);
            if (!confirm || selected < 0 || selected >= slots.Length) return;
            var slot = slots[selected];
            var carried = RepairCarryable.Current;
            Slot carriedSlot = null;
            foreach (var s in slots) if (carried && s.tool == carried) carriedSlot = s;
            if ((carried && carriedSlot == null) || HeldProp.Holding) { Message(Loc.T("Hands full")); return; }
            if (carriedSlot != null) Stow(carriedSlot);
            if (slot.Stowed && slot != carriedSlot) slot.tool.Unholster();
        }

        void Highlight(int index, bool on)
        {
            if (index >= 0 && index < slots.Length && slots[index].tool && slots[index].Stowed) slots[index].tool.SetHighlighted(on);
        }

        void Message(string text)
        {
            message = text;
            messageUntil = Time.time + 1.5f;
        }

        void AnimateLegs()
        {
            var delta = transform.position - lastPosition;
            delta.y = 0;
            lastPosition = transform.position;
            float speed = Time.deltaTime > 0 ? delta.magnitude / Time.deltaTime : 0;
            float amount = Mathf.Clamp01(speed / (3 * Scale));
            stride += Time.deltaTime * speed / Scale * 2.8f;
            float swing = Mathf.Sin(stride) * 28 * amount;
            leftLeg.localRotation = Quaternion.Euler(swing, 0, 0);
            rightLeg.localRotation = Quaternion.Euler(-swing, 0, 0);
        }

        void OnGUI()
        {
            if (GameSettingsMenu.IsOpen) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, wordWrap = true };
                selectedStyle = new GUIStyle(style) { fontStyle = FontStyle.Bold, fontSize = 19 };
            }
            if (message != null && Time.time < messageUntil)
                GUI.Label(new Rect(Screen.width / 2f - 200, Screen.height / 2f + 40, 400, 30), message, style);
            if (!open)
            {
                if (CanOpen() && RepairCarryable.Current)
                    GUI.Label(new Rect(16, Screen.height - 40, 360, 30), Loc.T("Hold mouse wheel — tool belt"), new GUIStyle(style) { alignment = TextAnchor.MiddleLeft, fontSize = 14 });
                return;
            }
            float width = 190, top = Screen.height * 0.72f;
            float left = Screen.width / 2f - width * slots.Length / 2f;
            var carried = RepairCarryable.Current;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                string name = Loc.T(slot.name);
                string state = slot.Stowed ? "" : carried && slot.tool == carried ? Loc.T("(in hand)") : Loc.T("(empty)");
                string label = i == selected ? $"[ {name} ]" : name;
                GUI.color = i == selected ? Color.white : new Color(1, 1, 1, 0.55f);
                GUI.Label(new Rect(left + width * i, top, width, 30), label, i == selected ? selectedStyle : style);
                if (state.Length > 0) GUI.Label(new Rect(left + width * i, top + 26, width, 24), state, style);
            }
            GUI.color = Color.white;
            var pick = slots.Length > 0 ? slots[selected] : null;
            string action = pick == null ? "" : carried && pick.tool == carried ? Loc.T("Release wheel — put back on the belt")
                : pick.Stowed ? Loc.T("Release wheel — take") : Loc.T("Release wheel — close");
            GUI.Label(new Rect(Screen.width / 2f - 300, top + 56, 600, 28), Loc.T("Move the mouse to choose") + "    " + action, style);
        }

        void OnDisable()
        {
            open = false;
            blend = 0;
            FirstPersonController.LookFrozen = false;
        }
    }
}
