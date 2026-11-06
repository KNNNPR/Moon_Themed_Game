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
        [Min(0)] public float dustDensity = 0.12f;
        [Min(0)] public float toxicDensity = 0.025f;
        [Range(0, 1)] public float toxicOpacity = 0.38f;
        [Min(0.1f)] public float atmosphereTransition = 3;
        [Tooltip("Seconds every weather takes to build up after it starts and to die down before it ends.")]
        [Min(0)] public float atmosphereFadeIn = 8, atmosphereFadeOut = 10;
        [Min(1)] public float toxicEnergyMultiplier = 4;
        [Header("Meteorites")]
        [Min(5)] public float warningSeconds = 5;
        [Min(0.2f)] public float fallSeconds = 1.6f;
        [Tooltip("Meteorite diameter in metres, chosen randomly for each impact.")]
        public Vector2 meteorDiameterRange = new Vector2(4f, 8f);
        public Vector2 meteorInterval = new Vector2(1.3f, 2.6f);
        [Tooltip("Map-wide impact area. Automatically uses the LAND collider when empty.")]
        public Collider meteorMapSurface;
        [Min(1)] public int maximumPendingStrikes=8;
        [Range(1,4)] public int meteorsPerWave=2;
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
        // Keep dust above the retro post-process black threshold so it reads as haze.
        static readonly Color DustTint = new Color(0.22f, 0.235f, 0.25f);
        static readonly Color RadiationTint = new Color(0.17f, 0.43f, 0.08f);
        public Color AtmosphereTint { get; private set; } = DustTint;
        public float OpacityLimit { get; private set; } = 0.96f;
        public int PendingStrikes { get; private set; }
        public int Impacts { get; private set; }
        public event Action<WeatherKind> WeatherChanged;
        public event Action<Vector3> Impact;
        WeatherKind nextOne, nextTwo;
        System.Random random;
        bool firstTail = true;
        float nextMeteor, emissionRemainder, intensityLead;
        ScavengerLink scavenger;
        ParticleSystem particles;
        readonly ParticleSystem.Particle[] liveParticles = new ParticleSystem.Particle[1200];
        readonly RaycastHit[] groundHits = new RaycastHit[64];

        // Camera.main belongs to the operator and is disabled while driving the rover.
        public Camera ViewCamera
        {
            get
            {
                if(!scavenger)scavenger=FindFirstObjectByType<ScavengerLink>();
                if(scavenger && scavenger.Connected && scavenger.robotCamera && scavenger.robotCamera.isActiveAndEnabled)
                    return scavenger.robotCamera;
                var camera=Camera.main;
                return camera && camera.isActiveAndEnabled ? camera : null;
            }
        }

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
            nextMeteor = MeteorInterval();
            CreateParticles();
            SnapAtmosphere();
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
        // 0..1: how far the current weather has built up; it dies down towards the end of its period.
        public float Intensity
        {
            get
            {
                float elapsed = CurrentDuration - Remaining + intensityLead;
                float rise = atmosphereFadeIn > 0 ? Mathf.Clamp01(elapsed / atmosphereFadeIn) : 1;
                float fall = atmosphereFadeOut > 0 ? Mathf.Clamp01(Remaining / atmosphereFadeOut) : 1;
                return Mathf.SmoothStep(0, 1, rise) * Mathf.SmoothStep(0, 1, fall);
            }
        }

        public float EnergyMultiplier(Vector3 position) => Current == WeatherKind.Radiation && !IsSheltered(position) ? 1 + (toxicEnergyMultiplier - 1) * Intensity : 1;

        // Manual/context-menu selection is visible immediately; the forecast queue still fades naturally.
        public void SetWeather(WeatherKind kind, float duration = -1, bool immediate = true)
        {
            if(random==null)random=new System.Random(forecastSeed);
            Current = kind;
            CurrentDuration = Remaining = duration > 0 ? duration : Duration(kind);
            nextOne = (WeatherKind)(((int)kind + 1) % 4);
            nextTwo = (WeatherKind)(((int)kind + 2) % 4);
            firstTail = true;
            intensityLead=immediate?atmosphereFadeIn:0;
            nextMeteor = MeteorInterval();
            if(immediate)SnapAtmosphere();
            WeatherChanged?.Invoke(Current);
        }

        public void SnapAtmosphere()
        {
            Density = (Current == WeatherKind.MoonDust ? dustDensity : Current == WeatherKind.Radiation ? toxicDensity : 0) * Intensity;
            AtmosphereTint = Current == WeatherKind.Radiation ? RadiationTint : DustTint;
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
                intensityLead=0;
                nextMeteor = MeteorInterval();
                WeatherChanged?.Invoke(Current);
            }
        }

        float MeteorInterval()
        {
            float minimum=Mathf.Max(0.4f,Mathf.Min(meteorInterval.x,meteorInterval.y));
            return UnityEngine.Random.Range(minimum,Mathf.Max(minimum,Mathf.Max(meteorInterval.x,meteorInterval.y)));
        }

        public Bounds MeteorMapBounds
        {
            get
            {
                if(!meteorMapSurface)
                    foreach(var root in gameObject.scene.GetRootGameObjects())
                        foreach(var collider in root.GetComponentsInChildren<Collider>())
                            if(collider.enabled && !collider.isTrigger &&
                                (collider.name.Equals("LAND",StringComparison.OrdinalIgnoreCase) || collider is TerrainCollider) &&
                                (!meteorMapSurface || collider.bounds.size.sqrMagnitude>meteorMapSurface.bounds.size.sqrMagnitude))meteorMapSurface=collider;
                return meteorMapSurface?meteorMapSurface.bounds:new Bounds(shelterCenter,new Vector3(600,300,600));
            }
        }

        WeatherKind PickAfter(WeatherKind previous)
        {
            return (WeatherKind)(((int)previous + 1 + random.Next(3)) % 4);
        }

        void Update()
        {
            AdvanceSchedule(Time.deltaTime);
            float target = (Current == WeatherKind.MoonDust ? dustDensity : Current == WeatherKind.Radiation ? toxicDensity : 0) * Intensity;
            float blend = 1 - Mathf.Exp(-Time.deltaTime * 4 / Mathf.Max(0.1f, atmosphereTransition));
            Density = Mathf.Lerp(Density, target, blend);
            AtmosphereTint = Color.Lerp(AtmosphereTint, Current == WeatherKind.Radiation ? RadiationTint : DustTint, blend);
            OpacityLimit = Mathf.Lerp(OpacityLimit, Current == WeatherKind.Radiation ? toxicOpacity : 0.96f, blend);
            UpdateParticles();
            if (Current == WeatherKind.MeteoriteRain && assets && assets.meteoritePrefab)
            {
                // Sparse first strikes while the shower builds up, sparse again as it passes.
                nextMeteor -= Time.deltaTime * Mathf.Max(0.4f, Intensity);
                if (nextMeteor <= 0)
                {
                    nextMeteor = MeteorInterval();
                    for(int i=0;i<meteorsPerWave && PendingStrikes<maximumPendingStrikes;i++)
                        if(TryFindStrike(out var point,out var normal))ScheduleStrike(point,normal);
                }
            }
        }

        void CreateParticles()
        {
            if (!assets || !assets.particleMaterial) return;
            var host = new GameObject("Exterior moon dust / haze"); host.transform.SetParent(transform, false);
            particles = host.AddComponent<ParticleSystem>(); particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main; main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
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
            emissionRemainder += Time.deltaTime * (dust ? 45 : 12) * Intensity;
            var camera = ViewCamera;
            var center = camera && !IsSheltered(camera.transform.position) ? camera.transform.position : shelterCenter;
            particles.transform.position=center;
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
            var camera=ViewCamera;
            var state=scavenger && scavenger.Connected ? scavenger.state : null;
            var focus=state ? state.transform.position : camera ? camera.transform.position : shelterCenter;
            var center = !IsSheltered(focus) ? focus : shelterCenter;
            var map=MeteorMapBounds;
            bool nearby=UnityEngine.Random.value<nearbyRobotStrikeChance;
            for (int attempt = 0; attempt < 32; attempt++)
            {
                var direction = UnityEngine.Random.insideUnitCircle.normalized;
                float radius = attempt == 0 && state && UnityEngine.Random.value < nearbyRobotStrikeChance
                    ? UnityEngine.Random.Range(0, 2.5f) : UnityEngine.Random.Range(strikeDistance.x, strikeDistance.y);
                var candidate = nearby ? center + new Vector3(direction.x, 0, direction.y) * radius
                    : new Vector3(UnityEngine.Random.Range(map.min.x,map.max.x),map.center.y,UnityEngine.Random.Range(map.min.z,map.max.z));
                if (IsBaseFootprint(candidate, 3)) continue;
                float top=Mathf.Max(map.max.y,candidate.y)+100;
                int count = Physics.RaycastNonAlloc(new Vector3(candidate.x,top,candidate.z),Vector3.down,groundHits,Mathf.Max(500,top-map.min.y+100),groundLayers,QueryTriggerInteraction.Ignore);
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

        internal void ResolveStrike(Vector3 point, Vector3 normal,float diameter=3)
        {
            PendingStrikes = Mathf.Max(0, PendingStrikes - 1); Impacts++;
            foreach (var state in FindObjectsByType<ScavengerRobotState>(FindObjectsSortMode.None))
            {
                float distance = Vector3.Distance(state.transform.position, point);
                if (distance < strikeRadius && !IsSheltered(state.transform.position))
                {
                    state.ApplyEnergyDamage(impactEnergyDamage * (1 - distance / strikeRadius));
                    var link = FindAnyObjectByType<ScavengerLink>();
                    if (link && link.state == state) link.GetComponent<ScavengerFeedHud>()?.Notify(Loc.T("METEOR IMPACT - ENERGY LOST"));
                }
            }
            WeatherImpactFx.Spawn(this, point, normal,diameter);
            MeteorCameraShake.Kick(point, shakeDistance);
            Impact?.Invoke(point);
        }

        void OnEnable() { if (Application.isPlaying && !MoonThemedGame.Computer.MainMenuController.IsMenuScene) Active = this; }
        void OnDisable() { if (Active == this) Active = null; }
        void OnDestroy() { if (Active == this) Active = null; }
        [ContextMenu("Weather / Sunny")] void Sunny() => SetWeather(WeatherKind.Sunny);
        [ContextMenu("Weather / Moon Dust")] void Dust() => SetWeather(WeatherKind.MoonDust);
        [ContextMenu("Weather / Radiation")] void Toxic() => SetWeather(WeatherKind.Radiation);
        [ContextMenu("Weather / Meteorite Rain")] void Meteors() => SetWeather(WeatherKind.MeteoriteRain);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetState() => Active = null;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RestoreActive()
        {
            if(MoonThemedGame.Computer.MainMenuController.IsMenuScene){Active=null;return;}
            // Also runs when entering Play with domain/scene reload disabled.
            if(!Active)
            {
                var weather=FindFirstObjectByType<MoonWeatherSystem>();
                if(weather && weather.isActiveAndEnabled)Active=weather;
            }
        }
    }
}
