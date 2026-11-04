using MoonThemedGame.Interactions;
using MoonThemedGame.Scavenger;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    // Low-poly tool case by the repair bay; E swings the lid open or shut.
    public sealed class RepairToolbox : Interactable
    {
        const float OpenAngle = 105, Speed = 2.6f;
        Transform lid;
        BoxCollider shell;
        Quaternion shut;
        Vector3 baseScale;
        float open, target, sign = 1, fitted, floor;

        public override string Prompt => Loc.T(target > 0 ? "E — Close the toolbox" : "E — Open the toolbox");

        public static RepairToolbox Place(Vector3 position, float yaw, float floor)
        {
            var catalog = Resources.Load<ShopCatalog>("ShopCatalog");
            if (!catalog || !catalog.toolbox) return null;
            var instance = Instantiate(catalog.toolbox, position, Quaternion.Euler(0, yaw, 0));
            instance.name = "Toolbox";
            var box = instance.AddComponent<RepairToolbox>();
            box.Setup(floor);
            return box;
        }

        void Setup(float floor)
        {
            this.floor = floor;
            baseScale = transform.localScale;
            Paint();
            lid = transform.Find("Lid");
            shell = gameObject.AddComponent<BoxCollider>();
            if (lid)
            {
                shut = lid.localRotation;
                // The FBX axis flip decides which way is "up"; pick the swing that lifts the lid.
                float before = lid.GetComponent<Renderer>().bounds.center.y;
                lid.localRotation = shut * Quaternion.AngleAxis(30, Vector3.right);
                if (lid.GetComponent<Renderer>().bounds.center.y < before) sign = -1;
                lid.localRotation = shut;
            }
            Fit();
        }

        // Sized to the player, who may be scaled up with the level; redone if that scale arrives late.
        void Fit()
        {
            fitted = PlayerBody.Scale;
            interactionDistance = 4.5f * fitted;
            var rotation = lid ? lid.localRotation : Quaternion.identity;
            if (lid) lid.localRotation = shut;
            transform.localScale = baseScale * fitted;
            shell.enabled = false;
            var bounds = Bounds();
            bool solid = Physics.Raycast(bounds.center + Vector3.up * bounds.size.y, Vector3.down, out var hit, bounds.size.y * 20, ~0, QueryTriggerInteraction.Ignore);
            transform.position += Vector3.up * ((solid ? hit.point.y : floor) - bounds.min.y);
            bounds = Bounds();
            shell.enabled = true;
            shell.center = transform.InverseTransformPoint(bounds.center);
            var size = transform.InverseTransformVector(bounds.size);
            shell.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            if (lid) lid.localRotation = rotation;
        }

        void Paint()
        {
            var catalog = Resources.Load<ShopCatalog>("ShopCatalog");
            var shader = Shader.Find("Old Tech/Retro Cel Lit");
            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var color = materials[i] ? materials[i].color : Color.gray;
                    var material = catalog && catalog.suitMaterial ? new Material(catalog.suitMaterial) : new Material(shader ? shader : Shader.Find("Universal Render Pipeline/Lit"));
                    if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", null);
                    material.color = color;
                    if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0);
                    materials[i] = material;
                }
                renderer.sharedMaterials = materials;
            }
        }

        Bounds Bounds()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(transform.position, Vector3.zero);
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        public override void Interact() => target = target > 0 ? 0 : 1;

        void Update()
        {
            if (!Mathf.Approximately(fitted, PlayerBody.Scale)) Fit();
            if (!lid || Mathf.Approximately(open, target)) return;
            open = Mathf.MoveTowards(open, target, Time.deltaTime * Speed);
            float t = open * open * (3 - 2 * open);
            lid.localRotation = shut * Quaternion.AngleAxis(sign * OpenAngle * t, Vector3.right);
        }
    }
}
