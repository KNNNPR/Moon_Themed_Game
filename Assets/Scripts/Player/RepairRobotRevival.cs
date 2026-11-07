using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    public enum RepairRevivalStyle { Scavenger, Finder, Skate }

    // Silent, cosmetic self-tests. Slot transforms, fasteners and physics stay authoritative.
    [DefaultExecutionOrder(150), DisallowMultipleComponent]
    public sealed class RepairRobotRevival : MonoBehaviour
    {
        [Min(.1f)] public float wheelCheckDuration = 1;
        [Range(0, 60)] public float wheelCheckAngle = 32;
        [Min(.05f)] public float sequenceGap = .16f;
        [Min(0)] public float idleLampGlow = .65f;
        [Min(0)] public float peakLampGlow = 2.6f;
        public bool Powered { get; private set; }
        public bool IsTesting => inspectionPhase != RepairDiagnosticPhase.None || current != null || queue.Count > 0;
        public RepairRevivalStyle Style { get; private set; }
        public int WheelChecksCompleted { get; private set; }
        public int LampChecksCompleted { get; private set; }
        public int SynchronizedChecksCompleted { get; private set; }

        enum Kind { Battery, Wheel, Lamp }
        sealed class Part
        {
            public GameObject visual;
            public Kind kind;
            public bool fitted, secure;
            public int revision;
            public float insertion = -10;
            public Transform indicator;
            public Renderer indicatorRenderer;
            public Vector3 indicatorAnchor;
            public MaterialPropertyBlock indicatorBlock;
        }
        sealed class Channel
        {
            public Part part;
            public MeshFilter mesh;
            public Bounds lens;
            public Light beam;
            public float brightness;
            public int material;
            public Renderer renderer;
            public MaterialPropertyBlock original, block;
            public bool lensMaterial;
            public float emissionScale = 1;
        }
        sealed class Reaction { public Part part; public int revision; public bool synchronized; }
        sealed class WheelVisual
        {
            public Part part;
            public MeshFilter source;
            public Renderer original;
            public bool wasEnabled;
            public Transform proxy;
            public Vector3 axle;
            public bool longModule;
        }

        readonly List<Part> parts = new List<Part>();
        readonly List<Channel> channels = new List<Channel>();
        readonly Queue<Reaction> queue = new Queue<Reaction>();
        readonly List<WheelVisual> wheels = new List<WheelVisual>();
        RepairRobotTarget owner;
        RepairRobotPersonality personality;
        GameObject effects;
        Material indicatorMaterial;
        Reaction current;
        Vector3 forward;
        float length, timer, gap;
        bool configured, allWheelsWereSecure;
        RepairDiagnosticPhase inspectionPhase;
        float inspectionProgress;
        bool inspectionGearStarted;
        static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        internal void Configure(RepairRobotTarget robot, IReadOnlyList<RepairRobotTarget.Slot> slots)
        {
            Shutdown(); owner = robot;
            string name = robot.Definition ? robot.Definition.DisplayName : robot.name;
            Style = name.IndexOf("finder", StringComparison.OrdinalIgnoreCase) >= 0 ? RepairRevivalStyle.Finder :
                name.IndexOf("skate", StringComparison.OrdinalIgnoreCase) >= 0 ? RepairRevivalStyle.Skate : RepairRevivalStyle.Scavenger;
            var whole = RepairPartMount.GeometryBounds(transform);
            length = Mathf.Max(.2f, whole.size.magnitude * .65f);
            Vector3 lampsCenter = Vector3.zero; int lampCount = 0;
            foreach (var slot in slots)
            {
                string label = slot.visual.name.ToLowerInvariant();
                Kind kind;
                if (label.Contains("battery")) kind = Kind.Battery;
                else if (label.Contains("wheel")) kind = Kind.Wheel;
                else if (label.Contains("headlight")) kind = Kind.Lamp;
                else continue;
                var part = new Part { visual = slot.visual, kind = kind }; parts.Add(part);
                if (kind == Kind.Lamp) { lampsCenter += RepairPartMount.GeometryBounds(slot.visual.transform).center; lampCount++; }
            }
            forward = lampCount > 0 ? lampsCenter / lampCount - whole.center : transform.forward;
            forward.y = 0; forward = forward.sqrMagnitude > .0001f ? forward.normalized : transform.forward;
            // Store in the robot frame so beams follow the crane as well as the repair stand.
            forward = transform.InverseTransformDirection(forward).normalized;
            effects = new GameObject("Repair self-test effects"); effects.transform.SetParent(transform, false);
            effects.layer = gameObject.layer;
            foreach (var part in parts)
                if (part.kind == Kind.Battery) CreateIndicator(part, whole.center);
                else if (part.kind == Kind.Lamp) CreateChannels(part);
            configured = true;
            WheelChecksCompleted = LampChecksCompleted = SynchronizedChecksCompleted = 0;
            personality=GetComponent<RepairRobotPersonality>();
            if(!personality)personality=gameObject.AddComponent<RepairRobotPersonality>();
            personality.Configure(robot,this,slots);
        }

        internal bool IsEffect(Transform source)=>effects&&source.IsChildOf(effects.transform);

        void CreateIndicator(Part part, Vector3 robotCenter)
        {
            if (!indicatorMaterial)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (!shader) return;
                indicatorMaterial = new Material(shader) { name = "Repair / power indicator", hideFlags = HideFlags.DontSave };
            }
            var geometry = RepairPartMount.GeometryBounds(part.visual.transform);
            Vector3 normal = geometry.center - robotCenter; normal.y = 0;
            normal = normal.sqrMagnitude > .001f ? normal.normalized : transform.TransformDirection(forward);
            float extent = Vector3.Dot(geometry.extents, Abs(normal));
            var position = geometry.center + normal * (extent + length * .003f) + Vector3.up * geometry.extents.y * .45f;
            // Mount the LED on an actual triangle, including curved/angled battery models.
            position = BatterySurface(part.visual.transform, geometry, normal, position);
            var light = GameObject.CreatePrimitive(PrimitiveType.Sphere); light.name = "Battery / power LED";
            var collider = light.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
            light.transform.SetParent(effects.transform, false); light.layer = part.visual.layer;
            part.indicator = light.transform; part.indicatorAnchor = part.visual.transform.InverseTransformPoint(position);
            float diameter = length * .012f;
            light.transform.localScale = Divide(Vector3.one * diameter, effects.transform.lossyScale);
            part.indicatorRenderer = light.GetComponent<Renderer>(); part.indicatorRenderer.sharedMaterial = indicatorMaterial;
            part.indicatorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            part.indicatorBlock = new MaterialPropertyBlock(); light.SetActive(false);
        }

        void CreateChannels(Part part)
        {
            var meshes = part.visual.GetComponentsInChildren<MeshFilter>(true);
            bool hasEmissiveLens = false;
            foreach (var mesh in meshes)
                if (mesh.TryGetComponent<Renderer>(out var renderer))
                    foreach (var material in renderer.sharedMaterials)
                        if (material && material.HasProperty(Emission) && material.GetColor(Emission).maxColorComponent > .1f) hasEmissiveLens = true;
            foreach (var mesh in meshes)
            {
                if (!mesh.sharedMesh || !mesh.TryGetComponent<Renderer>(out var renderer)) continue;
                var materials = renderer.sharedMaterials; int selected = -1; float best = -1;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i]; if (!material || !material.HasProperty(Emission)) continue;
                    float emission = material.GetColor(Emission).maxColorComponent;
                    if (hasEmissiveLens && emission <= .1f) continue;
                    var texture = material.HasProperty(BaseColor) && material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
                    string textureName = texture ? texture.name.ToLowerInvariant() : "";
                    float score = emission > .1f ? 100 + emission : textureName.Contains("comp2") ? 20 : textureName == "robot" ? 10 : 1;
                    if (score > best) { best = score; selected = i; }
                }
                for (int i = 0; i < materials.Length; i++)
                {
                    if (!materials[i] || !materials[i].HasProperty(Emission)) continue;
                    var channel = new Channel { part = part, mesh = mesh, renderer = renderer, material = i, lensMaterial = i == selected,
                        original = new MaterialPropertyBlock(), block = new MaterialPropertyBlock(),
                        emissionScale = Mathf.Max(1, Mathf.Sqrt(materials[i].GetColor(Emission).maxColorComponent)) };
                    renderer.GetPropertyBlock(channel.original, i); renderer.GetPropertyBlock(channel.block, i);
                    channel.block.SetColor(Emission, Color.black); renderer.SetPropertyBlock(channel.block, i);
                    if (channel.lensMaterial)
                    {
                        channel.lens = mesh.sharedMesh.GetSubMesh(Mathf.Min(i, mesh.sharedMesh.subMeshCount - 1)).bounds;
                        if (channel.lens.size.sqrMagnitude < .000001f) channel.lens = mesh.sharedMesh.bounds;
                        var go = new GameObject("Headlights / self-test beam"); go.transform.SetParent(effects.transform, false);
                        channel.beam = go.AddComponent<Light>(); channel.beam.type = LightType.Spot;
                        channel.beam.color = LampColor; channel.beam.range = length * 2.6f;
                        channel.beam.spotAngle = 48; channel.beam.innerSpotAngle = 26;
                        channel.beam.shadows = LightShadows.None; channel.beam.enabled = false;
                    }
                    channels.Add(channel);
                }
            }
        }

        Color LampColor => Style == RepairRevivalStyle.Finder ? new Color(.65f, .86f, .8f) :
            Style == RepairRevivalStyle.Skate ? new Color(.92f, .88f, .7f) : new Color(1, .77f, .4f);
        bool Secure(Part part) => part.visual && owner.IsPartSecured(part.visual);
        bool AllWheelsSecure()
        {
            bool any = false;
            foreach (var part in parts) if (part.kind == Kind.Wheel) { any = true; if (!part.secure) return false; }
            return any;
        }
        void Queue(Part part) => queue.Enqueue(new Reaction { part = part, revision = part.revision });
        void QueueBoot()
        {
            foreach (var part in parts) if (part.kind == Kind.Lamp && part.secure) Queue(part);
            foreach (var part in parts) if (part.kind == Kind.Wheel && part.secure) Queue(part);
            if (AllWheelsSecure()) queue.Enqueue(new Reaction { synchronized = true });
        }

        void Update()
        {
            if (!configured || !owner) return;
            if (owner.IsBeingCollected) { StopTests(); UpdateLamps(Time.deltaTime); return; }
            if (!owner.Accepts) { StopTests(); Powered = false; UpdateLamps(Time.deltaTime); return; }
            bool wasPowered = Powered; Powered = false;
            foreach (var part in parts)
            {
                bool fitted = part.visual && part.visual.activeSelf &&
                    (!part.visual.TryGetComponent<RepairPartMount>(out var mount) || !mount.IsInstalling);
                if (fitted && !part.fitted && part.kind == Kind.Battery) part.insertion = Time.time;
                part.fitted = fitted;
                bool secure = Secure(part);
                if (secure != part.secure)
                {
                    part.secure = secure; part.revision++;
                    if (!secure && current != null && (current.part == part || current.synchronized)) EndCurrent(false);
                    if (secure && wasPowered && part.kind != Kind.Battery && inspectionPhase == RepairDiagnosticPhase.None) Queue(part);
                }
                if (part.kind == Kind.Battery && secure) Powered = true;
            }
            bool allWheels = AllWheelsSecure();
            if (inspectionPhase != RepairDiagnosticPhase.None)
            {
                allWheelsWereSecure = allWheels;
                UpdateLamps(Time.deltaTime);
                return;
            }
            if (!Powered) StopTests();
            else if (!wasPowered) { StopTests(); QueueBoot(); }
            else if (allWheels && !allWheelsWereSecure) queue.Enqueue(new Reaction { synchronized = true });
            allWheelsWereSecure = allWheels;
            if (current != null)
            {
                timer += Time.deltaTime;
                if (timer >= Duration) EndCurrent(true);
            }
            else if ((gap -= Time.deltaTime) <= 0)
                while (queue.Count > 0)
                {
                    var reaction = queue.Dequeue();
                    if (reaction.synchronized ? !AllWheelsSecure() : !reaction.part.secure || reaction.revision != reaction.part.revision) continue;
                    current = reaction; timer = 0;
                    if (reaction.synchronized)
                    { foreach (var part in parts) if (part.kind == Kind.Wheel) AddWheelVisuals(part); }
                    else if (reaction.part.kind == Kind.Wheel) AddWheelVisuals(reaction.part);
                    break;
                }
            UpdateLamps(Time.deltaTime);
        }

        float Duration => current != null && current.part != null && current.part.kind == Kind.Lamp ? .85f :
            current != null && current.synchronized ? .7f : wheelCheckDuration * (Style == RepairRevivalStyle.Scavenger ? 1.25f : Style == RepairRevivalStyle.Finder ? 1 : .65f);

        void UpdateLamps(float dt)
        {
            foreach (var channel in channels)
            {
                if (!channel.renderer) continue;
                float wanted = Powered && channel.part.secure ? idleLampGlow : 0;
                if (current != null && current.part == channel.part && channel.part.kind == Kind.Lamp)
                {
                    float t = Mathf.Clamp01(timer / Duration);
                    wanted = Mathf.Lerp(.06f, peakLampGlow, Mathf.Pow(Mathf.Sin(t * Mathf.PI), 2));
                }
                if (inspectionPhase == RepairDiagnosticPhase.Power) wanted *= .35f;
                if (inspectionPhase == RepairDiagnosticPhase.Lights)
                {
                    wanted = Powered && channel.part.fitted ? peakLampGlow * Mathf.Pow(Mathf.Sin(inspectionProgress * Mathf.PI), 2) : 0;
                    if (!channel.part.secure) wanted *= .22f * (.35f + .65f * Mathf.Pow(Mathf.Sin(inspectionProgress * Mathf.PI * 5), 2));
                }
                if (channel.part.fitted)
                    foreach (var battery in parts)
                        if (battery.kind == Kind.Battery && battery.fitted && Time.time - battery.insertion < .4f)
                            wanted = Mathf.Max(wanted, Mathf.Sin(Mathf.Clamp01((Time.time - battery.insertion) / .4f) * Mathf.PI) * .18f);
                channel.brightness = Mathf.Lerp(channel.brightness, wanted, 1 - Mathf.Exp(-dt * 12));
                Color color = inspectionPhase == RepairDiagnosticPhase.Lights && !channel.part.secure ? new Color(1, .48f, .12f) : LampColor;
                channel.block.SetColor(Emission, channel.lensMaterial ? color * (channel.brightness * channel.emissionScale) : Color.black);
                channel.renderer.SetPropertyBlock(channel.block, channel.material);
                if (channel.beam)
                {
                    channel.beam.color = color;
                    channel.beam.enabled = channel.part.fitted && channel.brightness > .01f;
                    channel.beam.intensity = channel.brightness * length * length * 4;
                }
            }
        }

        void AddWheelVisuals(Part part)
        {
            foreach (var mesh in part.visual.GetComponentsInChildren<MeshFilter>())
            {
                if (!mesh.sharedMesh || mesh.GetComponentInParent<RepairBolt>() || !mesh.TryGetComponent<MeshRenderer>(out var renderer) || !renderer.enabled) continue;
                var proxy = new GameObject("Wheel / visual self-test"); proxy.layer = renderer.gameObject.layer;
                proxy.transform.SetParent(effects.transform, false); proxy.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
                var copy = proxy.AddComponent<MeshRenderer>(); copy.sharedMaterials = renderer.sharedMaterials;
                copy.shadowCastingMode = renderer.shadowCastingMode; copy.receiveShadows = renderer.receiveShadows;
                var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties); copy.SetPropertyBlock(properties);
                for (int index = 0; index < copy.sharedMaterials.Length; index++)
                { renderer.GetPropertyBlock(properties, index); copy.SetPropertyBlock(properties, index); }
                var size = mesh.sharedMesh.bounds.size;
                Vector3 axle = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
                float minimum = Mathf.Min(size.x, Mathf.Min(size.y, size.z)); float maximum = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                float middle = size.x + size.y + size.z - minimum - maximum;
                wheels.Add(new WheelVisual { part = part, source = mesh, original = renderer, wasEnabled = renderer.enabled, proxy = proxy.transform,
                    axle = axle, longModule = maximum > middle * 2.25f });
                renderer.enabled = false;
            }
        }

        void LateUpdate()
        {
            if (!configured) return;
            float t = inspectionPhase == RepairDiagnosticPhase.RunningGear ? inspectionProgress : current != null ? Mathf.Clamp01(timer / Duration) : 0;
            float envelope = Mathf.Pow(Mathf.Sin(t * Mathf.PI), 2);
            float gesture = Style == RepairRevivalStyle.Skate ? Mathf.Sin(t * Mathf.PI * 2) * Mathf.Sin(t * Mathf.PI) : envelope;
            foreach (var wheel in wheels)
            {
                if (!wheel.source || !wheel.proxy) continue;
                var source = wheel.source.transform;
                float response = inspectionPhase == RepairDiagnosticPhase.RunningGear && !wheel.part.secure ? Mathf.Sin(t * Mathf.PI) * Mathf.Sin(t * Mathf.PI * 10) * .22f : gesture;
                float angle = (wheel.longModule ? .65f : current != null && current.synchronized ? 9 : wheelCheckAngle) * response;
                var rotation = source.rotation * Quaternion.AngleAxis(angle, wheel.axle);
                var pivot = wheel.source.sharedMesh.bounds.center;
                Vector3 position = source.TransformPoint(pivot) - rotation * Vector3.Scale(pivot, source.lossyScale);
                if (wheel.longModule) position += transform.TransformDirection(forward) * (length * .003f * response);
                wheel.proxy.SetPositionAndRotation(position, rotation);
                wheel.proxy.localScale = Divide(source.lossyScale, effects.transform.lossyScale);
            }
            foreach (var part in parts)
                if (part.indicator && part.visual)
                {
                    float pulse = Mathf.Sin(Mathf.Clamp01((Time.time - part.insertion) / .4f) * Mathf.PI);
                    float brightness = part.secure ? .8f : part.fitted ? pulse * .45f : 0;
                    if (inspectionPhase == RepairDiagnosticPhase.Power && part.fitted)
                        brightness = .2f + .6f * Mathf.Pow(Mathf.Sin(inspectionProgress * Mathf.PI * (part.secure ? 1 : 3)), 2);
                    part.indicator.gameObject.SetActive(brightness > .005f);
                    part.indicator.position = part.visual.transform.TransformPoint(part.indicatorAnchor);
                    if(personality)personality.PoseEffect(part.indicator,false);
                    Color color = inspectionPhase == RepairDiagnosticPhase.Power && !part.secure ? new Color(1, .5f, .12f) : new Color(.35f, 1, .57f);
                    part.indicatorBlock.SetColor(BaseColor, color * brightness * 2);
                    part.indicatorRenderer.SetPropertyBlock(part.indicatorBlock);
                }
            var direction = transform.TransformDirection(forward);
            foreach (var channel in channels)
                if (channel.beam && channel.mesh)
                {
                    Vector3 local = channel.mesh.transform.InverseTransformDirection(direction).normalized;
                    float depth = Vector3.Dot(channel.lens.extents, Abs(local));
                    channel.beam.transform.position = channel.mesh.transform.TransformPoint(channel.lens.center + local * depth) + direction * length * .006f;
                    bool scanCheck = current != null && current.part == channel.part || inspectionPhase == RepairDiagnosticPhase.Lights;
                    float scanTime = inspectionPhase == RepairDiagnosticPhase.Lights ? inspectionProgress : t;
                    float scan = Style == RepairRevivalStyle.Finder && scanCheck ? Mathf.Sin(scanTime * Mathf.PI * 2) * 8 : 0;
                    channel.beam.transform.rotation = Quaternion.LookRotation(direction, transform.up) * Quaternion.Euler(12, scan, 0);
                    if(personality)personality.PoseEffect(channel.beam.transform,true);
                }
        }

        void EndCurrent(bool completed)
        {
            if (completed && current != null)
            {
                if (current.synchronized) SynchronizedChecksCompleted++;
                else if (current.part.kind == Kind.Wheel) WheelChecksCompleted++;
                else if (current.part.kind == Kind.Lamp) LampChecksCompleted++;
            }
            foreach (var wheel in wheels)
            {
                if (wheel.original) wheel.original.enabled = wheel.wasEnabled;
                if (wheel.proxy) { wheel.proxy.gameObject.SetActive(false); Destroy(wheel.proxy.gameObject); }
            }
            wheels.Clear(); current = null; gap = sequenceGap;
        }
        internal void SetInspection(RepairDiagnosticPhase phase, float progress)
        {
            if (phase != inspectionPhase)
            {
                StopTests(); inspectionPhase = phase; inspectionGearStarted = false;
            }
            inspectionProgress = Mathf.Clamp01(progress);
            if (phase == RepairDiagnosticPhase.RunningGear && Powered && !inspectionGearStarted)
            {
                inspectionGearStarted = true;
                foreach (var part in parts) if (part.kind == Kind.Wheel && part.fitted) AddWheelVisuals(part);
            }
            if (!Powered && wheels.Count > 0) EndCurrent(false);
        }
        internal void CompleteInspection(bool passed)
        {
            if (passed && inspectionGearStarted) SynchronizedChecksCompleted++;
            SetInspection(RepairDiagnosticPhase.None, 0);
            if(passed&&personality)personality.BeginReaction();
        }
        internal void StopTests()
        {
            if(personality)personality.StopReaction();
            queue.Clear(); if (current != null || wheels.Count > 0) EndCurrent(false);
            inspectionPhase = RepairDiagnosticPhase.None;
        }
        void OnEnable() { if (effects) effects.SetActive(true); Powered = false; }
        void OnDisable()
        {
            StopTests(); RestoreMaterials(); if (effects) effects.SetActive(false);
        }
        void RestoreMaterials()
        {
            foreach (var channel in channels) if (channel.renderer) channel.renderer.SetPropertyBlock(channel.original, channel.material);
        }
        void OnDestroy() => Shutdown();
        void Shutdown()
        {
            StopTests(); RestoreMaterials(); channels.Clear(); parts.Clear(); configured = false; Powered = false; allWheelsWereSecure = false;
            if (effects) { effects.SetActive(false); effects.transform.SetParent(null, true); Destroy(effects); effects = null; }
            if (indicatorMaterial) { Destroy(indicatorMaterial); indicatorMaterial = null; }
        }
        Vector3 BatterySurface(Transform part, Bounds bounds, Vector3 normal, Vector3 fallback)
        {
            float radius = bounds.extents.magnitude + length * .05f;
            float nearest = float.PositiveInfinity; Vector3 result = fallback;
            foreach (var mesh in part.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mesh.sharedMesh || mesh.GetComponentInParent<RepairBolt>()) continue;
                var probe = new GameObject("Battery surface probe");
                probe.transform.SetPositionAndRotation(mesh.transform.position, mesh.transform.rotation);
                probe.transform.localScale = mesh.transform.lossyScale;
                var collider = probe.AddComponent<MeshCollider>(); collider.sharedMesh = mesh.sharedMesh;
                Physics.SyncTransforms();
                for (int sample = 0; sample < 3; sample++)
                {
                    Vector3 target = bounds.center + Vector3.up * (bounds.extents.y * (sample == 0 ? .45f : sample == 1 ? 0 : -.3f));
                    var ray = new Ray(target + normal * radius, -normal);
                    if (collider.Raycast(ray, out var hit, radius * 2) && hit.distance < nearest)
                    { nearest = hit.distance; result = hit.point + hit.normal * length * .002f; }
                }
                collider.enabled = false; probe.SetActive(false); Destroy(probe);
            }
            return result;
        }
        static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        static Vector3 Divide(Vector3 a, Vector3 b) => new Vector3(a.x / Mathf.Max(.00001f, Mathf.Abs(b.x)), a.y / Mathf.Max(.00001f, Mathf.Abs(b.y)), a.z / Mathf.Max(.00001f, Mathf.Abs(b.z)));
    }
}
