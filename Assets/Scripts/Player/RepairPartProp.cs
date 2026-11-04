using MoonThemedGame.Scavenger;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoonThemedGame.Repair
{
    // A salvaged part in the repair bay: carry it to the robot, line it up with its empty slot and click to fit it.
    public sealed class RepairPartProp : RepairCarryable
    {
        const float FitDistance = 3.5f;
        static Material ghostMaterial;

        public GameObject prefab;
        GameObject ghost, ghostSlot, slot;
        RepairRobotTarget slotRobot;

        protected override Vector3 HoldOffset => new Vector3(0.05f, -0.28f, 0.7f);
        public override string Prompt => Loc.F("E — Take {0}", SalvageField.PartName(prefab));

        public static RepairPartProp Create(GameObject prefab, Vector3 position, float floor)
        {
            var instance = Instantiate(prefab, position, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            instance.name = "Part / " + prefab.name;
            var part = instance.AddComponent<RepairPartProp>();
            part.prefab = prefab;
            part.Settle(position, floor);
            return part;
        }

        protected override void Carry()
        {
            slot = null;
            slotRobot = null;
            var view = new Ray(hand.transform.position, hand.transform.forward);
            foreach (var robot in FindObjectsByType<RepairRobotTarget>(FindObjectsSortMode.None))
            {
                var found = robot.FindSlot(prefab, view, FitDistance);
                if (found) { slot = found; slotRobot = robot; break; }
            }
            ShowGhost(slot);
            if (slot && MouseClicked())
            {
                slotRobot.AttachLoose(slot, hand.transform.position);
                ShowGhost(null);
                Release();
                Destroy(gameObject);
            }
        }

        protected override string CarryHint
        {
            get
            {
                string name = SalvageField.PartName(prefab);
                if (slot) return Loc.F("LMB — fit {0}", name);
                foreach (var robot in FindObjectsByType<RepairRobotTarget>(FindObjectsSortMode.None))
                    if (robot.Needs(prefab))
                        return robot.Accepts ? Loc.F("Aim at the empty {0} slot on the robot", name) : Loc.T("Wait until the robot has landed");
                return Loc.T("No robot needs this part right now");
            }
        }

        void ShowGhost(GameObject target)
        {
            if (target == ghostSlot) { if (ghost) Pulse(); return; }
            if (ghost) Destroy(ghost);
            ghost = null;
            ghostSlot = target;
            if (!target) return;
            ghost = Instantiate(target, target.transform.parent);
            ghost.name = "Fit preview";
            foreach (var collider in ghost.GetComponentsInChildren<Collider>(true)) DestroyImmediate(collider);
            var material = GhostMaterial();
            if (material)
                foreach (var r in ghost.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < materials.Length; i++) materials[i] = material;
                    r.sharedMaterials = materials;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
            ghost.SetActive(true);
            Pulse();
        }

        void Pulse()
        {
            if (!ghostMaterial) return;
            ghostMaterial.SetColor("_BaseColor", new Color(0.45f, 1, 0.55f, 0.22f + 0.16f * Mathf.Sin(Time.time * 6)));
        }

        static Material GhostMaterial()
        {
            if (ghostMaterial) return ghostMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (!shader) return null;
            ghostMaterial = new Material(shader) { name = "Repair fit preview", hideFlags = HideFlags.HideAndDontSave };
            ghostMaterial.SetFloat("_Surface", 1);
            ghostMaterial.SetFloat("_Blend", 0);
            ghostMaterial.SetOverrideTag("RenderType", "Transparent");
            ghostMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            ghostMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            ghostMaterial.SetFloat("_ZWrite", 0);
            ghostMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            ghostMaterial.renderQueue = (int)RenderQueue.Transparent;
            return ghostMaterial;
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (!IsCarried && ghost) ShowGhost(null);
        }

        void OnDestroy()
        {
            if (ghost) Destroy(ghost);
        }
    }
}
