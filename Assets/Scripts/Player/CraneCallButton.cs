using MoonThemedGame.Interactions;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    [DisallowMultipleComponent]
    public sealed class CraneCallButton : Interactable
    {
        public RepairCranePickup crane;
        public override string Prompt => crane && crane.IsBusy ? "Crane is busy" : "E — Call crane";
        public override void Interact()
        {
            if (crane) crane.TryCollect();
        }
    }
}
