using UnityEngine;

namespace MoonThemedGame.Weather
{
    public sealed class MeteorStrike : MonoBehaviour
    {
        public const float ReferenceDiameter = 0.85f;
        MoonWeatherSystem weather;
        Vector3 point, normal, start;
        float elapsed, warning;
        GameObject meteor;
        MeshRenderer marker;
        MaterialPropertyBlock properties;
        bool resolved;
        public float Remaining => Mathf.Max(0, warning - elapsed);
        public Vector3 Target => point;
        public bool HasFallen => resolved;
        public float Diameter { get; private set; }
        void Awake() => properties = new MaterialPropertyBlock();

        public void Initialize(MoonWeatherSystem system, Vector3 target, Vector3 surfaceNormal)
        {
            weather = system; point = target; normal = surfaceNormal;
            warning = Mathf.Max(5, system.warningSeconds);
            float minimum = Mathf.Max(0.1f, Mathf.Min(system.meteorDiameterRange.x, system.meteorDiameterRange.y));
            float maximum = Mathf.Max(minimum, Mathf.Max(system.meteorDiameterRange.x, system.meteorDiameterRange.y));
            Diameter = UnityEngine.Random.Range(minimum, maximum);
            start = point + Vector3.up * 60 + new Vector3(-24, 0, 16);
            transform.SetPositionAndRotation(point, Quaternion.FromToRotation(Vector3.up, normal));
            marker = WeatherImpactFx.MakeRing(transform, system.assets.warningMaterial, Mathf.Max(system.strikeRadius * 2.3f,Diameter*1.25f));
            marker.transform.localPosition = Vector3.up * 0.035f;
            UpdateMarker();
        }

        void Update() => Advance(Time.deltaTime);

        public void Advance(float seconds)
        {
            if (resolved || !weather) return;
            elapsed += Mathf.Max(0, seconds);
            float flight = Mathf.Min(weather.fallSeconds, warning);
            if (Remaining <= flight && !meteor && weather.assets.meteoritePrefab)
            {
                meteor = Instantiate(weather.assets.meteoritePrefab, start, UnityEngine.Random.rotation, transform);
                float scale = Diameter / ReferenceDiameter;
                meteor.transform.localScale *= scale;
                foreach (var trail in meteor.GetComponentsInChildren<TrailRenderer>()) trail.widthMultiplier *= scale;
                AddBurningTrail();
            }
            if (meteor)
            {
                float t = 1 - Remaining / Mathf.Max(0.01f, flight);
                meteor.transform.position = Vector3.Lerp(start, point + normal * (Diameter * 0.5f), t * t);
                meteor.transform.Rotate(160 * seconds, 65 * seconds, 30 * seconds, Space.Self);
            }
            UpdateMarker();
            if (Remaining > 0) return;
            resolved = true;
            weather.ResolveStrike(point, normal,Diameter);
            if(meteor)
                foreach(var particles in meteor.GetComponentsInChildren<ParticleSystem>())
                {
                    particles.transform.SetParent(weather.transform,true);
                    particles.Stop(true,ParticleSystemStopBehavior.StopEmitting);
                    Destroy(particles.gameObject,4);
                }
            Destroy(gameObject);
        }

        void AddBurningTrail()
        {
            var flames=WeatherImpactFx.MakeParticles(meteor.transform,"Meteor flames",weather.assets.particleMaterial,.5f,Diameter*.6f,160,true);
            var main=flames.main;main.startColor=new Color(3,1,.1f,1);
            var emission=flames.emission;emission.enabled=true;emission.rateOverTime=90;
            var shape=flames.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=.25f;
            // Emitter is unscaled: the FBX model's authored scale must not enlarge its flames.
            flames.transform.SetParent(weather.transform,true);flames.transform.localScale=Vector3.one;flames.transform.SetParent(meteor.transform,true);
            main=flames.main;main.scalingMode=ParticleSystemScalingMode.Shape;
            var smoke=WeatherImpactFx.MakeParticles(flames.transform,"Meteor smoke trail",weather.assets.particleMaterial,2,Diameter*.45f,140,true);
            main=smoke.main;main.startColor=new Color(.18f,.16f,.14f,.75f);main.scalingMode=ParticleSystemScalingMode.Shape;
            emission=smoke.emission;emission.enabled=true;emission.rateOverTime=40;
            var light=new GameObject("Meteor fire light").AddComponent<Light>();light.transform.SetParent(flames.transform,false);light.color=new Color(1,.4f,.08f);light.intensity=4;light.range=Diameter*4;light.shadows=LightShadows.None;
        }

        void UpdateMarker()
        {
            if (!marker) return;
            properties.SetFloat("_Countdown", Remaining / warning);
            properties.SetFloat("_Opacity", 0.75f);
            marker.SetPropertyBlock(properties);
        }
    }
}
