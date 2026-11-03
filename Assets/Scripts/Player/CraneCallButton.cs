using MoonThemedGame.Interactions;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    [DisallowMultipleComponent]
    public sealed class CraneCallButton : Interactable
    {
        public RepairCranePickup crane;
        public override string Prompt => crane && crane.IsBusy ? "Кран занят" : "E — Вызвать кран";
        public override void Interact()
        {
            if (crane) crane.TryCollect();
        }
    }
}
