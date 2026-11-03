using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    public enum UplinkStatus { Ok, NoSatellite, OutOfRange, Intercepted, Hopping }

    public enum RivalPhase { None, Incoming, Hijacked }

    public enum Polarization { RHCP, LHCP }

    // Each satellite is in range for `window` seconds out of every `period`;
    // its signal rises and falls over the window.
    [Serializable]
    public sealed class Satellite
    {
        public string name;
        public Polarization polarization;
        [Min(10)] public float period = 180;
        [Min(5)] public float window = 100;
        public float offset;

        float Cycle(float time) => Mathf.Repeat(time + offset, period);

        public bool InRange(float time) => Cycle(time) < window;
        public float TimeLeft(float time) => InRange(time) ? window - Cycle(time) : 0;
        public float TimeToReturn(float time) => InRange(time) ? 0 : period - Cycle(time);
        public float WindowProgress(float time) => InRange(time) ? Cycle(time) / window : 0;
        public float Angle(float time) => 2 * Mathf.PI * (Cycle(time) - window * 0.5f) / period;
        public float Strength(float time) => InRange(time) ? 0.25f + 0.75f * Mathf.Sin(Mathf.PI * Cycle(time) / window) : 0;
        public int Bars(float time) => InRange(time) ? Mathf.Clamp(Mathf.CeilToInt(Strength(time) * 4), 1, 4) : 0;
    }

    // The robot's feed goes through one satellite, picked on the PC (SAT-LINK) or with 1/2/3 in the feed.
    // All satellites share one period with staggered windows, so one is always in range and they leave one at a time.
    public sealed class SatelliteNetwork : MonoBehaviour
    {
        public List<Satellite> satellites = new List<Satellite>
        {
            new Satellite { name = "ARGUS", polarization = Polarization.RHCP, period = 180, window = 110, offset = 0 },
            new Satellite { name = "HERMES", polarization = Polarization.LHCP, period = 180, window = 110, offset = 120 },
            new Satellite { name = "VESTA", polarization = Polarization.RHCP, period = 180, window = 110, offset = 60 }
        };
        [Tooltip("Warn the operator this many seconds before the active satellite leaves range.")]
        public float leaveWarning = 15;
        [Tooltip("Anything this close above the robot's antenna counts as cover.")]
        public float skyCheckHeight = 60;
        [Tooltip("Share of the signal lost when the sky above is fully covered.")]
        [Range(0, 1)] public float coverPenalty = 0.8f;

        [Header("Rival intercept")]
        public string rivalName = "CERBERUS-9";
        [Tooltip("Seconds of connection before the first rival attack (min, max).")]
        public Vector2 rivalFirstAttack = new Vector2(25, 45);
        [Tooltip("Seconds of connection between later rival attacks (min, max).")]
        public Vector2 rivalInterval = new Vector2(50, 110);
        public float interceptDuration = 40;
        [Range(0, 1)] public float interceptJam = 0.7f;
        [Tooltip("Intercept progress the rival gains for each failed carrier lock.")]
        [Range(0, 1)] public float lockPenalty = 0.12f;
        [Tooltip("Seconds without a picture while the receiver retunes after a successful lock.")]
        public float retuneDuration = 1.5f;
        public int hijackCreditLoss = 5;

        [Header("Receiver")]
        public float[] channels = { 2.15f, 2.30f, 2.45f, 2.60f };

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
        public event Action<RivalPhase> RivalChanged;

        public event Action RivalAttack;

        // The rival locks onto the robot's carrier, not a satellite: switching satellites does not shake it off.
        public RivalPhase Rival { get; private set; }
        public int RivalTarget => Rival != RivalPhase.None ? ActiveIndex : -1;
        public float RivalProgress { get; private set; }
        public float HopTimeLeft { get; private set; }
        public bool Hopping => HopTimeLeft > 0;
        public bool RivalOnActive => Rival != RivalPhase.None;
        public float Jam => Rival == RivalPhase.Incoming ? 1 - interceptJam * RivalProgress : 1;
        public int CarrierChannel { get; private set; }
        public bool[] Jammed { get; private set; } = new bool[0];
        float rivalTimer = -1;

        public bool IsJammed(int channel) => Rival != RivalPhase.None && channel >= 0 && channel < Jammed.Length && Jammed[channel];

        public void TickRival(bool exposed)
        {
            float dt = Time.deltaTime;
            if (HopTimeLeft > 0) HopTimeLeft = Mathf.Max(0, HopTimeLeft - dt);
            if (rivalTimer < 0) rivalTimer = UnityEngine.Random.Range(rivalFirstAttack.x, rivalFirstAttack.y);
            switch (Rival)
            {
                case RivalPhase.None:
                    if (!exposed || Hopping || Active == null) return;
                    rivalTimer -= dt;
                    if (rivalTimer > 0) return;
                    RivalProgress = 0;
                    JamChannels();
                    SetRival(RivalPhase.Incoming);
                    RivalAttack?.Invoke();
                    break;
                case RivalPhase.Incoming:
                    if (!exposed) return;
                    RivalProgress = Mathf.Min(1, RivalProgress + dt / interceptDuration);
                    if (RivalProgress >= 1) SetRival(RivalPhase.Hijacked);
                    break;
            }
        }

        // Every channel but one free one is jammed, always including the current carrier.
        void JamChannels()
        {
            Jammed = new bool[channels.Length];
            int free;
            do free = UnityEngine.Random.Range(0, channels.Length);
            while (channels.Length > 1 && free == CarrierChannel);
            for (int i = 0; i < Jammed.Length; i++) Jammed[i] = i != free;
        }

        public void Repel(int channel)
        {
            CarrierChannel = channel;
            if (Rival == RivalPhase.None) return;
            HopTimeLeft = retuneDuration;
            EndRival();
        }

        public void PenalizeLock()
        {
            if (Rival != RivalPhase.Incoming) return;
            RivalProgress = Mathf.Min(1, RivalProgress + lockPenalty);
            if (RivalProgress >= 1) SetRival(RivalPhase.Hijacked);
        }

        void EndRival()
        {
            RivalProgress = 0;
            rivalTimer = UnityEngine.Random.Range(rivalInterval.x, rivalInterval.y);
            SetRival(RivalPhase.None);
        }

        void SetRival(RivalPhase phase)
        {
            if (Rival == phase) return;
            Rival = phase;
            RivalChanged?.Invoke(phase);
        }

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

        // The rival jams the robot's carrier itself, so the base relay is affected too.
        public UplinkStatus Threat(out float jam)
        {
            jam = Jam;
            if (Hopping) return UplinkStatus.Hopping;
            return Rival == RivalPhase.Hijacked && RivalOnActive ? UplinkStatus.Intercepted : UplinkStatus.Ok;
        }

        public float ApplyCover(float strength, float cover) => strength * (1 - coverPenalty * cover);

        public UplinkStatus Evaluate(Vector3 antenna, Collider[] ignore, out float quality, out float cover)
        {
            quality = 0;
            cover = Cover(antenna, ignore);
            var threat = Threat(out float jam);
            if (threat != UplinkStatus.Ok) return threat;
            var satellite = Active;
            if (satellite == null) return UplinkStatus.NoSatellite;
            if (!satellite.InRange(Clock)) return UplinkStatus.OutOfRange;
            quality = ApplyCover(satellite.Strength(Clock), cover) * jam;
            return UplinkStatus.Ok;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() => instance = null;
    }
}
