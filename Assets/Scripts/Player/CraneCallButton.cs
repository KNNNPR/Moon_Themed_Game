using MoonThemedGame.Audio;
using MoonThemedGame.Interactions;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    [DisallowMultipleComponent]
    public sealed class CraneCallButton : Interactable
    {
        public RepairCranePickup crane;
        public override string Prompt
        {
            get
            {
                bool ru = Loc.Russian;
                if (!crane || !crane.IsReady || !crane.dropper) return ru ? "Кран недоступен" : "Crane offline";
                if (crane.IsBusy) return ru ? "Кран занят" : "Crane is busy";
                var robot = crane.dropper.CurrentRobot;
                if (!robot) return ru ? "На посту нет робота" : "No robot in the bay";
                if (!robot.IsSettled) return ru ? "Дождитесь приземления" : "Wait for the robot to land";
                if (!robot.ReadyForCollection) return robot.IsDismantling?(ru?"Сначала разберите бота":"Dismantle the robot first"):(ru ? "Сначала завершите ремонт" : "Finish repairing the robot");
                return ru ? "[E] Вызвать кран" : "[E] Call crane";
            }
        }
        public override void Interact()
        {
            GameAudio.At(Sound.Button,transform.position);if (crane) crane.TryCollect();
        }
    }
}
