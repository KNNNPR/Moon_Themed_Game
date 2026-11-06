using MoonThemedGame.Interactions;
using UnityEngine;
namespace MoonThemedGame.Scavenger
{
    public sealed class BridgeButtonInteractable : Interactable
    {
        public override string Prompt=>"["+GameControls.Label(GameCommand.Interact)+"] "+(Loc.Russian?"Опустить мост":"Lower bridge");
        public override void Interact()
        {
            if(!isActiveAndEnabled)return;
            var lever=GetComponent<RobotLever>();if(lever)lever.Pull();
        }
    }
}
