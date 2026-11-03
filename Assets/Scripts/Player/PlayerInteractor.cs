using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MoonThemedGame.Interactions
{
    [DisallowMultipleComponent, RequireComponent(typeof(FirstPersonController))]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        public Camera viewCamera;
        [Min(0.1f)] public float maxDistance = 5f;
        [Tooltip("Include walls and other obstacles, not just interactable objects.")]
        public LayerMask raycastLayers = Physics.DefaultRaycastLayers;
        [Min(1f)] public float dotSize = 6f;
        public bool showPrompt = true;

        static readonly HashSet<PlayerInteractor> active = new HashSet<PlayerInteractor>();
        FirstPersonController movement;
        Interactable target;
        Texture2D dot;
        GUIStyle promptStyle;

        public static bool Manages(Camera camera)
        {
            foreach (var interactor in active)
                if (interactor && interactor.viewCamera == camera) return true;
            return false;
        }

        void Awake()
        {
            movement = GetComponent<FirstPersonController>();
            if (!viewCamera) viewCamera = GetComponentInChildren<Camera>();
        }

        void OnEnable() => active.Add(this);

        void LateUpdate()
        {
            SetTarget(FindTarget());
            if (target && Keyboard.current != null &&
                Keyboard.current.eKey.wasPressedThisFrame)
            {
                target.Interact();
                if (!CanUseInput()) SetTarget(null);
            }
        }

        bool CanUseInput() => viewCamera && viewCamera.isActiveAndEnabled && movement &&
            movement.isActiveAndEnabled && Cursor.lockState == CursorLockMode.Locked;

        Interactable FindTarget()
        {
            if (!CanUseInput()) return null;
            Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            // RaycastAll lets us ignore our own colliders without ignoring walls.
            var hits = Physics.RaycastAll(ray, maxDistance, raycastLayers,
                QueryTriggerInteraction.Collide);
            float nearest = float.PositiveInfinity;
            Collider first = null;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
                if (hit.collider.isTrigger && !hit.collider.GetComponent<DoorInteractionZone>()) continue;
                nearest = hit.distance;
                first = hit.collider;
            }
            if (!first) return null;
            var fixedZone = first.GetComponent<DoorInteractionZone>();
            Interactable candidate = fixedZone ? fixedZone.door : first.GetComponentInParent<Interactable>();
            if (!fixedZone && candidate is DoorInteractable) return null;
            return candidate && nearest <= candidate.interactionDistance &&
                candidate.CanInteract(viewCamera) ? candidate : null;
        }

        void SetTarget(Interactable value)
        {
            if (target == value) return;
            if (target) target.SetHighlighted(false);
            target = value;
            if (target) target.SetHighlighted(true);
        }

        void OnGUI()
        {
            if (!target || !CanUseInput()) return;
            if (!dot)
            {
                const int size = 32;
                dot = new Texture2D(size, size, TextureFormat.RGBA32, false)
                    { name = "Interaction dot", hideFlags = HideFlags.HideAndDontSave };
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float distance = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).magnitude;
                        dot.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(size / 2f - distance)));
                    }
                dot.Apply();
            }
            Color previous = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect((Screen.width - dotSize) / 2f,
                (Screen.height - dotSize) / 2f, dotSize, dotSize), dot);
            if (showPrompt)
            {
                if (promptStyle == null)
                    promptStyle = new GUIStyle(GUI.skin.label)
                        { alignment = TextAnchor.MiddleCenter, fontSize = 16 };
                GUI.Label(new Rect(Screen.width / 2f - 160f, Screen.height / 2f + 18f,
                    320f, 30f), target.Prompt, promptStyle);
            }
            GUI.color = previous;
        }

        void OnDisable()
        {
            active.Remove(this);
            SetTarget(null);
        }

        void OnDestroy()
        {
            if (dot) Destroy(dot);
        }
    }
}
