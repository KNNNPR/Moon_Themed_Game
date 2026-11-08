using MoonThemedGame.Audio;
using System;
using System.Collections.Generic;
using MoonThemedGame.Scavenger;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoonThemedGame.Weather
{
    public enum WeatherKind { Sunny, MoonDust, Radiation, MeteoriteRain, DeepNight, CryogenicFront, AshenCalm, Horde }
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
        [Min(10)] public float nightDuration=100, cryogenicDuration=80, ashDuration=65;
        [Header("The Horde (unlocked by clearing Crazy Cosmonauts wave 2)")]
        [Min(10)] public float hordeDuration=90;
        [Range(1,24)] public int maximumHordeZombies=12;
        [Min(.5f)] public float hordeSpawnInterval=2.5f;
        [Min(0)] public float hordeContactDamage=8;
        [Min(.1f)] public float hordeRamSpeed=5;
        public bool HordeUnlocked {get;private set;}
        HordeWeatherEvent horde;
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
        [Min(1)] public float coldEnergyMultiplier=2;
        [Range(.1f,1)] public float coldGripMultiplier=.45f;
        [Header("Camera frost (seconds)")]
        [Min(.1f)] public float frostBuildSeconds=14, frostMeltSeconds=10;
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
        static readonly Color HordeTint = new Color(.95f,.17f,.035f);
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
        static readonly int KindCount=Enum.GetValues(typeof(WeatherKind)).Length;
        readonly Vector4[] lightingMins=new Vector4[4],lightingMaxs=new Vector4[4];
        sealed class FrostCamera { public Camera camera;public float amount; }
        readonly List<FrostCamera> frostCameras=new List<FrostCamera>();
        public float NightIntensity=>Current==WeatherKind.DeepNight?Intensity:0;
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
            WeatherKind.MoonDust => "FOG [MOON DUST]", WeatherKind.Radiation => "RADIATION",
            WeatherKind.MeteoriteRain => "METEORITE RAIN", WeatherKind.DeepNight=>"DEEP NIGHT",
            WeatherKind.CryogenicFront=>"CRYOGENIC FRONT",WeatherKind.AshenCalm=>"ASHEN CALM",WeatherKind.Horde=>"THE HORDE", _ => "SUNNY"
        };
        public static string Effect(WeatherKind kind) => kind switch
        {
            WeatherKind.MoonDust => "Low Visibility",
            WeatherKind.Radiation => "Drains your energy faster",
            WeatherKind.MeteoriteRain => "Meteorites drop from the sky,\nwhen hit - drains your energy",
            WeatherKind.DeepNight=>"Almost total darkness. Use your headlights.",
            WeatherKind.CryogenicFront=>"Faster battery drain. Reduced grip. Frost on the camera.",
            WeatherKind.AshenCalm=>"Slowly falling ash. No penalties.",
            WeatherKind.Horde=>"Zombies drain energy on contact. Ram them at high speed.",
            _ => "Perfect Weather"
        };

        void Awake()
        {
            Active = this;
            random = new System.Random(forecastSeed);
            Current = startingWeather;
            CurrentDuration = Remaining = Duration(Current);
            nextOne = Following(Current);
            nextTwo = Following(nextOne);
            nextMeteor = MeteorInterval();
            CreateParticles();
            SnapAtmosphere();
            horde=gameObject.AddComponent<HordeWeatherEvent>();horde.Initialize(this);
        }

        public float Duration(WeatherKind kind) => Mathf.Max(1, kind switch
        {
            WeatherKind.MoonDust => dustDuration, WeatherKind.Radiation => toxicityDuration,
            WeatherKind.MeteoriteRain => meteorDuration,WeatherKind.DeepNight=>nightDuration,
            WeatherKind.CryogenicFront=>cryogenicDuration,WeatherKind.AshenCalm=>ashDuration,WeatherKind.Horde=>hordeDuration, _ => sunnyDuration
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

        public float EnergyMultiplier(Vector3 position) => IsSheltered(position)?1:Mathf.Lerp(1,
            Current==WeatherKind.Radiation?toxicEnergyMultiplier:Current==WeatherKind.CryogenicFront?coldEnergyMultiplier:1,Intensity);
        public float GripMultiplier(Vector3 position)=>Current==WeatherKind.CryogenicFront && !IsSheltered(position)?Mathf.Lerp(1,coldGripMultiplier,Intensity):1;
        bool IsRobotCamera(Camera camera)
        {
            if(!scavenger)scavenger=FindFirstObjectByType<ScavengerLink>();
            return scavenger && camera && camera==scavenger.robotCamera;
        }
        public Vector4 CameraEffects(Camera camera)
        {
            // Lens frost and its cold tint belong only to the rover feed, never the human view.
            if(!IsRobotCamera(camera))return new Vector4(NightIntensity,0,Time.time,0);
            var frost=frostCameras.Find(f=>f.camera==camera);
            if(frost==null){frost=new FrostCamera{camera=camera};frostCameras.Add(frost);}
            return new Vector4(NightIntensity,frost.amount,Time.time,0);
        }
        void UpdateFrost(float dt)
        {
            for(int i=frostCameras.Count-1;i>=0;i--)
            {
                var frost=frostCameras[i];if(!IsRobotCamera(frost.camera)){frostCameras.RemoveAt(i);continue;}
                float target=Current==WeatherKind.CryogenicFront && !IsSheltered(frost.camera.transform.position)?Intensity:0;
                float duration=target>frost.amount?frostBuildSeconds:frostMeltSeconds;
                frost.amount=Mathf.MoveTowards(frost.amount,target,Mathf.Max(0,dt)/Mathf.Max(.1f,duration));
            }
        }
        WeatherKind Following(WeatherKind kind)
        {
            int count=HordeUnlocked?KindCount:KindCount-1;
            if((int)kind>=count)return WeatherKind.Sunny;
            return (WeatherKind)(((int)kind+1)%count);
        }
        public void RecordCosmonautsWaveCleared(int wave)
        {
            if(wave<2||HordeUnlocked||gameObject.scene.name!="Operator")return;
            HordeUnlocked=true;
            SetWeather(WeatherKind.Horde,immediate:false);
        }
        void PublishLighting()
        {
            Shader.SetGlobalFloat("_MoonDeepNight",NightIntensity);
            for(int i=0;i<ShelterCount;i++){lightingMins[i]=GetShelter(i).min;lightingMaxs[i]=GetShelter(i).max;}
            Shader.SetGlobalInt("_MoonWeatherShelterCount",ShelterCount);
            Shader.SetGlobalVectorArray("_MoonWeatherShelterMins",lightingMins);Shader.SetGlobalVectorArray("_MoonWeatherShelterMaxs",lightingMaxs);
        }

        // Manual/context-menu selection is visible immediately; the forecast queue still fades naturally.
        public void SetWeather(WeatherKind kind, float duration = -1, bool immediate = true)
        {
            if(random==null)random=new System.Random(forecastSeed);
            Current = kind;
            CurrentDuration = Remaining = duration > 0 ? duration : Duration(kind);
            nextOne = Following(kind);
            nextTwo = Following(nextOne);
            firstTail = true;
            intensityLead=immediate?atmosphereFadeIn:0;
            nextMeteor = MeteorInterval();
            if(immediate)SnapAtmosphere();
            WeatherChanged?.Invoke(Current);
        }

        public void SnapAtmosphere()
        {
            Density = TargetDensity * Intensity;
            AtmosphereTint = TargetTint;
            OpacityLimit = TargetOpacity;
            PublishLighting();
        }

        public void AdvanceSchedule(float seconds)
        {
            Remaining -= Mathf.Max(0, seconds);
            while (Remaining <= 0)
            {
                Current = nextOne; nextOne = nextTwo;
                nextTwo = firstTail ? Following(nextOne) : PickAfter(nextOne);
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
            int count=HordeUnlocked?KindCount:KindCount-1;
            return (WeatherKind)(((int)previous + 1 + random.Next(count-1)) % count);
        }
        float TargetDensity=>Current==WeatherKind.MoonDust?dustDensity:Current==WeatherKind.Radiation?toxicDensity:Current==WeatherKind.Horde?.012f:0;
        Color TargetTint=>Current==WeatherKind.Radiation?RadiationTint:Current==WeatherKind.Horde?HordeTint:DustTint;
        float TargetOpacity=>Current==WeatherKind.Radiation?toxicOpacity:Current==WeatherKind.Horde?.42f:.96f;

        void Update()
        {
            AdvanceSchedule(Time.deltaTime);
            UpdateFrost(Time.deltaTime);
            float target = TargetDensity * Intensity;
            float blend = 1 - Mathf.Exp(-Time.deltaTime * 4 / Mathf.Max(0.1f, atmosphereTransition));
            Density = Mathf.Lerp(Density, target, blend);
            AtmosphereTint = Color.Lerp(AtmosphereTint, TargetTint, blend);
            OpacityLimit = Mathf.Lerp(OpacityLimit, TargetOpacity, blend);
            PublishLighting();
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
            bool dust = Current == WeatherKind.MoonDust, toxic = Current == WeatherKind.Radiation, ash=Current==WeatherKind.AshenCalm;
            if (!dust && !toxic && !ash) return;
            emissionRemainder += Time.deltaTime * (dust ? 45 : ash?18:12) * Intensity;
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
                    position = point, velocity = ash?new Vector3(.025f,-.14f,.015f):new Vector3(dust ? 1.3f : 0.3f, 0.04f, 0.3f),
                    startSize = dust || ash ? UnityEngine.Random.Range(0.035f, ash ? .085f : .13f) : UnityEngine.Random.Range(1.3f, 3f),
                    startColor = ash?new Color(.58f,.61f,.64f,.36f):dust ? new Color(0.7f, 0.65f, 0.55f, 0.3f) : new Color(0.3f, 0.6f, 0.15f, 0.055f),
                    startLifetime = ash?12:7
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
            GameAudio.At(Sound.Meteor,point);WeatherImpactFx.Spawn(this, point, normal,diameter);
            MeteorCameraShake.Kick(point, shakeDistance);
            Impact?.Invoke(point);
        }

        void OnEnable() { if (Application.isPlaying && !MoonThemedGame.Computer.MainMenuController.IsMenuScene) Active = this; }
        void OnDisable() { frostCameras.Clear();if (Active == this){Active = null;Shader.SetGlobalFloat("_MoonDeepNight",0);Shader.SetGlobalVector("_MoonWeatherCamera",Vector4.zero);} }
        void OnDestroy() { if (Active == this) Active = null; }
        [ContextMenu("Weather / Sunny")] void Sunny() => SetWeather(WeatherKind.Sunny);
        [ContextMenu("Weather / Moon Dust")] void Dust() => SetWeather(WeatherKind.MoonDust);
        [ContextMenu("Weather / Radiation")] void Toxic() => SetWeather(WeatherKind.Radiation);
        [ContextMenu("Weather / Meteorite Rain")] void Meteors() => SetWeather(WeatherKind.MeteoriteRain);
        [ContextMenu("Weather / Deep Night")] void Night()=>SetWeather(WeatherKind.DeepNight);
        [ContextMenu("Weather / Cryogenic Front")] void Cold()=>SetWeather(WeatherKind.CryogenicFront);
        [ContextMenu("Weather / Ashen Calm")] void Ash()=>SetWeather(WeatherKind.AshenCalm);
        [ContextMenu("Weather / The Horde")] void Horde()=>SetWeather(WeatherKind.Horde);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetState(){Active = null;Shader.SetGlobalFloat("_MoonDeepNight",0);Shader.SetGlobalVector("_MoonWeatherCamera",Vector4.zero);}
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
