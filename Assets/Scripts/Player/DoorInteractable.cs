using UnityEngine;

namespace MoonThemedGame.Interactions
{
    [DisallowMultipleComponent]
    public sealed class DoorInteractable : Interactable
    {
        public enum Motion { Slide, Rotate }
        public Transform movingPart;
        public Motion motion = Motion.Slide;
        [Tooltip("Offset in the moving part's PARENT space, not mesh space.")]
        public Vector3 openOffset = new Vector3(2f, 0f, 0f);
        public bool useWorldSlideDirection = true;
        [Tooltip("Choose left along the doorway as seen from the player's starting side.")]
        public bool slideLeftAlongDoor = true;
        public Vector3 worldSlideDirection = Vector3.left;
        [Tooltip("Zero chooses door width automatically.")]
        [Min(0f)] public float slideDistance;
        public Vector3 openEuler = new Vector3(0f, 90f, 0f);
        [Min(0.05f)] public float duration = 0.65f;
        public bool locked;

        Vector3 closedPosition;
        Quaternion closedRotation;
        float progress;
        bool targetOpen;
        GameObject interactionZone;

        public bool IsOpen => targetOpen;
        public override string Prompt => targetOpen ? "E — Close" : "E — Open";
        public override bool CanInteract(Camera camera) => base.CanInteract(camera) && !locked;

        void Awake()
        {
            if (!movingPart) movingPart = transform;
            closedPosition = movingPart.localPosition;
            closedRotation = movingPart.localRotation;
            var renderers = movingPart.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                if (useWorldSlideDirection)
                {
                    Vector3 direction = worldSlideDirection.sqrMagnitude > 0.001f
                        ? worldSlideDirection.normalized : Vector3.left;
                    if (slideLeftAlongDoor)
                    {
                        Vector3 normal = bounds.size.x < bounds.size.z ? Vector3.right : Vector3.forward;
                        var camera = Camera.main;
                        if (camera && Vector3.Dot(camera.transform.position - bounds.center, normal) < 0f)
                            normal = -normal;
                        direction = Vector3.Cross(Vector3.up, normal).normalized;
                    }
                    float distance = slideDistance > 0f ? slideDistance :
                        Vector3.Dot(bounds.size, new Vector3(Mathf.Abs(direction.x),
                            Mathf.Abs(direction.y), Mathf.Abs(direction.z))) + 0.15f;
                    Vector3 worldOffset = direction * distance;
                    openOffset = movingPart.parent
                        ? movingPart.parent.InverseTransformVector(worldOffset) : worldOffset;
                }
                interactionZone = new GameObject("Door interaction / fixed closed position");
                interactionZone.layer = gameObject.layer;
                interactionZone.transform.position = bounds.center;
                interactionZone.AddComponent<DoorInteractionZone>().door = this;
                var box = interactionZone.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = bounds.size + Vector3.one * 0.04f;

            }
        }

        public override void Interact()
        {
            if (isActiveAndEnabled && !locked) targetOpen = !targetOpen;
        }

        void OnEnable()
        {
            if (interactionZone) interactionZone.SetActive(true);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (interactionZone) interactionZone.SetActive(false);
        }

        void OnDestroy()
        {
            if (interactionZone) Destroy(interactionZone);
        }

        void Update()
        {
            progress = Mathf.MoveTowards(progress, targetOpen ? 1f : 0f,
                Time.deltaTime / Mathf.Max(0.05f, duration));
            float t = Mathf.SmoothStep(0f, 1f, progress);
            if (motion == Motion.Slide)
                movingPart.localPosition = closedPosition + openOffset * t;
            else
                movingPart.localRotation = closedRotation *
                    Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(openEuler), t);
        }
    }
}
