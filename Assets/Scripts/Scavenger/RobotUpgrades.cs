using System;
using MoonThemedGame.Computer;
using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    public enum Upgrade { Capacity, Drain, Speed, Headlights, Cargo, Arm }

    // Upgrades bought in the SHOP for this session; ScavengerLink applies them to the robot.
    public static class RobotUpgrades
    {
        public static readonly Upgrade[] All = { Upgrade.Capacity, Upgrade.Drain, Upgrade.Speed, Upgrade.Headlights, Upgrade.Cargo, Upgrade.Arm };

        static readonly int[][] Prices =
        {
            new[] { 10, 18, 30 },
            new[] { 10, 18, 30 },
            new[] { 12, 20, 32 },
            new[] { 20 },
            new[] { 8, 15, 25 },
            new[] { 25 }
        };
        static readonly float[] CapacityScales = { 1, 1.4f, 1.85f, 2.4f };
        static readonly float[] DrainScales = { 1, 0.8f, 0.62f, 0.45f };
        static readonly float[] SpeedScales = { 1, 1.2f, 1.4f, 1.6f };
        static readonly int[] CargoSlotsByLevel = { 4, 6, 8, 10 };
        const float LampFogScale = 0.35f;

        static readonly int[] levels = new int[All.Length];

        public static event Action Changed;

        public static int Level(Upgrade upgrade) => levels[(int)upgrade];
        public static int MaxLevel(Upgrade upgrade) => Prices[(int)upgrade].Length;
        public static bool IsMax(Upgrade upgrade) => Level(upgrade) >= MaxLevel(upgrade);
        public static int Price(Upgrade upgrade) => IsMax(upgrade) ? 0 : Prices[(int)upgrade][Level(upgrade)];

        public static float CapacityScale => CapacityScales[Level(Upgrade.Capacity)];
        public static float DrainScale => DrainScales[Level(Upgrade.Drain)];
        public static float SpeedScale => SpeedScales[Level(Upgrade.Speed)];
        public static bool Lamps => Level(Upgrade.Headlights) > 0;
        public static float FogScale => Lamps ? LampFogScale : 1;
        public static int CargoSlots => CargoSlotsByLevel[Level(Upgrade.Cargo)];
        public static bool HasArm => Level(Upgrade.Arm) > 0;

        public static bool TryBuy(Upgrade upgrade, PcMonitorTerminal terminal)
        {
            if (IsMax(upgrade) || !terminal || terminal.Credits < Price(upgrade)) return false;
            terminal.SetCredits(terminal.Credits - Price(upgrade));
            levels[(int)upgrade]++;
            Changed?.Invoke();
            return true;
        }

        public static string Title(Upgrade upgrade) => Loc.T(upgrade switch
        {
            Upgrade.Capacity => "BATTERY CELLS",
            Upgrade.Drain => "POWER REGULATOR",
            Upgrade.Speed => "DRIVE WHEELS",
            Upgrade.Headlights => "FOG LAMPS",
            Upgrade.Arm => "MANIPULATOR ARM",
            _ => "CARGO BAY"
        });

        public static string Description(Upgrade upgrade) => Loc.T(upgrade switch
        {
            Upgrade.Capacity => "Bigger battery, longer runs.",
            Upgrade.Drain => "Battery drains slower.",
            Upgrade.Speed => "Higher top speed and acceleration.",
            Upgrade.Headlights => "Longer, brighter beams. Cut through moon dust.",
            Upgrade.Arm => "Lets the robot pull levers.",
            _ => "More room for salvage."
        });

        public static string Value(Upgrade upgrade, int level) => upgrade switch
        {
            Upgrade.Capacity => Loc.F("{0} CELL", Mathf.RoundToInt(100 * CapacityScales[level])),
            Upgrade.Drain => level == 0 ? Loc.T("STOCK DRAIN") : Loc.F("-{0}% DRAIN", Mathf.RoundToInt((1 - DrainScales[level]) * 100)),
            Upgrade.Speed => level == 0 ? Loc.T("STOCK SPEED") : Loc.F("+{0}% SPEED", Mathf.RoundToInt((SpeedScales[level] - 1) * 100)),
            Upgrade.Headlights => level == 0 ? Loc.T("STOCK LAMPS") : Loc.F("FOG -{0}%", Mathf.RoundToInt((1 - LampFogScale) * 100)),
            Upgrade.Arm => Loc.T(level == 0 ? "NO ARM" : "PULLS LEVERS"),
            _ => Loc.F("{0} SLOTS", CargoSlotsByLevel[level])
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState()
        {
            Array.Clear(levels, 0, levels.Length);
            Changed = null;
        }
    }
}
