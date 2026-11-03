using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    public enum UplinkStatus { Ok, NoSatellite, BelowHorizon, Blocked }

    [Serializable]
    public sealed class Satellite
    {
        public string name;
        [Min(10)] public float period = 300;
        [Range(0, 1)] public float phase;
        public float azimuthStart;
        public float azimuthSweep = 160;
        [Range(5, 90)] public float maxElevation = 70;

        float Orbit(float time) => Mathf.Repeat(time / period + phase, 1);

        // Half of each orbit is above the horizon.
        public float Elevation(float time) => maxElevation * Mathf.Sin(Orbit(time) * 2 * Mathf.PI);
        public float Azimuth(float time) => Mathf.Repeat(azimuthStart + azimuthSweep * Orbit(time) * 2, 360);
        public Vector3 Direction(float time) => Quaternion.Euler(-Elevation(time), Azimuth(time), 0) * Vector3.forward;
        public bool InView(float time, float minElevation) => Elevation(time) > minElevation;

        public float TimeToRise(float time) => Orbit(time) < 0.5f ? 0 : (1 - Orbit(time)) * period;
        public float TimeToSet(float time) => Orbit(time) < 0.5f ? (0.5f - Orbit(time)) * period : 0;
    }

    // Satellites the robot relays its feed through. The operator picks the active one on the PC.
    public sealed class SatelliteNetwork : MonoBehaviour
    {
        public List<Satellite> satellites = new List<Satellite>
        {
            new Satellite { name = "ARGUS", period = 300, phase = 0.05f, azimuthStart = 40, maxElevation = 72 },
            new Satellite { name = "HERMES", period = 360, phase = 0.42f, azimuthStart = 200, maxElevation = 48 },
            new Satellite { name = "VESTA", period = 420, phase = 0.75f, azimuthStart = 300, azimuthSweep = 120, maxElevation = 64 }
        };
        [Range(0, 30)] public float minElevation = 5;
        [Tooltip("Elevation above the horizon at which the link reaches full quality.")]
        [Range(5, 60)] public float fullQualityElevation = 25;
        public float lineOfSightRange = 400;

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

        public event Action<Satellite> Selected;

        public void Select(int index)
        {
            if (index == ActiveIndex || index < 0 || index >= satellites.Count) return;
            ActiveIndex = index;
            Selected?.Invoke(Active);
        }

        public float Quality(Satellite satellite) =>
            Mathf.Clamp01((satellite.Elevation(Clock) - minElevation) / (fullQualityElevation - minElevation));

        public UplinkStatus Evaluate(Vector3 position, Collider[] ignore, out float quality)
        {
            quality = 0;
            var satellite = Active;
            if (satellite == null) return UplinkStatus.NoSatellite;
            if (!satellite.InView(Clock, minElevation)) return UplinkStatus.BelowHorizon;
            int count = Physics.RaycastNonAlloc(position, satellite.Direction(Clock), hits, lineOfSightRange, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (ignore == null || Array.IndexOf(ignore, hits[i].collider) < 0) return UplinkStatus.Blocked;
            quality = Quality(satellite);
            return UplinkStatus.Ok;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() => instance = null;
    }
}
