using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    public enum UplinkStatus { Ok, NoSatellite, OutOfRange }

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
        public float Strength(float time) => InRange(time) ? 0.25f + 0.75f * Mathf.Sin(Mathf.PI * Cycle(time) / window) : 0;
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
        [Tooltip("Share of the signal lost when the sky above is fully covered.")]
        [Range(0, 1)] public float coverPenalty = 0.8f;

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

        static readonly Vector3[] SkyRays = BuildSkyRays();

        static Vector3[] BuildSkyRays()
        {
            var rays = new Vector3[9];
            rays[0] = Vector3.up;
            for (int i = 0; i < 8; i++) rays[i + 1] = Quaternion.Euler(0, i * 45, 0) * Quaternion.Euler(35, 0, 0) * Vector3.up;
            return rays;
        }

        // Share of the sky above the antenna hidden by something (0 = open, 1 = fully covered).
        public float Cover(Vector3 antenna, Collider[] ignore)
        {
            int covered = 0;
            foreach (var direction in SkyRays)
            {
                int count = Physics.RaycastNonAlloc(antenna, direction, hits, skyCheckHeight, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                    if (ignore == null || Array.IndexOf(ignore, hits[i].collider) < 0) { covered++; break; }
            }
            return covered / (float)SkyRays.Length;
        }

        public float ApplyCover(float strength, float cover) => strength * (1 - coverPenalty * cover);

        public UplinkStatus Evaluate(Vector3 antenna, Collider[] ignore, out float quality, out float cover)
        {
            quality = 0;
            cover = Cover(antenna, ignore);
            var satellite = Active;
            if (satellite == null) return UplinkStatus.NoSatellite;
            if (!satellite.InRange(Clock)) return UplinkStatus.OutOfRange;
            quality = ApplyCover(satellite.Strength(Clock), cover);
            return UplinkStatus.Ok;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() => instance = null;
    }
}
