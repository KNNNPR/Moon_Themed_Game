using UnityEngine;

namespace MoonThemedGame.Interactions
{
    public abstract class Interactable : MonoBehaviour
    {
        [Min(0.1f)] public float interactionDistance = 2.5f;
        public InteractionOutline outline;
        public virtual string Prompt => "E — Interact";
        public virtual bool CanInteract(Camera camera) => isActiveAndEnabled;
        public abstract void Interact();

        public void SetHighlighted(bool value)
        {
            if (!outline && value)
            {
                outline = GetComponent<InteractionOutline>();
                if (!outline) outline = gameObject.AddComponent<InteractionOutline>();
            }
            if (outline) outline.SetHighlighted(value);
        }

        protected virtual void OnDisable() => SetHighlighted(false);
    }
}
