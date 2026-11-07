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
        internal sealed class Slot { public GameObject visual; public int part; }
        List<Slot> slots = new List<Slot>();
        readonly Dictionary<GameObject, List<RepairBolt>> loose = new Dictionary<GameObject, List<RepairBolt>>();
        sealed class Proud { public Vector3 home, offset; public int hits; }
        readonly Dictionary<GameObject, Proud> proud = new Dictionary<GameObject, Proud>();
        public const int HammerHits = 3;
        RepairPartDefinition[] definitions;
        bool initialized;
        public bool IsBeingCollected { get; private set; }
        public int MissingCount
        {
            get
            {
                int count = 0;
                foreach (var slot in slots) if (!slot.visual || !slot.visual.activeSelf || loose.ContainsKey(slot.visual) || proud.ContainsKey(slot.visual)) count++;
                return count;
            }
        }
        public bool IsRepaired => initialized && MissingCount == 0;
        internal void FillMissingParts(Dictionary<GameObject,int> counts)
        {
            counts.Clear();
            if(!initialized || IsBeingCollected)return;
            foreach(var slot in slots)
                if(!slot.visual || !slot.visual.activeSelf)
                {
                    var prefab=definitions[slot.part].prefab;
                    if(prefab)counts[prefab]=counts.TryGetValue(prefab,out int count)?count+1:1;
                }
        }
        public bool IsSettled
        {
            get
            {
                var landing=GetComponent<RepairLandingStabilizer>();
                if(landing && landing.IsDescending)return false;
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
                        text.Append('\n').Append(MoonThemedGame.Scavenger.SalvageField.PartName(definitions[i].prefab)).Append(" ×").Append(counts[i]);
                if (proud.Count > 0) text.Append('\n').Append(Loc.F("Knock in with the hammer: {0} parts", proud.Count));
                int bolts = 0, screws = 0;
                foreach (var pair in loose)
                    foreach (var fastener in pair.Value)
                        if (fastener && !fastener.Tight) { if (fastener.Kind == Fastener.Bolt) bolts++; else screws++; }
                if (bolts > 0) text.Append('\n').Append(Loc.F("Bolts to tighten with the wrench: {0}", bolts));
                if (screws > 0) text.Append('\n').Append(Loc.F("Screws to drive with the screwdriver: {0}", screws));
                return text.ToString();
            }
        }

        internal static List<Slot> FindSlots(Transform model, RepairPartDefinition[] parts)
        {
            var slots = new List<Slot>();
            foreach (Transform child in model)
            {
                string name = child.name.Split('.')[0];
                for (int p = 0; p < parts.Length; p++)
                {
                    if (parts[p] == null || !parts[p].prefab ||
                        !string.Equals(name, parts[p].prefab.name, StringComparison.OrdinalIgnoreCase)) continue;
                    slots.Add(new Slot { visual = child.gameObject, part = p });
                    break;
                }
            }
            return slots;
        }

        public void Initialize(RepairPartDefinition[] parts, int minimum, int maximum, int seed)
            => Initialize(parts, minimum, maximum, seed, null);

        internal void Initialize(RepairPartDefinition[] parts, int minimum, int maximum, int seed, int[] missingSlots)
        {
            definitions = parts;
            ClearBolts();
            initialized = false;
            IsBeingCollected = false;
            slots = FindSlots(transform, parts);
            if (slots.Count == 0) throw new InvalidOperationException("Robot has no part slots matching Prefabs/Parts.");
            foreach (var slot in slots) slot.visual.SetActive(true);
            foreach (int index in missingSlots ?? RepairDamagePlan.Select(slots.Count, minimum, maximum, seed))
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
            => DebugRepairAll();

        public bool DebugRepairAll()
        {
            if (!Application.isPlaying || !initialized || IsBeingCollected || !IsSettled) return false;
            ClearBolts();
            foreach (var slot in slots) if (slot.visual) slot.visual.SetActive(true);
            return IsRepaired;
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

        // The part sits in its slot but only counts once it is seated and all of its fasteners are tight.
        // Tight-fitting parts first stick out and have to be knocked in with the hammer.
        public void AttachLoose(GameObject visual, Vector3 viewer)
        {
            if (!visual || visual.activeSelf) return;
            visual.SetActive(true);
            if (RepairBolt.NeedsHammer(visual))
            {
                var outward = RepairBolt.Outward(this, visual, out float size);
                var parent = visual.transform.parent;
                var offset = parent ? parent.InverseTransformVector(outward * size * 0.07f) : outward * size * 0.07f;
                var seat = new Proud { home = visual.transform.localPosition, offset = offset, hits = HammerHits };
                visual.transform.localPosition = seat.home + seat.offset;
                proud[visual] = seat;
            }
            loose[visual] = RepairBolt.Spawn(this, visual, viewer, RepairBolt.KindFor(visual));
        }

        public bool Seated(GameObject visual) => !proud.ContainsKey(visual);
        public int HitsLeft(GameObject visual) => proud.TryGetValue(visual, out var seat) ? seat.hits : 0;

        // The sticking-out part the player is looking at.
        public GameObject FindProud(Ray view, float maxDistance)
        {
            GameObject best = null;
            float bestAngle = 16;
            foreach (var visual in proud.Keys)
            {
                if (!visual) continue;
                var offset = SlotCenter(visual) - view.origin;
                if (offset.magnitude > maxDistance) continue;
                float angle = Vector3.Angle(view.direction, offset);
                if (angle < bestAngle) { bestAngle = angle; best = visual; }
            }
            return best;
        }

        public void Strike(GameObject visual)
        {
            if (!visual || !proud.TryGetValue(visual, out var seat)) return;
            seat.hits--;
            visual.transform.localPosition = seat.home + seat.offset * (seat.hits / (float)HammerHits);
            if (seat.hits <= 0) proud.Remove(visual);
        }

        internal void BoltTightened(GameObject visual)
        {
            if (!loose.TryGetValue(visual, out var bolts)) return;
            foreach (var bolt in bolts) if (bolt && (!bolt.Tight || bolt.IsTurning)) return;
            loose.Remove(visual);
        }

        void ClearBolts()
        {
            foreach (var bolts in loose.Values)
                foreach (var bolt in bolts) if (bolt) Destroy(bolt.gameObject);
            loose.Clear();
            foreach (var pair in proud) if (pair.Key) pair.Key.transform.localPosition = pair.Value.home;
            proud.Clear();
        }
        public override void Interact() { }
    }
}
