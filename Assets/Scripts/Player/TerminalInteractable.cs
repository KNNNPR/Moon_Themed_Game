using UnityEngine;
using MoonThemedGame.Computer;

namespace MoonThemedGame.Interactions
{
    [DisallowMultipleComponent, RequireComponent(typeof(PcMonitorTerminal))]
    public sealed class TerminalInteractable : Interactable
    {
        PcMonitorTerminal terminal;
        public override string Prompt => "E — Use computer";

        void Awake() => terminal = GetComponent<PcMonitorTerminal>();

        public override bool CanInteract(Camera camera)
        {
            return base.CanInteract(camera) && terminal && terminal.isActiveAndEnabled &&
                !terminal.IsFocused && !terminal.IsTransitioning &&
                terminal.TryGetPanelPosition(camera,
                    new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), out _);
        }

        public override void Interact()
        {
            if (terminal) terminal.Focus();
        }
    }
}
