using UnityEngine;

namespace MoonThemedGame.Computer
{
    public enum StationGameMode { Story, Endless }
    public static class StationRun
    {
        public const string EndlessUnlockKey="palehorse.endless.unlocked.v1";
        public static StationGameMode Mode {get;private set;}
        public static bool EndlessUnlocked=>PlayerPrefs.GetInt(EndlessUnlockKey,0)==1;
        public static bool Select(StationGameMode mode)
        {
            if(mode==StationGameMode.Endless && !EndlessUnlocked)return false;
            Mode=mode;MoonThemedGame.Scavenger.RobotUpgrades.ResetForRun();return true;
        }
        public static void UnlockEndless(){PlayerPrefs.SetInt(EndlessUnlockKey,1);PlayerPrefs.Save();}
        internal static void ContinueEndless(){if(EndlessUnlocked)Mode=StationGameMode.Endless;}
        internal static void DebugReplayStory()=>Mode=StationGameMode.Story;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()=>Mode=StationGameMode.Story;
    }
}
