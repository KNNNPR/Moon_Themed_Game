using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    public enum UplinkStatus { Ok, NoSatellite, OutOfRange, Blocked }

    // Each satellite is in range for `window` seconds out of every `period`;
    // its signal rises and falls over the window.
    [Serializable]
    public sealed class Satellite
    {
        public string name;
        [Min(10)] public float period = 180;
        [Min(5)] public float window = 100;
        public float offset;

        float Cycle(float time) => Mathf.Repeat(time + offset, period);

        public bool InRange(float time) => Cycle(time) < window;
        public float TimeLeft(float time) => InRange(time) ? window - Cycle(time) : 0;
        public float TimeToReturn(float time) => InRange(time) ? 0 : period - Cycle(time);
        public float WindowProgress(float time) => InRange(time) ? Cycle(time) / window : 0;
        public float Strength(float time) => InRange(time) ? Mathf.Sin(Mathf.PI * Cycle(time) / window) : 0;
        public int Bars(float time) => InRange(time) ? Mathf.Clamp(Mathf.CeilToInt(Strength(time) * 4), 1, 4) : 0;
    }

    // The robot's feed goes through one satellite, picked on the PC (SAT-LINK) or with 1/2/3 in the feed.
    public sealed class SatelliteNetwork : MonoBehaviour
    {
        public List<Satellite> satellites = new List<Satellite>
        {
            new Satellite { name = "ARGUS", period = 150, window = 90, offset = 0 },
            new Satellite { name = "HERMES", period = 180, window = 100, offset = 70 },
            new Satellite { name = "VESTA", period = 210, window = 110, offset = 140 }
        };
        [Tooltip("Warn the operator this many seconds before the active satellite leaves range.")]
        public float leaveWarning = 15;
        [Tooltip("Anything this close above the robot's antenna counts as cover.")]
        public float skyCheckHeight = 60;

        static SatelliteNetwork instance;
        readonly RaycastHit[] hits = new RaycastHit[16];

        public static SatelliteNetwork Instance
        {
            get
            {
                if (instance) return instance;
                instance = FindAnyObjectByType<SatelliteNetwork>();
                if (!instance) instance = new GameObject("Satellite Network").AddComponent<SatelliteNetwork>();
                return instance;
            }
        }

        public int ActiveIndex { get; private set; }
        public Satellite Active => ActiveIndex >= 0 && ActiveIndex < satellites.Count ? satellites[ActiveIndex] : null;
        public float Clock => Time.time;
        public bool ActiveLeavingSoon => Active != null && Active.InRange(Clock) && Active.TimeLeft(Clock) <= leaveWarning;

        public event Action<Satellite> Selected;

        public void Select(int index)
        {
            if (index == ActiveIndex || index < 0 || index >= satellites.Count) return;
            ActiveIndex = index;
            Selected?.Invoke(Active);
        }

        public bool SkyVisible(Vector3 antenna, Collider[] ignore)
        {
            int count = Physics.RaycastNonAlloc(antenna, Vector3.up, hits, skyCheckHeight, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (ignore == null || Array.IndexOf(ignore, hits[i].collider) < 0) return false;
            return true;
        }

        public UplinkStatus Evaluate(Vector3 antenna, Collider[] ignore, out float strength)
        {
            strength = 0;
            var satellite = Active;
            if (satellite == null) return UplinkStatus.NoSatellite;
            if (!SkyVisible(antenna, ignore)) return UplinkStatus.Blocked;
            if (!satellite.InRange(Clock)) return UplinkStatus.OutOfRange;
            strength = satellite.Strength(Clock);
            return UplinkStatus.Ok;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() => instance = null;
    }
}
