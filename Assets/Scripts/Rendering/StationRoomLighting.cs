using UnityEngine;

namespace MoonThemedGame.Rendering
{
    // This volume affects surfaces in the operator room, not the outside palette.
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class StationRoomLighting : MonoBehaviour
    {
        public Vector3 roomCenter = new Vector3(-1.5f, 6.8f, 1.85f);
        public Vector3 roomSize = new Vector3(10.0f, 9.0f, 12.9f);
        [Min(.01f)] public float edgeBlend = .75f;
        [Range(0, 1)] public float daylight = .16f;
        [Range(0, 1)] public float ambient = .75f;
        public Color shadowBounce = new Color(.16f, .18f, .21f);
        public Light workLight;
        public Light coldAccent;
        [Range(0, .1f)] public float powerVariation = .025f;
        [Range(0, 3)] public float accentDrift = .6f;
        [Min(0)] public float ceilingIntensity = 8.5f;
        public Color ceilingColor = new Color(.88f, .89f, .82f);
        Light ceiling;
        Renderer ceilingGlass;
        float oldIntensity, workIntensity, coldIntensity;
        Color oldColor;
        Quaternion coldRotation;
        MaterialPropertyBlock originalGlass, dimGlass;
        bool bound;
        static StationRoomLighting owner;
        static readonly int MinId = Shader.PropertyToID("_StationRoomMin");
        static readonly int MaxId = Shader.PropertyToID("_StationRoomMax");
        static readonly int SettingsId = Shader.PropertyToID("_StationRoomLighting");
        static readonly int BounceId = Shader.PropertyToID("_StationRoomBounce");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetGlobals() { owner = null; Shader.SetGlobalVector(SettingsId, Vector4.zero); }

        void OnEnable()
        {
            // OnEnable runs before scene.isLoaded becomes true during a game load.
            if (!gameObject.scene.IsValid()) return;
            bound = true;
            owner = this;
            if (workLight) workIntensity = workLight.intensity;
            if (coldAccent) { coldIntensity = coldAccent.intensity; coldRotation = coldAccent.transform.localRotation; }
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.gameObject.scene == gameObject.scene && light.name == "Point" &&
                    Vector3.Distance(light.transform.position, new Vector3(.25f, 9.3f, 2.64f)) < .5f)
                { ceiling = light; oldIntensity = light.intensity; oldColor = light.color; break; }
            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (renderer.gameObject.scene == gameObject.scene && renderer.name == "lamp" &&
                    Vector3.Distance(renderer.bounds.center, new Vector3(.06f, 9.72f, 2.95f)) < .5f)
                { ceilingGlass = renderer; break; }
            if (ceilingGlass)
            {
                originalGlass = new MaterialPropertyBlock(); ceilingGlass.GetPropertyBlock(originalGlass);
                dimGlass = new MaterialPropertyBlock(); ceilingGlass.GetPropertyBlock(dimGlass);
                dimGlass.SetColor("_EmissionColor", new Color(.24f, .18f, .11f));
                ceilingGlass.SetPropertyBlock(dimGlass);
            }
            foreach (var source in GetComponentsInChildren<StationLampSource>())
                if (source.ceilingFixture) source.glass = ceilingGlass;
            Publish();
        }

        void OnValidate() { if (isActiveAndEnabled && owner == this) Publish(); }
        void Update()
        {
            if (owner != this) return;
            Publish();
            if (!Application.isPlaying) return;
            float t = Time.time;
            // Gentle supply drift instead of rapid flicker over the dithering.
            float supply = 1 - powerVariation * (.5f + .5f * Mathf.Sin(t * .31f) * Mathf.Sin(t * .13f));
            if (workLight) workLight.intensity = workIntensity * supply;
            if (coldAccent)
            {
                coldAccent.intensity = coldIntensity * (1 - .018f * Mathf.Sin(t * .23f));
                coldAccent.transform.localRotation = coldRotation * Quaternion.Euler(0, Mathf.Sin(t * .12f) * accentDrift, 0);
            }
        }
        void Publish()
        {
            Vector3 center = transform.TransformPoint(roomCenter);
            Vector3 size = Vector3.Scale(roomSize, transform.lossyScale);
            Shader.SetGlobalVector(MinId, center - size * .5f);
            Shader.SetGlobalVector(MaxId, center + size * .5f);
            Shader.SetGlobalVector(SettingsId, new Vector4(daylight, ambient, edgeBlend, 1));
            Shader.SetGlobalColor(BounceId, shadowBounce);
            if (ceiling) { ceiling.intensity = ceilingIntensity; ceiling.color = ceilingColor; }
        }
        void OnDisable()
        {
            if (!bound) return;
            bound = false;
            if (owner == this) { Shader.SetGlobalVector(SettingsId, Vector4.zero); owner = null; }
            if (ceiling) { ceiling.intensity = oldIntensity; ceiling.color = oldColor; }
            if (ceilingGlass && originalGlass != null) ceilingGlass.SetPropertyBlock(originalGlass);
            if (workLight) workLight.intensity = workIntensity;
            if (coldAccent) { coldAccent.intensity = coldIntensity; coldAccent.transform.localRotation = coldRotation; }
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(.45f, .7f, .85f, .65f);
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.DrawWireCube(transform.TransformPoint(roomCenter), Vector3.Scale(roomSize, transform.lossyScale));
        }
    }
}
