using System;
using System.Collections.Generic;
using System.Text;
using MoonThemedGame.Interactions;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    [Serializable]
    public sealed class RepairPartDefinition
    {
        public GameObject prefab;
        public string displayName;
    }

    [DisallowMultipleComponent]
    public sealed class RepairRobotTarget : Interactable
    {
        sealed class Slot { public GameObject visual; public int part; }
        readonly List<Slot> slots = new List<Slot>();
        readonly Dictionary<GameObject, List<RepairBolt>> loose = new Dictionary<GameObject, List<RepairBolt>>();
        RepairPartDefinition[] definitions;
        bool initialized;
        public bool IsBeingCollected { get; private set; }
        public int MissingCount
        {
            get
            {
                int count = 0;
                foreach (var slot in slots) if (!slot.visual || !slot.visual.activeSelf || loose.ContainsKey(slot.visual)) count++;
                return count;
            }
        }
        public bool IsRepaired => initialized && MissingCount == 0;
        public bool IsSettled
        {
            get
            {
                var body = GetComponent<Rigidbody>();
                return !body || body.isKinematic;
            }
        }
        public override string Prompt
        {
            get
            {
                if (!initialized) return Loc.T("Robot diagnostics…");
                if (IsRepaired) return Loc.T(IsSettled ? "Robot repaired. You can call the crane." : "Robot repaired. Waiting for landing.");
                var counts = new int[definitions.Length];
                foreach (var slot in slots)
                    if (!slot.visual || !slot.visual.activeSelf) counts[slot.part]++;
                var text = new StringBuilder(Loc.T("Missing parts:"));
                for (int i = 0; i < counts.Length; i++)
                    if (counts[i] > 0)
                        text.Append('\n').Append(string.IsNullOrWhiteSpace(definitions[i].displayName)
                            ? definitions[i].prefab.name : definitions[i].displayName).Append(" ×").Append(counts[i]);
                if (loose.Count > 0) text.Append('\n').Append(Loc.F("Bolts to tighten with the wrench: {0} parts", loose.Count));
                return text.ToString();
            }
        }

        public void Initialize(RepairPartDefinition[] parts, int minimum, int maximum, int seed)
        {
            definitions = parts;
            slots.Clear();
            ClearBolts();
            initialized = false;
            IsBeingCollected = false;
            foreach (Transform child in transform)
            {
                string name = child.name.Split('.')[0];
                for (int p = 0; p < parts.Length; p++)
                {
                    if (parts[p] == null || !parts[p].prefab ||
                        !string.Equals(name, parts[p].prefab.name, StringComparison.OrdinalIgnoreCase)) continue;
                    child.gameObject.SetActive(true);
                    slots.Add(new Slot { visual = child.gameObject, part = p });
                    break;
                }
            }
            if (slots.Count == 0) throw new InvalidOperationException("Robot has no part slots matching Prefabs/Parts.");
            foreach (int index in RepairDamagePlan.Select(slots.Count, minimum, maximum, seed))
                slots[index].visual.SetActive(false);
            initialized = true;
            interactionDistance = 4f;
        }

        public bool TryInstallPart(GameObject partPrefab)
        {
            if (!initialized || IsBeingCollected || !IsSettled || !partPrefab) return false;
            foreach (var slot in slots)
                if (slot.visual && !slot.visual.activeSelf && definitions[slot.part].prefab == partPrefab)
                {
                    slot.visual.SetActive(true);
                    return true;
                }
            return false;
        }

        [ContextMenu("Debug/Repair All Parts")]
        public void RepairAllForTesting()
        {
            if (!Application.isPlaying || !initialized || IsBeingCollected || !IsSettled) return;
            foreach (var slot in slots) if (slot.visual) slot.visual.SetActive(true);
        }

        public bool TryReserveCollection()
        {
            if (!isActiveAndEnabled || !IsRepaired || !IsSettled || IsBeingCollected) return false;
            IsBeingCollected = true;
            SetHighlighted(false);
            return true;
        }
        public void CancelCollection() => IsBeingCollected = false;
        public override bool CanInteract(Camera camera) => base.CanInteract(camera) && !IsBeingCollected && !RepairCarryable.Current;

        public bool Accepts => initialized && !IsBeingCollected && IsSettled;

        public bool Needs(GameObject partPrefab)
        {
            if (!initialized || !partPrefab) return false;
            foreach (var slot in slots)
                if (slot.visual && !slot.visual.activeSelf && definitions[slot.part].prefab == partPrefab) return true;
            return false;
        }

        // The empty slot for this part that the player is looking at.
        public GameObject FindSlot(GameObject partPrefab, Ray view, float maxDistance)
        {
            if (!Accepts || !partPrefab) return null;
            GameObject best = null;
            float bestAngle = 14;
            foreach (var slot in slots)
            {
                if (!slot.visual || slot.visual.activeSelf || definitions[slot.part].prefab != partPrefab) continue;
                var offset = SlotCenter(slot.visual) - view.origin;
                if (offset.magnitude > maxDistance) continue;
                float angle = Vector3.Angle(view.direction, offset);
                if (angle < bestAngle) { bestAngle = angle; best = slot.visual; }
            }
            return best;
        }

        static Vector3 SlotCenter(GameObject visual)
        {
            var filters = visual.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0) return visual.transform.position;
            var sum = Vector3.zero;
            int count = 0;
            foreach (var filter in filters)
                if (filter.sharedMesh) { sum += filter.transform.TransformPoint(filter.sharedMesh.bounds.center); count++; }
            return count > 0 ? sum / count : visual.transform.position;
        }

        // The part sits in its slot but only counts once all of its bolts are tight.
        public void AttachLoose(GameObject visual, Vector3 viewer)
        {
            if (!visual || visual.activeSelf) return;
            visual.SetActive(true);
            loose[visual] = RepairBolt.Spawn(this, visual, viewer);
        }

        internal void BoltTightened(GameObject visual)
        {
            if (!loose.TryGetValue(visual, out var bolts)) return;
            foreach (var bolt in bolts) if (bolt && !bolt.Tight) return;
            loose.Remove(visual);
        }

        void ClearBolts()
        {
            foreach (var bolts in loose.Values)
                foreach (var bolt in bolts) if (bolt) Destroy(bolt.gameObject);
            loose.Clear();
        }
        public override void Interact() { }
    }
}
