using UnityEngine;
using UnityEngine.Rendering;

namespace MoonThemedGame.Weather
{
    public sealed class WeatherImpactFx : MonoBehaviour
    {
        static Mesh quad;
        MeshRenderer ring;
        Light flash;
        float age;
        MaterialPropertyBlock properties;
        void Awake() => properties = new MaterialPropertyBlock();
        public static Mesh Quad
        {
            get
            {
                if (quad) return quad;
                quad = new Mesh { name = "Weather ground marker", hideFlags = HideFlags.HideAndDontSave,
                    vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) },
                    uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up }, triangles = new[] { 0, 2, 1, 0, 3, 2 } };
                quad.RecalculateNormals(); quad.RecalculateBounds(); return quad;
            }
        }

        public static MeshRenderer MakeRing(Transform parent, Material material, float size)
        {
            var host = new GameObject("Impact area"); host.transform.SetParent(parent, false); host.transform.localScale = Vector3.one * size;
            host.AddComponent<MeshFilter>().sharedMesh = Quad;
            var renderer = host.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return renderer;
        }

        public static void Spawn(MoonWeatherSystem weather, Vector3 point, Vector3 normal)
        {
            var host = new GameObject("Meteorite impact / dust burst"); host.transform.SetParent(weather.transform, false);
            host.transform.SetPositionAndRotation(point + normal * 0.04f, Quaternion.FromToRotation(Vector3.up, normal));
            var fx = host.AddComponent<WeatherImpactFx>();
            fx.ring = MakeRing(host.transform, weather.assets.warningMaterial, 0.4f);
            var lightHost = new GameObject("Short impact flash"); lightHost.transform.SetParent(host.transform, false); lightHost.transform.localPosition = Vector3.up * 0.6f;
            fx.flash = lightHost.AddComponent<Light>(); fx.flash.color = new Color(1, 0.46f, 0.17f); fx.flash.range = 6; fx.flash.intensity = 1.8f; fx.flash.shadows = LightShadows.None;
            var dust = host.AddComponent<ParticleSystem>(); dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = dust.main; main.loop = false; main.playOnAwake = false; main.startLifetime = 1.5f; main.startSpeed = 0;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = 0.12f; main.maxParticles = 80;
            var emission = dust.emission; emission.enabled = false;
            var shape = dust.shape; shape.enabled = false;
            var color = dust.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) }); color.color = gradient;
            dust.GetComponent<ParticleSystemRenderer>().sharedMaterial = weather.assets.particleMaterial;
            dust.Play();
            for (int i = 0; i < 48; i++)
            {
                bool spark = i < 12;
                var velocity = UnityEngine.Random.insideUnitSphere; velocity.y = Mathf.Abs(velocity.y) + 0.25f;
                dust.Emit(new ParticleSystem.EmitParams { position = point + normal * 0.2f,
                    velocity = velocity * (spark ? 3.5f : 1.8f), startLifetime = spark ? 0.45f : 1.7f,
                    startSize = spark ? 0.07f : UnityEngine.Random.Range(0.3f, 0.8f),
                    startColor = spark ? new Color(1.8f, 0.55f, 0.15f, 1) : new Color(0.5f, 0.43f, 0.32f, 0.55f) }, 1);
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            ring.transform.localScale = Vector3.one * Mathf.Lerp(0.4f, 7, Mathf.Clamp01(age / 0.65f));
            properties.SetFloat("_Impact", 1); properties.SetFloat("_Opacity", Mathf.Clamp01(1 - age / 0.65f) * 0.6f); ring.SetPropertyBlock(properties);
            if (flash) flash.intensity = Mathf.Max(0, 1.8f * (1 - age / 0.18f));
            if (age > 2.2f) Destroy(gameObject);
        }
    }
}
