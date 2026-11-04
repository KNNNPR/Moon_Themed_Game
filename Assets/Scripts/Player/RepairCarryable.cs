using MoonThemedGame.Interactions;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MoonThemedGame.Repair
{
    // Something the player carries in front of the camera for the repair job: a robot part or the wrench.
    [RequireComponent(typeof(Rigidbody))]
    public abstract class RepairCarryable : Interactable
    {
        public static RepairCarryable Current { get; private set; }

        protected Rigidbody body;
        protected Camera hand;
        Collider[] colliders, playerColliders;
        Quaternion heldRotation;
        Vector3 localCenter, homePosition;
        float radius, homeFloor;

        public bool IsCarried => Current == this;
        protected abstract Vector3 HoldOffset { get; }

        protected virtual void Awake() => Init();

        protected void Init()
        {
            if (body) return;
            body = GetComponent<Rigidbody>();
            body.mass = 2;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            foreach (var mesh in GetComponentsInChildren<MeshCollider>(true)) mesh.convex = true;
            var bounds = RendererBounds();
            if (GetComponentsInChildren<Collider>(true).Length == 0)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                box.center = transform.InverseTransformPoint(bounds.center);
                var size = transform.InverseTransformVector(bounds.size);
                box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            }
            colliders = GetComponentsInChildren<Collider>(true);
            localCenter = transform.InverseTransformPoint(bounds.center);
            radius = bounds.extents.magnitude;
            interactionDistance = 4.5f;
            homePosition = transform.position;
        }

        public Bounds RendererBounds()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(transform.position, Vector3.one * 0.2f);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        // Rest on the floor if there is one, otherwise stay put where it was placed.
        public void Settle(Vector3 position, float floor)
        {
            Init();
            transform.position = position;
            bool solid = Physics.Raycast(position + Vector3.up * 0.2f, Vector3.down, 4, ~0, QueryTriggerInteraction.Ignore);
            if (!solid) transform.position += Vector3.up * (floor - RendererBounds().min.y);
            homePosition = transform.position;
            homeFloor = floor;
            body.isKinematic = !solid;
        }

        public override bool CanInteract(Camera camera) => base.CanInteract(camera) && !Current && !HeldProp.Holding;

        public override void Interact()
        {
            if (Current || !Camera.main) return;
            Current = this;
            hand = Camera.main;
            heldRotation = AlignInHand
                ? Quaternion.FromToRotation(LongAxis(), new Vector3(0.2f, 0.3f, 1).normalized)
                : Quaternion.Inverse(hand.transform.rotation) * transform.rotation;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            var player = hand.GetComponentInParent<FirstPersonController>();
            playerColliders = player ? player.GetComponentsInChildren<Collider>() : new Collider[0];
            foreach (var a in colliders) foreach (var b in playerColliders) if (a && b) Physics.IgnoreCollision(a, b, true);
            foreach (var c in colliders) if (c) c.enabled = false;
            SetHighlighted(false);
            OnPickedUp();
        }

        public Transform Holder { get; private set; }
        protected virtual bool AlignInHand => false;

        // Local axis along the tool's longest side.
        public Vector3 LongAxis()
        {
            var filter = GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh) return Vector3.forward;
            var size = filter.sharedMesh.bounds.size;
            return size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
        }

        // Stowed on the player's belt: no physics, follows the holder; localCenter is where the tool's middle goes.
        public void Holster(Transform holder, Vector3 localCenter, Quaternion localRotation)
        {
            Init();
            if (IsCarried) Current = null;
            hand = null;
            CancelInvoke();
            Holder = holder;
            body.isKinematic = true;
            // An interpolated body would keep drawing itself where the player used to be.
            body.interpolation = RigidbodyInterpolation.None;
            foreach (var c in colliders) if (c) c.enabled = false;
            transform.SetParent(holder, true);
            transform.localRotation = localRotation;
            transform.position += holder.TransformPoint(localCenter) - RendererBounds().center;
            SetHighlighted(false);
        }

        public void Unholster()
        {
            if (!Holder || Current) return;
            Holder = null;
            transform.SetParent(null, true);
            Interact();
        }

        protected virtual void OnPickedUp() { }
        protected virtual void Carry() { }
        protected virtual string CarryHint => "";

        protected static bool MouseHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
            return Input.GetMouseButton(0);
#endif
        }

        protected static bool MouseClicked()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        protected static float Scroll()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0;
#else
            return Input.mouseScrollDelta.y;
#endif
        }

        protected virtual void LateUpdate()
        {
            if (!IsCarried)
            {
                if (!body.isKinematic && transform.position.y < homePosition.y - 15) Settle(homePosition, homeFloor);
                return;
            }
            if (!hand) { Drop(false); return; }
            var player = hand.GetComponentInParent<FirstPersonController>();
            if (!hand.enabled || (player && !player.enabled)) { Drop(false); return; }
            if (GameSettingsMenu.BlockGameplayInput) return;
            if (GameControls.Pressed(GameCommand.Throw)) { Drop(true); return; }
            var offset = HoldOffset * PlayerBody.Scale;
            var desired = hand.transform.position + hand.transform.rotation * new Vector3(offset.x, offset.y, offset.z + radius * 0.6f);
            transform.rotation = hand.transform.rotation * heldRotation;
            transform.position = desired - transform.TransformVector(localCenter);
            Carry();
        }

        public void Drop(bool toss)
        {
            if (!IsCarried) return;
            Current = null;
            var direction = hand ? hand.transform.forward : Vector3.zero;
            hand = null;
            foreach (var c in colliders) if (c) c.enabled = true;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.linearVelocity = toss ? direction * 3.5f : Vector3.zero;
            Invoke(nameof(RestorePlayerCollisions), 0.6f);
        }

        // Leaves the hand without falling, e.g. a part that was bolted onto the robot.
        protected void Release()
        {
            if (IsCarried) Current = null;
            hand = null;
        }

        void RestorePlayerCollisions()
        {
            if (playerColliders == null || IsCarried) return;
            foreach (var a in colliders) foreach (var b in playerColliders) if (a && b) Physics.IgnoreCollision(a, b, false);
        }

        protected virtual void OnGUI()
        {
            if (!IsCarried || GameSettingsMenu.IsOpen || PlayerBody.BeltOpen) return;
            RepairHud.Hint(Screen.width / 2f, Screen.height / 2f + 24, CarryHint);
            RepairHud.Hint(Screen.width / 2f, Screen.height - 64, Loc.F("{0} — Drop", GameControls.Label(GameCommand.Throw)), 14);
        }

        protected override void OnDisable()
        {
            if (IsCarried) Current = null;
            base.OnDisable();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() => Current = null;
    }
}
