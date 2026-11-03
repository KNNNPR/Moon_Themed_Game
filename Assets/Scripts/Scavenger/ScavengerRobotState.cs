using System;
using MoonThemedGame.Computer;
using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    public enum RobotStatus { Online, Failing, Offline, Evacuating }

    [DisallowMultipleComponent]
    public sealed class ScavengerRobotState : MonoBehaviour
    {
        [Min(1)] public float capacity = 100;
        [Tooltip("Charge per second while the operator is connected.")]
        public float drainPerSecond = 1;
        [Range(0, 1)] public float lowChargeThreshold = 0.2f;
        [Min(0)] public float shutdownDelay = 10;
        [Min(0)] public int evacuationCost = 15;
        [Min(0)] public float evacuationTime = 8;

        float timer;

        public float Charge { get; private set; }
        public float Normalized => Charge / capacity;
        public RobotStatus Status { get; private set; } = RobotStatus.Online;
        public bool Active { get; set; }
        public bool LowCharge => Status == RobotStatus.Online && Normalized <= lowChargeThreshold;
        public float TimeLeft => Mathf.Max(0, timer);
        public bool HasSignal => Status == RobotStatus.Online || Status == RobotStatus.Failing;

        public float Interference => Status switch
        {
            RobotStatus.Online => LowCharge ? Mathf.Lerp(0.35f, 0.05f, Normalized / lowChargeThreshold) : 0,
            RobotStatus.Failing => Mathf.Lerp(1, 0.5f, timer / Mathf.Max(0.01f, shutdownDelay)),
            _ => 1
        };

        public event Action<RobotStatus> StatusChanged;

        void Awake() => Charge = capacity;

        void Update()
        {
            float dt = Time.deltaTime;
            switch (Status)
            {
                case RobotStatus.Online:
                    if (Active) Charge = Mathf.Max(0, Charge - drainPerSecond * dt);
                    if (Charge <= 0) SetStatus(RobotStatus.Failing, shutdownDelay);
                    break;
                case RobotStatus.Failing:
                    timer -= dt;
                    if (timer <= 0) SetStatus(RobotStatus.Offline, 0);
                    break;
                case RobotStatus.Evacuating:
                    timer -= dt;
                    if (timer <= 0)
                    {
                        Charge = capacity;
                        SetStatus(RobotStatus.Online, 0);
                    }
                    break;
            }
        }

        public bool CanAffordEvacuation(PcMonitorTerminal terminal) => terminal && terminal.Credits >= evacuationCost;

        public bool OrderEvacuation(PcMonitorTerminal terminal)
        {
            if (Status != RobotStatus.Offline || !CanAffordEvacuation(terminal)) return false;
            terminal.SetCredits(terminal.Credits - evacuationCost);
            SetStatus(RobotStatus.Evacuating, evacuationTime);
            return true;
        }

        void SetStatus(RobotStatus status, float duration)
        {
            Status = status;
            timer = duration;
            StatusChanged?.Invoke(status);
        }
    }
}
