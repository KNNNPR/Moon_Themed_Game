using MoonThemedGame.Interactions;
using MoonThemedGame.Scavenger;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using static MoonThemedGame.Repair.SuitMesh;

namespace MoonThemedGame.Repair
{
    // A faceted, cel-shaded suit under the first-person camera. The arms reach for whatever is carried.
    // Holding the mouse wheel freezes the view, the player brings the toolbox up and opens it; moving
    // the mouse picks a tool, releasing the wheel takes it (or puts back the one that was in hand).
    [DisallowMultipleComponent, RequireComponent(typeof(FirstPersonController))]
    public sealed class PlayerBody : MonoBehaviour
    {
        sealed class Slot
        {
            public Transform anchor;
            public RepairCarryable tool;
            public string name;
            public Quaternion hang, restRotation, fromRotation;
            public Vector3 offset, restPosition, fromPosition;
            public float lift, blend = 1;
            public bool Stowed => tool && tool.Holder == anchor;
        }

        sealed class Arm
        {
            public Transform shoulder, elbow;
            public float side;
            public Vector3 hand;
            public bool placed;
        }

        const float Eye = 1.65f;
        const float ToolLength = 0.3f;
        const float UpperArm = 0.31f, Forearm = 0.35f;
        const float SelectStep = 110;
        const float CaseWidth = 0.46f, ModelWidth = 0.568f, Tilt = 48, LidOpen = 138;
        static readonly Color Suit = new Color(0.86f, 0.84f, 0.78f);
        static readonly Color Dark = new Color(0.22f, 0.21f, 0.2f);
        static readonly Color Strap = new Color(0.36f, 0.27f, 0.17f);
        static readonly Color Accent = new Color(0.95f, 0.55f, 0.16f);

        // The scene is built larger than life; 1 = a 1.65 m eye height in world units.
        public static float Scale { get; private set; } = 1;
        public static bool BeltOpen { get; private set; }

