using System;
using MoonThemedGame.Computer;
using MoonThemedGame.Weather;
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
        [Tooltip("Charge per second while parked at the CHARGE station.")]
        public float chargePerSecond = 8;
        [Range(0, 1)] public float lowChargeThreshold = 0.2f;
        [Min(0)] public float shutdownDelay = 10;
        [Min(0)] public int evacuationCost = 15;
        [Min(0)] public float evacuationTime = 8;
        [Min(0)] public int cargoCapacity = 4;
        public int Cargo { get; set; }

        float timer;

        public float Charge { get; private set; }
        public float Normalized => Charge / capacity;
        public RobotStatus Status { get; private set; } = RobotStatus.Online;
        public bool Active { get; set; }
        // Set every frame by the link while the robot stands at the CHARGE station.
        public bool AtCharger { get; set; }
        public bool Charging => AtCharger && (Status == RobotStatus.Online || Status == RobotStatus.Failing) && Charge < capacity;
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

        // Radiation outside the base eats the battery even while the robot stands idle.
        public float RadiationMultiplier
        {
            get
            {
                var weather = MoonWeatherSystem.Active;
                return weather ? weather.EnergyMultiplier(transform.position) : 1;
            }
        }

        public bool Irradiated => RadiationMultiplier > 1;
        float Drain => (Active ? drainPerSecond * RadiationMultiplier : 0) + (!Active && Irradiated ? drainPerSecond * (RadiationMultiplier - 1) : 0);

        void Update()
        {
            float dt = Time.deltaTime;
            switch (Status)
            {
                case RobotStatus.Online:
                    if (Charging) Charge = Mathf.Min(capacity, Charge + chargePerSecond * dt);
                    else Charge = Mathf.Max(0, Charge - Drain * dt);
                    if (Charge <= 0) SetStatus(RobotStatus.Failing, shutdownDelay);
                    break;
                case RobotStatus.Failing:
                    if (Charging)
                    {
                        Charge = Mathf.Min(capacity, Charge + chargePerSecond * dt);
                        SetStatus(RobotStatus.Online, 0);
                        break;
                    }
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

        public void ApplyEnergyDamage(float amount)
        {
            if (Status != RobotStatus.Online || amount <= 0) return;
            Charge = Mathf.Max(0, Charge - amount);
            if (Charge <= 0) SetStatus(RobotStatus.Failing, shutdownDelay);
        }

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
