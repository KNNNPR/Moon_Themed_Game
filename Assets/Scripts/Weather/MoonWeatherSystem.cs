using System;
using MoonThemedGame.Scavenger;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoonThemedGame.Weather
{
    public enum WeatherKind { Sunny, MoonDust, Radiation, MeteoriteRain }
    public readonly struct WeatherForecast
    {
        public readonly WeatherKind kind;
        public readonly float startsIn, duration;
        public WeatherForecast(WeatherKind kind, float startsIn, float duration)
        { this.kind = kind; this.startsIn = startsIn; this.duration = duration; }
    }

    [DisallowMultipleComponent]
    public sealed class MoonWeatherSystem : MonoBehaviour
    {
        public MoonWeatherAssets assets;
        [Header("Schedule (seconds)")]
        public WeatherKind startingWeather = WeatherKind.Sunny;
        [Min(10)] public float sunnyDuration = 90, dustDuration = 75, toxicityDuration = 75, meteorDuration = 90;
        public int forecastSeed = 415;
        [Header("Sealed base / world coordinates")]
        public Vector3 shelterCenter;
        public Vector3 shelterSize = new Vector3(12, 8, 12);
        public Bounds[] additionalShelters = Array.Empty<Bounds>();
        [Header("Atmosphere outside the base")]
        [Min(0)] public float dustDensity = 0.065f;
        [Min(0)] public float toxicDensity = 0.009f;
        [Range(0, 1)] public float toxicOpacity = 0.28f;
        [Min(0.1f)] public float atmosphereTransition = 3;
        [Min(1)] public float toxicEnergyMultiplier = 2;
        [Header("Meteorites")]
        [Min(5)] public float warningSeconds = 5;
        [Min(0.2f)] public float fallSeconds = 1.6f;
        [Tooltip("Meteorite diameter in metres, chosen randomly for each impact.")]
        public Vector2 meteorDiameterRange = new Vector2(1.4f, 2.8f);
        public Vector2 meteorInterval = new Vector2(7, 12);
        public Vector2 strikeDistance = new Vector2(14, 45);
        [Range(0, 1)] public float nearbyRobotStrikeChance = 0.25f;
        [Min(1)] public float strikeRadius = 2.5f;
        [Min(0)] public float impactEnergyDamage = 35;
        [Min(1)] public float shakeDistance = 20;
        public LayerMask groundLayers = ~0;

        public static MoonWeatherSystem Active { get; private set; }
        public WeatherKind Current { get; private set; }
        public float Remaining { get; private set; }
        public float CurrentDuration { get; private set; }
        public Bounds Shelter => new Bounds(shelterCenter, shelterSize);
        public int ShelterCount => Mathf.Min(4, 1 + (additionalShelters?.Length ?? 0));
        public Bounds GetShelter(int index) => index == 0 ? Shelter : additionalShelters[index - 1];
        public float Density { get; private set; }
        public Color AtmosphereTint { get; private set; } = new Color(0.22f, 0.2f, 0.17f);
        public float OpacityLimit { get; private set; } = 0.96f;
        public int PendingStrikes { get; private set; }
        public int Impacts { get; private set; }
        public event Action<WeatherKind> WeatherChanged;
        public event Action<Vector3> Impact;
        WeatherKind nextOne, nextTwo;
        System.Random random;
        bool firstTail = true;
        float nextMeteor, emissionRemainder;
        ParticleSystem particles;
        readonly ParticleSystem.Particle[] liveParticles = new ParticleSystem.Particle[1200];
        readonly RaycastHit[] groundHits = new RaycastHit[64];

        public static string Name(WeatherKind kind) => kind switch
        {
            WeatherKind.MoonDust => "FOG / MOON DUST", WeatherKind.Radiation => "RADIATION",
            WeatherKind.MeteoriteRain => "METEORITE RAIN", _ => "SUNNY"
        };
        public static string Effect(WeatherKind kind) => kind switch
        {
            WeatherKind.MoonDust => "Low Visibility",
            WeatherKind.Radiation => "Drains your energy faster",
            WeatherKind.MeteoriteRain => "Meteorites drop from the sky,\nwhen hit - drains your energy",
            _ => "Perfect Weather"
        };

        void Awake()
        {
            Active = this;
            random = new System.Random(forecastSeed);
            Current = startingWeather;
            CurrentDuration = Remaining = Duration(Current);
            nextOne = (WeatherKind)(((int)Current + 1) % 4);
            nextTwo = (WeatherKind)(((int)Current + 2) % 4);
            nextMeteor = UnityEngine.Random.Range(meteorInterval.x, meteorInterval.y);
            CreateParticles();
        }

        public float Duration(WeatherKind kind) => Mathf.Max(1, kind switch
        {
            WeatherKind.MoonDust => dustDuration, WeatherKind.Radiation => toxicityDuration,
            WeatherKind.MeteoriteRain => meteorDuration, _ => sunnyDuration
        });
        public WeatherForecast Forecast(int index) => index == 0
            ? new WeatherForecast(nextOne, Remaining, Duration(nextOne))
            : new WeatherForecast(nextTwo, Remaining + Duration(nextOne), Duration(nextTwo));
        public bool IsSheltered(Vector3 position)
        {
            for (int i = 0; i < ShelterCount; i++) if (GetShelter(i).Contains(position)) return true;
            return false;
        }
        public bool IsBaseFootprint(Vector3 position, float margin = 2)
        {
            for (int i = 0; i < ShelterCount; i++)
            {
                var bounds = GetShelter(i); bounds.Expand(new Vector3(margin * 2, 0, margin * 2));
                if (position.x >= bounds.min.x && position.x <= bounds.max.x && position.z >= bounds.min.z && position.z <= bounds.max.z) return true;
            }
            return false;
        }
        public float EnergyMultiplier(Vector3 position) => Current == WeatherKind.Radiation && !IsSheltered(position) ? toxicEnergyMultiplier : 1;

        public void SetWeather(WeatherKind kind, float duration = -1)
        {
            Current = kind;
            CurrentDuration = Remaining = duration > 0 ? duration : Duration(kind);
            nextOne = (WeatherKind)(((int)kind + 1) % 4);
            nextTwo = (WeatherKind)(((int)kind + 2) % 4);
            firstTail = true;
            nextMeteor = 0.8f;
            WeatherChanged?.Invoke(Current);
        }

        public void SnapAtmosphere()
        {
            Density = Current == WeatherKind.MoonDust ? dustDensity : Current == WeatherKind.Radiation ? toxicDensity : 0;
            AtmosphereTint = Current == WeatherKind.Radiation ? new Color(0.13f, 0.32f, 0.065f) : new Color(0.22f, 0.2f, 0.17f);
            OpacityLimit = Current == WeatherKind.Radiation ? toxicOpacity : 0.96f;
        }

        public void AdvanceSchedule(float seconds)
        {
            Remaining -= Mathf.Max(0, seconds);
            while (Remaining <= 0)
            {
                Current = nextOne; nextOne = nextTwo;
                nextTwo = firstTail ? (WeatherKind)(((int)nextOne + 1) % 4) : PickAfter(nextOne);
                firstTail = false;
                CurrentDuration = Duration(Current);
                Remaining += CurrentDuration;
                nextMeteor = 0.8f;
                WeatherChanged?.Invoke(Current);
            }
        }

        WeatherKind PickAfter(WeatherKind previous)
        {
            return (WeatherKind)(((int)previous + 1 + random.Next(3)) % 4);
        }

        void Update()
        {
            AdvanceSchedule(Time.deltaTime);
            float target = Current == WeatherKind.MoonDust ? dustDensity : Current == WeatherKind.Radiation ? toxicDensity : 0;
            float blend = 1 - Mathf.Exp(-Time.deltaTime * 4 / Mathf.Max(0.1f, atmosphereTransition));
            Density = Mathf.Lerp(Density, target, blend);
            AtmosphereTint = Color.Lerp(AtmosphereTint, Current == WeatherKind.Radiation ? new Color(0.13f, 0.32f, 0.065f) : new Color(0.22f, 0.2f, 0.17f), blend);
            OpacityLimit = Mathf.Lerp(OpacityLimit, Current == WeatherKind.Radiation ? toxicOpacity : 0.96f, blend);
            UpdateParticles();
            if (Current == WeatherKind.MeteoriteRain && assets && assets.meteoritePrefab)
            {
                nextMeteor -= Time.deltaTime;
                if (nextMeteor <= 0)
                {
                    nextMeteor = UnityEngine.Random.Range(Mathf.Max(5.5f, meteorInterval.x), Mathf.Max(meteorInterval.x, meteorInterval.y));
                    if (PendingStrikes < 3 && TryFindStrike(out var point, out var normal)) ScheduleStrike(point, normal);
                }
            }
        }

        void CreateParticles()
        {
            if (!assets || !assets.particleMaterial) return;
            var host = new GameObject("Exterior moon dust / haze"); host.transform.SetParent(transform, false);
            particles = host.AddComponent<ParticleSystem>(); particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main; main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1200; main.startLifetime = 7; main.startSpeed = 0;
            var emission = particles.emission; emission.enabled = false;
            var shape = particles.shape; shape.enabled = false;
            var color = particles.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0, 1) }); color.color = gradient;
            var renderer = particles.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = assets.particleMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            particles.Play();
        }

        void UpdateParticles()
        {
            if (!particles) return;
            int count = particles.GetParticles(liveParticles);
            for (int i = 0; i < count; i++) if (IsSheltered(liveParticles[i].position)) liveParticles[i].remainingLifetime = 0;
            particles.SetParticles(liveParticles, count);
            bool dust = Current == WeatherKind.MoonDust, toxic = Current == WeatherKind.Radiation;
            if (!dust && !toxic) return;
            emissionRemainder += Time.deltaTime * (dust ? 45 : 12);
            var camera = Camera.main;
            var center = camera && !IsSheltered(camera.transform.position) ? camera.transform.position : shelterCenter;
            while (emissionRemainder >= 1)
            {
                emissionRemainder--;
                var point = center + new Vector3(UnityEngine.Random.Range(-35f, 35f), UnityEngine.Random.Range(-4f, 7f), UnityEngine.Random.Range(-35f, 35f));
                if (IsSheltered(point)) continue;
                particles.Emit(new ParticleSystem.EmitParams
                {
                    position = point, velocity = new Vector3(dust ? 1.3f : 0.3f, 0.04f, 0.3f),
                    startSize = dust ? UnityEngine.Random.Range(0.035f, 0.13f) : UnityEngine.Random.Range(1.3f, 3f),
                    startColor = dust ? new Color(0.7f, 0.65f, 0.55f, 0.3f) : new Color(0.3f, 0.6f, 0.15f, 0.055f),
                    startLifetime = 7
                }, 1);
            }
        }

        public bool TryFindStrike(out Vector3 point, out Vector3 normal)
        {
            var state = FindAnyObjectByType<ScavengerRobotState>();
            var center = state && !IsSheltered(state.transform.position) ? state.transform.position : shelterCenter;
            for (int attempt = 0; attempt < 32; attempt++)
            {
                var direction = UnityEngine.Random.insideUnitCircle.normalized;
                float radius = attempt == 0 && state && UnityEngine.Random.value < nearbyRobotStrikeChance
                    ? UnityEngine.Random.Range(0, 2.5f) : UnityEngine.Random.Range(strikeDistance.x, strikeDistance.y);
                var candidate = center + new Vector3(direction.x, 0, direction.y) * radius;
                if (IsBaseFootprint(candidate, 3)) continue;
                int count = Physics.RaycastNonAlloc(candidate + Vector3.up * 100, Vector3.down, groundHits, 200, groundLayers, QueryTriggerInteraction.Ignore);
                float nearest = float.MaxValue;
                int selected = -1;
                for (int i = 0; i < count; i++)
                {
                    var hit = groundHits[i];
                    if (hit.normal.y < 0.65f || hit.collider.GetComponentInParent<ScavengerRobotState>() || hit.collider.GetComponentInParent<FirstPersonController>()) continue;
                    if (hit.distance >= nearest) continue;
                    nearest = hit.distance; selected = i;
                }
                if (selected < 0) continue;
                point = groundHits[selected].point; normal = groundHits[selected].normal; return true;
            }
            point = normal = default; return false;
        }

        public MeteorStrike ScheduleStrike(Vector3 point, Vector3 normal)
        {
            if (!assets || IsSheltered(point + Vector3.up)) return null;
            var host = new GameObject("Meteorite / 5 second impact warning"); host.transform.SetParent(transform, false);
            var strike = host.AddComponent<MeteorStrike>(); strike.Initialize(this, point, normal);
            PendingStrikes++;
            return strike;
        }

        internal void ResolveStrike(Vector3 point, Vector3 normal)
        {
            PendingStrikes = Mathf.Max(0, PendingStrikes - 1); Impacts++;
            foreach (var state in FindObjectsByType<ScavengerRobotState>(FindObjectsSortMode.None))
            {
                float distance = Vector3.Distance(state.transform.position, point);
                if (distance < strikeRadius && !IsSheltered(state.transform.position))
                {
                    state.ApplyEnergyDamage(impactEnergyDamage * (1 - distance / strikeRadius));
                    var link = FindAnyObjectByType<ScavengerLink>();
                    if (link && link.state == state) link.GetComponent<ScavengerFeedHud>()?.Notify("METEOR IMPACT - ENERGY LOST");
                }
            }
            WeatherImpactFx.Spawn(this, point, normal);
            MeteorCameraShake.Kick(point, shakeDistance);
            Impact?.Invoke(point);
        }

        void OnEnable() { if (Application.isPlaying) Active = this; }
        void OnDisable() { if (Active == this) Active = null; }
        void OnDestroy() { if (Active == this) Active = null; }
        [ContextMenu("Weather / Sunny")] void Sunny() => SetWeather(WeatherKind.Sunny);
        [ContextMenu("Weather / Moon Dust")] void Dust() => SetWeather(WeatherKind.MoonDust);
        [ContextMenu("Weather / Radiation")] void Toxic() => SetWeather(WeatherKind.Radiation);
        [ContextMenu("Weather / Meteorite Rain")] void Meteors() => SetWeather(WeatherKind.MeteoriteRain);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetState() => Active = null;
    }
}