        Camera view;
        FirstPersonController controller;
        Transform root, leftLeg, rightLeg;
        Arm left, right;
        Slot[] slots = new Slot[0];
        Slot returned;
        Transform kit, lid;
        Quaternion lidShut;
        Vector3 lidAxis;
        float unit, kitShow, lidSign = 1;
        bool kitVisible = true;
        Material baseMaterial;
        bool open;
        int selected = 1, startIndex = 1;
        float selectX, stride, walk, messageUntil;
        Vector3 lastPosition;
        string message;

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState()
        {
            Scale = 1;
            BeltOpen = false;
            FirstPersonController.LookFrozen = false;
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
            float eye = view ? view.transform.position.y - feet.y : Eye;
            if (eye < 0.5f || eye > 12) { eye = Eye; if (view) feet = view.transform.position - Vector3.up * eye; }
            Scale = eye / Eye;

            root = new GameObject("Player Body").transform;
            root.SetPositionAndRotation(feet, Quaternion.LookRotation(Flat(transform.forward), Vector3.up));
            root.SetParent(transform, true);
            root.localScale = Vector3.one * Scale / Mathf.Max(1e-5f, scale);
            // Keeps HeldProp from giving the body pieces colliders, which would trip the controller.
            root.gameObject.AddComponent<Rigidbody>().isKinematic = true;

            var catalog = Resources.Load<ShopCatalog>("ShopCatalog");
            baseMaterial = catalog ? catalog.suitMaterial : null;
            Build(catalog);
            lastPosition = transform.position;
            Application.onBeforeRender -= PoseArms;
            Application.onBeforeRender += PoseArms;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        void Build(ShopCatalog catalog)
        {
            const float hip = 0.86f;
            Piece("Torso", root, Suit, 8,
                new Ring(0.8f, 0.15f, 0.11f, 0, -0.08f), new Ring(0.92f, 0.165f, 0.12f, 0, -0.08f),
                new Ring(1.06f, 0.18f, 0.13f, 0, -0.085f), new Ring(1.22f, 0.205f, 0.14f, 0, -0.09f),
                new Ring(1.34f, 0.2f, 0.13f, 0, -0.09f), new Ring(1.41f, 0.12f, 0.1f, 0, -0.09f),
                new Ring(1.45f, 0.085f, 0.08f, 0, -0.08f));
            Piece("Chest unit", root, Dark, 4,
                new Ring(1.1f, 0.09f, 0.025f, 0, 0.055f), new Ring(1.24f, 0.095f, 0.028f, 0, 0.065f));
            Piece("Belt", root, Strap, 8,
                new Ring(0.895f, 0.172f, 0.127f, 0, -0.08f), new Ring(0.955f, 0.176f, 0.13f, 0, -0.08f));
            Piece("Belt buckle", root, Accent, 4,
                new Ring(0.9f, 0.035f, 0.012f, 0, 0.052f), new Ring(0.95f, 0.035f, 0.012f, 0, 0.052f));

            leftLeg = Joint("Left leg", root, new Vector3(-0.095f, hip, -0.07f));
            rightLeg = Joint("Right leg", root, new Vector3(0.095f, hip, -0.07f));
            foreach (var leg in new[] { leftLeg, rightLeg })
            {
                Piece("Leg", leg, Suit, 7,
                    new Ring(0.02f, 0.09f, 0.09f), new Ring(-0.4f, 0.07f, 0.072f), new Ring(-0.45f, 0.072f, 0.078f, 0, 0.008f),
                    new Ring(-0.5f, 0.066f, 0.07f), new Ring(-0.76f, 0.056f, 0.06f));
                Piece("Boot", leg, Dark, 6,
                    new Ring(-0.86f, 0.068f, 0.135f, 0, 0.045f), new Ring(-0.8f, 0.07f, 0.13f, 0, 0.04f),
                    new Ring(-0.74f, 0.062f, 0.075f, 0, 0.005f), new Ring(-0.7f, 0.058f, 0.062f));
            }

            left = MakeArm(-1);
            right = MakeArm(1);

            BuildKit(catalog);
            slots = new[]
            {
                MakeSlot("WRENCH", catalog ? catalog.wrench : null, -1, Fastener.Bolt),
                MakeSlot("SCREWDRIVER", catalog ? catalog.screwdriver : null, 0, Fastener.Screw),
                MakeSlot("HAMMER", catalog ? catalog.hammer : null, 1, null)
            };
            ShowKit(false);
        }

        Arm MakeArm(float side)
        {
            var shoulder = Joint(side < 0 ? "Left arm" : "Right arm", root, new Vector3(0.215f * side, 1.33f, -0.09f));
            Piece("Shoulder", shoulder, Suit, 7,
                new Ring(0.05f, 0.04f, 0.04f), new Ring(0.0f, 0.078f, 0.078f), new Ring(-0.06f, 0.074f, 0.074f));
            Piece("Upper arm", shoulder, Suit, 7,
                new Ring(-0.02f, 0.064f, 0.064f), new Ring(-UpperArm, 0.054f, 0.054f));
            var elbow = Joint("Elbow", shoulder, new Vector3(0, -UpperArm, 0));
            Piece("Forearm", elbow, Suit, 7,
                new Ring(0.01f, 0.056f, 0.056f), new Ring(-0.22f, 0.05f, 0.05f));
            Piece("Cuff", elbow, Accent, 7,
                new Ring(-0.2f, 0.056f, 0.056f), new Ring(-0.25f, 0.056f, 0.056f));
            Piece("Glove", elbow, Dark, 6,
                new Ring(-0.245f, 0.05f, 0.044f), new Ring(-0.3f, 0.052f, 0.036f, 0, 0.006f),
                new Ring(-0.35f, 0.04f, 0.026f, 0, 0.01f), new Ring(-0.37f, 0.018f, 0.014f, 0, 0.008f));
            return new Arm { shoulder = shoulder, elbow = elbow, side = side };
        }

        static Transform Joint(string name, Transform parent, Vector3 local)
        {
            var joint = new GameObject(name).transform;
            joint.SetParent(parent, false);
            joint.localPosition = local;
            return joint;
        }

        void Piece(string name, Transform parent, Color color, int sides, params Ring[] rings)
        {
            var piece = new GameObject(name);
            piece.transform.SetParent(parent, false);
            piece.AddComponent<MeshFilter>().sharedMesh = Lathe(name, sides, rings);
            piece.AddComponent<MeshRenderer>().sharedMaterial = Paint(color);
        }

        Material Paint(Color color)
        {
            Material material;
            if (baseMaterial) material = new Material(baseMaterial);
            else
            {
                var shader = Shader.Find("Old Tech/Retro Cel Lit");
                material = new Material(shader ? shader : Shader.Find("Universal Render Pipeline/Lit"));
            }
            material.name = "Suit";
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", null);
            material.color = color;
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0);
            return material;
        }

        // The case is posed through a pivot whose axes are x across, y up, z away from the viewer,
        // with its origin at the middle of the bottom.
        void BuildKit(ShopCatalog catalog)
        {
            unit = CaseWidth * Scale / ModelWidth;
            kit = new GameObject("Toolbox").transform;
            if (catalog && catalog.toolbox)
            {
                var model = Instantiate(catalog.toolbox);
                model.name = "Toolbox model";
                foreach (var t in model.GetComponentsInChildren<Transform>()) if (t.name == "Lid") lid = t;
                var back = lid ? lid.position - model.transform.position : Vector3.forward;
                back.y = 0;
                back = back.sqrMagnitude > 1e-8f ? back.normalized : Vector3.forward;
                var bounds = Bounds(model);
                float width = Mathf.Abs(Vector3.Dot(bounds.size, Abs(Vector3.Cross(Vector3.up, back))));
                if (width > 1e-5f) model.transform.localScale *= CaseWidth * Scale / width;
                bounds = Bounds(model);
                kit.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), Quaternion.LookRotation(back, Vector3.up));
                model.transform.SetParent(kit, true);
                foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = Paint(materials[i] ? materials[i].color : Color.gray);
                    renderer.sharedMaterials = materials;
                }
                if (lid)
                {
                    lidShut = lid.localRotation;
                    lidAxis = lid.parent.InverseTransformDirection(kit.right).normalized;
                    float before = Bounds(lid.gameObject).center.y;
                    lid.localRotation = Quaternion.AngleAxis(30, lidAxis) * lidShut;
                    if (Bounds(lid.gameObject).center.y < before) lidSign = -1;
                    lid.localRotation = lidShut;
                }
            }
            kit.SetParent(root, true);
        }

        static Bounds Bounds(GameObject target)
        {
            var renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(target.transform.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        // Row -1 is the cradle nearest the viewer, 1 the farthest.
        Slot MakeSlot(string name, GameObject prefab, int row, Fastener? drives)
        {
            var anchor = new GameObject(name + " cradle").transform;
            anchor.SetParent(kit, false);
            anchor.localPosition = new Vector3(0, 0.115f, row * 0.075f) * unit;
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
            RepairCarryable tool;
            if (drives.HasValue)
            {
                var driver = instance.AddComponent<RepairWrench>();
                driver.drives = drives.Value;
                driver.displayName = name;
                tool = driver;
            }
            else tool = instance.AddComponent<RepairTool>();
            slot.tool = tool;
            slot.hang = LieFlat(tool);
            Stow(slot);
            return slot;
        }

        // Long side across the case, thinnest side up.
        static Quaternion LieFlat(RepairCarryable tool)
        {
            var length = tool.LongAxis();
            var filter = tool.GetComponent<MeshFilter>();
            var size = filter && filter.sharedMesh ? filter.sharedMesh.bounds.size : Vector3.one;
            Vector3 thin = Vector3.zero;
            float smallest = float.MaxValue;
            foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
            {
                float extent = Vector3.Dot(size, axis);
                if (axis != length && extent < smallest) { smallest = extent; thin = axis; }
            }
            return Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(length, thin), thin));
        }

        void Stow(Slot slot)
        {
            slot.fromPosition = slot.tool.transform.position;
            slot.fromRotation = slot.tool.transform.rotation;
            slot.tool.Holster(slot.anchor, slot.offset, slot.hang);
            slot.restPosition = slot.tool.transform.localPosition;
            slot.restRotation = slot.tool.transform.localRotation;
        }

        void ShowKit(bool visible)
        {
            if (!kit || kitVisible == visible) return;
            kitVisible = visible;
            foreach (var renderer in kit.GetComponentsInChildren<Renderer>(true)) renderer.enabled = visible;
        }

        bool CanOpen()
        {
            if (!view || !view.isActiveAndEnabled || GameSettingsMenu.BlockGameplayInput || Cursor.lockState != CursorLockMode.Locked) return false;
            return controller && controller.enabled;
        }

        bool Available(Slot slot) => slot.tool && slot.Stowed;

        Slot SlotOf(RepairCarryable tool)
        {
            foreach (var slot in slots) if (tool && slot.tool == tool) return slot;
            return null;
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
                    selectX += mouse.delta.ReadValue().y;
                    int count = 0;
                    foreach (var slot in slots) if (Available(slot)) count++;
                    int position = Mathf.Clamp(startIndex + Mathf.RoundToInt(selectX / SelectStep), 0, Mathf.Max(0, count - 1));
                    int index = SlotAt(position);
                    if (index != selected) { Highlight(selected, false); selected = index; Highlight(selected, true); }
                    if (!mouse.middleButton.isPressed) Close(true);
                }
            }
            BeltOpen = open;
            FirstPersonController.LookFrozen = open;
        }

        // Index into slots of the n-th tool that is on the belt or in hand.
        int SlotAt(int position)
        {
            for (int i = 0, n = 0; i < slots.Length; i++)
                if (Available(slots[i]) && n++ == position) return i;
            return -1;
        }

        int PositionOf(int index)
        {
            for (int i = 0, n = 0; i < slots.Length; i++)
                if (Available(slots[i])) { if (i == index) return n; n++; }
            return 0;
        }

        static float Ease(float t) => t * t * (3 - 2 * t);

        // The case rises from the hip into the hands, then the lid swings open; in reverse when put away.
        void PoseKit()
        {
            if (!kit) return;
            kitShow = Mathf.MoveTowards(kitShow, open ? 1 : 0, Time.deltaTime / (open ? 0.42f : 0.34f));
            ShowKit(kitShow > 0.001f);
            if (!kitVisible) return;
            var camera = view.transform;
            float tilt = Tilt * Mathf.Deg2Rad;
            var up = camera.up * Mathf.Cos(tilt) - camera.forward * Mathf.Sin(tilt);
            var back = camera.forward * Mathf.Cos(tilt) + camera.up * Mathf.Sin(tilt);
            var heldRotation = Quaternion.LookRotation(back, up);
            var heldPosition = camera.position + camera.rotation * (new Vector3(0, -0.2f, 0.48f) * Scale) - up * (0.09f * unit);
            var hipRotation = root.rotation * Quaternion.Euler(0, 90, 0);
            var hipPosition = root.TransformPoint(new Vector3(-0.3f, 0.55f, 0.1f));
            float move = Ease(Mathf.Clamp01(kitShow / 0.6f));
            kit.SetPositionAndRotation(Vector3.Lerp(hipPosition, heldPosition, move), Quaternion.Slerp(hipRotation, heldRotation, move));
            if (lid) lid.localRotation = Quaternion.AngleAxis(lidSign * LidOpen * Ease(Mathf.Clamp01((kitShow - 0.45f) / 0.55f)), lidAxis) * lidShut;
        }

        // Tools rest in their cradles; the chosen one lifts out a little, a tool just put back glides in.
        void PoseTools()
        {
            var chosen = open && selected >= 0 && selected < slots.Length ? slots[selected] : null;
            foreach (var slot in slots)
            {
                if (!slot.tool || !slot.Stowed) continue;
                slot.lift = Mathf.MoveTowards(slot.lift, slot == chosen ? 1 : 0, Time.deltaTime / 0.14f);
                var raised = slot.restPosition + new Vector3(0, 0.06f, -0.05f) * (Ease(slot.lift) * Scale);
                var tool = slot.tool.transform;
                tool.SetLocalPositionAndRotation(raised, slot.restRotation);
                if (slot.blend < 1)
                {
                    slot.blend = Mathf.MoveTowards(slot.blend, 1, Time.deltaTime / 0.25f);
                    float t = Ease(slot.blend);
                    tool.SetPositionAndRotation(Vector3.Lerp(slot.fromPosition, tool.position, t), Quaternion.Slerp(slot.fromRotation, tool.rotation, t));
                }
            }
        }

        // Runs after every LateUpdate, so the hands land on the carried item where it is drawn.
        void PoseArms()
        {
            if (!root || left == null || !left.shoulder) return;
            if (view) { PoseKit(); PoseTools(); }
            var carried = RepairCarryable.Current;
            Vector3? leftTarget = null, rightTarget = null;
            if (open && selected >= 0 && selected < slots.Length && slots[selected].tool && slots[selected].Stowed)
                rightTarget = Grip(slots[selected].tool, slots[selected].tool.RendererBounds());
            else if (carried && view)
            {
                var bounds = carried.RendererBounds();
                if (carried is RepairPartProp)
                {
                    float reach = Mathf.Abs(Vector3.Dot(bounds.extents, Abs(view.transform.right))) * 0.85f;
                    var below = -view.transform.up * (bounds.extents.y * 0.3f);
                    leftTarget = bounds.center - view.transform.right * reach + below;
                    rightTarget = bounds.center + view.transform.right * reach + below;
                }
                else rightTarget = Grip(carried, bounds);
            }
            // The off hand holds the case by its near left corner.
            if (kitVisible && kit) leftTarget = kit.TransformPoint(new Vector3(-0.24f, 0.06f, -0.1f) * unit);
            Solve(left, leftTarget);
            Solve(right, rightTarget);
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        Vector3 Grip(RepairCarryable tool, Bounds bounds)
        {
            var axis = tool.transform.TransformDirection(tool.LongAxis()).normalized;
            float half = Mathf.Abs(Vector3.Dot(bounds.extents, Abs(axis)));
            if (Vector3.Dot(axis, view.transform.position - bounds.center) < 0) axis = -axis;
            return bounds.center + axis * half * 0.55f;
        }

        // Two-bone IK: shoulder to elbow to hand, elbow bending out and down.
        void Solve(Arm arm, Vector3? target)
        {
            float unit = root.lossyScale.y;
            var shoulder = arm.shoulder.position;
            var up = root.up;
            var forward = root.forward;
            var side = root.right * arm.side;
            float swing = Mathf.Sin(stride + (arm.side > 0 ? 0 : Mathf.PI)) * 0.09f * walk;
            var goal = target ?? shoulder + (-up * 0.6f + side * 0.035f + forward * (0.05f + swing)) * unit;
            float smooth = 1 - Mathf.Exp(-Time.deltaTime * (target.HasValue ? 22 : 10));
            arm.hand = arm.placed ? Vector3.Lerp(arm.hand, goal, smooth) : goal;
            arm.placed = true;

            float l1 = UpperArm * unit, l2 = Forearm * unit;
            var reach = arm.hand - shoulder;
            float distance = Mathf.Clamp(reach.magnitude, 0.05f * unit, (l1 + l2) * 0.999f);
            var direction = reach.sqrMagnitude > 1e-8f ? reach.normalized : -up;
            var pole = (side * 0.6f - up * 0.5f - forward * 0.35f).normalized;
            var bendAxis = Vector3.Cross(direction, pole);
            if (bendAxis.sqrMagnitude < 1e-6f) bendAxis = side;
            float cos = Mathf.Clamp((l1 * l1 + distance * distance - l2 * l2) / (2 * l1 * distance), -1, 1);
            var elbow = shoulder + Quaternion.AngleAxis(Mathf.Acos(cos) * Mathf.Rad2Deg, bendAxis.normalized) * direction * l1;
            var hand = shoulder + direction * distance;
            arm.shoulder.rotation = Quaternion.FromToRotation(root.rotation * Vector3.down, elbow - shoulder) * root.rotation;
            arm.elbow.rotation = Quaternion.FromToRotation(root.rotation * Vector3.down, hand - elbow) * root.rotation;
        }

        void Open()
        {
            var carried = RepairCarryable.Current;
            var carriedSlot = SlotOf(carried);
            if ((carried && carriedSlot == null) || HeldProp.Holding) { Message(Loc.T("Hands full")); return; }
            if (carriedSlot != null) { Stow(carriedSlot); carriedSlot.blend = 0; }
            if (SlotAt(0) < 0) return;
            open = true;
            returned = carriedSlot;
            selectX = 0;
            int count = 0;
            foreach (var slot in slots) if (Available(slot)) count++;
            startIndex = returned != null ? PositionOf(System.Array.IndexOf(slots, returned)) : (count - 1) / 2;
            selected = SlotAt(startIndex);
            Highlight(selected, true);
        }

        void Close(bool confirm)
        {
            open = false;
            BeltOpen = false;
            FirstPersonController.LookFrozen = false;
            Highlight(selected, false);
            var back = returned;
            returned = null;
            if (!confirm) { if (back != null) Take(back); return; }
            if (selected >= 0 && selected < slots.Length && slots[selected] != back) Take(slots[selected]);
        }

        void Take(Slot slot)
        {
            if (!slot.tool || !slot.Stowed) return;
            slot.tool.Unholster();
            foreach (var renderer in slot.tool.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
        }

        void Highlight(int index, bool on)
        {
            if (index >= 0 && index < slots.Length && slots[index].tool) slots[index].tool.SetHighlighted(on);
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
            float speed = Time.deltaTime > 0 ? delta.magnitude / Time.deltaTime / Scale : 0;
            walk = Mathf.MoveTowards(walk, Mathf.Clamp01(speed / 3), Time.deltaTime * 4);
            stride += Time.deltaTime * speed * 2.8f;
            float swing = Mathf.Sin(stride) * 26 * walk;
            leftLeg.localRotation = Quaternion.Euler(swing, 0, 0);
            rightLeg.localRotation = Quaternion.Euler(-swing, 0, 0);
        }

        void OnGUI()
        {
            if (GameSettingsMenu.IsOpen || open) return;
            if (message != null && Time.time < messageUntil)
                RepairHud.Hint(Screen.width / 2f, Screen.height / 2f + 60, message);
            if (CanOpen() && RepairCarryable.Current && !(RepairCarryable.Current is RepairPartProp))
                RepairHud.Corner(Loc.T("Hold mouse wheel — toolbox"));
        }

        void OnEnable()
        {
            if (!root) return;
            Application.onBeforeRender -= PoseArms;
            Application.onBeforeRender += PoseArms;
        }

        void OnDisable()
        {
            open = false;
            BeltOpen = false;
            FirstPersonController.LookFrozen = false;
            Application.onBeforeRender -= PoseArms;
        }
    }
}
