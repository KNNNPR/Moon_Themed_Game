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
        sealed class Proud { public Vector3 home, offset; public int hits,total=HammerHits; }
        readonly Dictionary<GameObject, Proud> proud = new Dictionary<GameObject, Proud>();
        public const int HammerHits = 3;
        RepairPartDefinition[] definitions;
        bool initialized;
        public RepairOrderTask SpecialStep {get;private set;}
        public RepairRobotDefinition Definition { get; internal set; }
        public bool IsBeingCollected { get; private set; }
        public int MissingCount
        {
            get
            {
                int count = 0;
                foreach (var slot in slots) if (!slot.visual || !slot.visual.activeSelf || loose.ContainsKey(slot.visual) || proud.ContainsKey(slot.visual)||SpecialStep&&SpecialStep.PendingFor(slot.visual)) count++;
                return count;
            }
        }
        public bool IsDismantling=>GetComponent<AurinkoDismantling>();
        public bool IsRepaired => initialized && !IsDismantling && MissingCount == 0;
        public bool ReadyForCollection=>IsDismantling?GetComponent<AurinkoDismantling>().Complete:IsRepaired;
        internal void FillMissingParts(Dictionary<GameObject,int> counts)
        {
            counts.Clear();
            if(IsDismantling)return;
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
                if(IsDismantling)return GetComponent<AurinkoDismantling>().Status;
                if(SpecialStep&&SpecialStep.Aimed(Camera.main))return SpecialStep.Instruction+"\n["+GameControls.Label(GameCommand.Interact)+"] "+(Loc.Russian?"Проверить бота":"Run final test");
                var test = GetComponent<RepairRobotFinalTest>();
                if (test && test.IsRunning) return test.Status;
                string check = Loc.Russian ? $"[{GameControls.Label(GameCommand.Interact)}] Проверить бота" : $"[{GameControls.Label(GameCommand.Interact)}] Run final test";
                if (IsRepaired) return Loc.T(IsSettled ? "Robot repaired. You can call the crane." : "Robot repaired. Waiting for landing.") + (Accepts ? "\n" + check : "");
                var counts = new int[definitions.Length];
                foreach (var slot in slots)
                    if (!slot.visual || !slot.visual.activeSelf) counts[slot.part]++;
                var text = new StringBuilder(Loc.T("Missing parts:"));
                for (int i = 0; i < counts.Length; i++)
                    if (counts[i] > 0)
                        text.Append('\n').Append(MoonThemedGame.Scavenger.SalvageField.PartName(definitions[i].prefab)).Append(" ×").Append(counts[i]);
                if (proud.Count > 0) text.Append('\n').Append(Loc.F("Knock in with the hammer: {0} parts", proud.Count));
                if(SpecialStep&&SpecialStep.Pending)text.Append('\n').Append(SpecialStep.Instruction);
                int bolts = 0, screws = 0;
                foreach (var pair in loose)
                    foreach (var fastener in pair.Value)
                        if (fastener && !fastener.Tight) { if (fastener.Kind == Fastener.Bolt) bolts++; else screws++; }
                if (bolts > 0) text.Append('\n').Append(Loc.F("Bolts to tighten with the wrench: {0}", bolts));
                if (screws > 0) text.Append('\n').Append(Loc.F("Screws to drive with the screwdriver: {0}", screws));
                if (Accepts) text.Append('\n').Append(check);
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
            if(TryGetComponent<RepairOrderTask>(out var oldTask)){oldTask.ResetTask();SpecialStep=oldTask;}
            if (TryGetComponent<RepairRobotRevival>(out var previous)) previous.StopTests();
            if (TryGetComponent<RepairRobotFinalTest>(out var previousTest)) previousTest.Cancel();
            definitions = parts;
            ClearBolts();
            foreach(var fx in GetComponentsInChildren<RepairFeedbackFx>(true))fx.ResetForOrder(this);
            initialized = false;
            IsBeingCollected = false;
            slots = FindSlots(transform, parts);
            if (slots.Count == 0) throw new InvalidOperationException("Robot has no part slots matching Prefabs/Parts.");
            foreach (var slot in slots) slot.visual.SetActive(true);
            foreach (int index in missingSlots ?? RepairDamagePlan.Select(slots.Count, minimum, maximum, seed))
                slots[index].visual.SetActive(false);
            initialized = true;
            interactionDistance = 4f;
            var revival = GetComponent<RepairRobotRevival>();
            if (!revival) revival = gameObject.AddComponent<RepairRobotRevival>();
            revival.Configure(this, slots);
            var test = GetComponent<RepairRobotFinalTest>();
            if (!test) test = gameObject.AddComponent<RepairRobotFinalTest>();
            test.Configure(this, revival);
        }

        internal void ConfigureSpecialStep(int orderNumber,int[] missing)
        {
            var settings=Resources.Load<RepairSpecialStepSettings>("RepairSpecialStepSettings");
            if(!settings||!settings.enableSpecialRepairs||IsDismantling)return;
            if(!SpecialStep)SpecialStep=GetComponent<RepairOrderTask>();
            if(!SpecialStep)SpecialStep=gameObject.AddComponent<RepairOrderTask>();
            SpecialStep.Configure(this,slots,orderNumber,missing);
        }

        internal void GetDiagnosticFaults(List<RepairRobotFinalTest.Fault> faults)
        {
            faults.Clear();
            foreach (var slot in slots)
            {
                var fault = new RepairRobotFinalTest.Fault { visual = slot.visual, prefab = definitions[slot.part].prefab };
                if (!slot.visual || !slot.visual.activeSelf) fault.reason = RepairRobotFinalTest.Reason.Missing;
                else if (slot.visual.TryGetComponent<RepairPartMount>(out var mount) && mount.IsInstalling) fault.reason = RepairRobotFinalTest.Reason.Installing;
                else if(SpecialStep&&SpecialStep.PendingFor(slot.visual))fault.reason=RepairRobotFinalTest.Reason.SpecialStep;
                else if (proud.ContainsKey(slot.visual)) fault.reason = RepairRobotFinalTest.Reason.Unseated;
                else if (loose.TryGetValue(slot.visual, out var bolts))
                {
                    fault.reason = RepairRobotFinalTest.Reason.Fasteners;
                    foreach (var bolt in bolts) if (bolt && (!bolt.Tight || bolt.IsTurning)) fault.count++;
                }
                else continue;
                faults.Add(fault);
            }
        }

        internal bool IsPartSecured(GameObject visual) => initialized && visual && visual.activeSelf &&
            !loose.ContainsKey(visual) && !proud.ContainsKey(visual) && Seated(visual);

        public bool TryInstallPart(GameObject partPrefab)
        {
            if (!initialized || IsDismantling || IsBeingCollected || !IsSettled || !partPrefab) return false;
            foreach (var slot in slots)
                if (slot.visual && !slot.visual.activeSelf && definitions[slot.part].prefab == partPrefab)
                {
                    if(SpecialStep&&SpecialStep.PendingFor(slot.visual))return TryAttachLoose(slot.visual,transform.position+Vector3.up*2,null);
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
            if (!Application.isPlaying || !initialized || IsDismantling || IsBeingCollected || !IsSettled) return false;
            if (TryGetComponent<RepairRobotFinalTest>(out var test)) test.Cancel();
            if (TryGetComponent<RepairRobotRevival>(out var revival)) revival.StopTests();
            if(SpecialStep)SpecialStep.DebugComplete();
            ClearBolts();
            foreach (var slot in slots) if (slot.visual) slot.visual.SetActive(true);
            return IsRepaired;
        }

        public bool TryReserveCollection()
        {
            if (!isActiveAndEnabled || !ReadyForCollection || !IsSettled || IsBeingCollected) return false;
            IsBeingCollected = true;
            if (TryGetComponent<RepairRobotFinalTest>(out var test)) test.Cancel();
            if (TryGetComponent<RepairRobotRevival>(out var revival)) revival.StopTests();
            SetHighlighted(false);
            return true;
        }
        public void CancelCollection() => IsBeingCollected = false;
        public override bool CanInteract(Camera camera) => base.CanInteract(camera) && !IsBeingCollected && !RepairCarryable.Current;

        public bool Accepts => initialized && !IsBeingCollected && IsSettled;

        public bool Needs(GameObject partPrefab)
        {
            if (!initialized || IsDismantling || !partPrefab) return false;
            foreach (var slot in slots)
                if (slot.visual && !slot.visual.activeSelf && definitions[slot.part].prefab == partPrefab) return true;
            return false;
        }

        // The empty slot for this part that the player is looking at.
        public GameObject FindSlot(GameObject partPrefab, Ray view, float maxDistance)
        {
            if (!Accepts || IsDismantling || !partPrefab) return null;
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
            => TryAttachLoose(visual, viewer, null);

        public bool TryAttachLoose(GameObject visual, Vector3 viewer, Transform carried)
        {
            if (!Accepts || IsDismantling || !visual || visual.activeSelf || !slots.Exists(slot => slot.visual == visual)) return false;
            var home = visual.transform.localPosition;
            var rotation = visual.transform.localRotation;
            visual.SetActive(true);
            var geometry = RepairPartMount.GeometryBounds(visual.transform);
            var outward = RepairBolt.Outward(this, visual, out float size);
            var parent = visual.transform.parent;
            Vector3 offset = Vector3.zero;
            bool special=SpecialStep&&SpecialStep.PendingFor(visual);
            if (RepairBolt.NeedsHammer(visual)&&(!special||SpecialStep.Kind==RepairOrderTaskKind.Casing))
            {
                offset = parent ? parent.InverseTransformVector(outward * size * 0.07f) : outward * size * 0.07f;
                int hits=special?SpecialStep.RequiredHits:HammerHits;
                var seat = new Proud { home = visual.transform.localPosition, offset = offset, hits = hits,total=hits };
                visual.transform.localPosition = seat.home + seat.offset;
                proud[visual] = seat;
            }
            loose[visual] = RepairBolt.Spawn(this, visual, viewer, RepairBolt.KindFor(visual));
            var mount = visual.GetComponent<RepairPartMount>();
            if (!mount) mount = visual.AddComponent<RepairPartMount>();
            // Geometry was measured at the rest pose; use its center at the proud pose for registration.
            geometry.center += parent ? parent.TransformVector(offset) : offset;
            mount.Begin(this, loose[visual], home, rotation, offset, outward, size, geometry, carried);
            if(special)SpecialStep.Fitted();
            return true;
        }

        public bool Seated(GameObject visual) => !(SpecialStep&&SpecialStep.PendingFor(visual))&&!proud.ContainsKey(visual) &&
            (!visual || !visual.TryGetComponent<RepairPartMount>(out var mount) || !mount.IsInstalling);
        public int HitsLeft(GameObject visual) => proud.TryGetValue(visual, out var seat) ? seat.hits : 0;

        // The sticking-out part the player is looking at.
        public GameObject FindProud(Ray view, float maxDistance)
        {
            GameObject best = null;
            float bestAngle = 16;
            foreach (var visual in proud.Keys)
            {
                if (!visual) continue;
                if (visual.TryGetComponent<RepairPartMount>(out var mount) && mount.IsInstalling) continue;
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
            if (visual.TryGetComponent<RepairPartMount>(out var mount) && mount.IsInstalling) return;
            var view=Camera.main;
            RepairFeedbackFx.For(visual)?.Hammer(view?view.transform.position:visual.transform.position+transform.forward);
            seat.hits--;
            var offset = seat.offset * (seat.hits / (float)seat.total);
            if (mount) mount.SetSeatOffset(offset);
            else visual.transform.localPosition = seat.home + offset;
            if (seat.hits <= 0) proud.Remove(visual);
            if(SpecialStep&&SpecialStep.PendingFor(visual))SpecialStep.HammerHit();
            BoltTightened(visual);
        }

        internal void SpecialSeatComplete(GameObject visual)
        {
            if(!visual)return;proud.Remove(visual);
            if(visual.TryGetComponent<RepairPartMount>(out var mount))mount.SetSeatOffset(Vector3.zero);
            BoltTightened(visual);
        }
        internal void RestoreNormalSeat(GameObject visual,float remaining)
        {
            if(!visual||!visual.activeSelf||!visual.TryGetComponent<RepairPartMount>(out var mount))return;
            if(!RepairBolt.NeedsHammer(visual)){mount.SetSeatOffset(Vector3.zero);return;}
            var normal=RepairBolt.Outward(this,visual,out float size);
            var offset=visual.transform.parent?visual.transform.parent.InverseTransformVector(normal*size*.07f):normal*size*.07f;
            int hits=Mathf.Clamp(Mathf.CeilToInt(remaining*HammerHits),1,HammerHits);
            proud[visual]=new Proud{home=mount.RestPosition,offset=offset,hits=hits};
            mount.SetSeatOffset(offset*(hits/(float)HammerHits));
        }
        internal string SeatingHint(GameObject visual)=>SpecialStep&&SpecialStep.PendingFor(visual)?
            (SpecialStep.Kind==RepairOrderTaskKind.Casing?SpecialStep.Instruction:
                (Loc.Russian?"[ЛКМ] Убрать инструмент. ":"[LMB] Stow tool. ")+SpecialStep.Instruction):
            (Loc.Russian?"Дождитесь установки / подбейте деталь молотком":"Wait for fitting / seat the part with the hammer");

        internal void BoltTightened(GameObject visual)
        {
            if (!Seated(visual)) return;
            if (!loose.TryGetValue(visual, out var bolts)) return;
            foreach (var bolt in bolts) if (bolt && (!bolt.Tight || bolt.IsTurning)) return;
            if (visual.TryGetComponent<RepairPartMount>(out var mount)) mount.Secure();
            loose.Remove(visual);
        }

        internal void FastenerTurned(GameObject visual)
        {
            // Backing out a previously tight bolt makes the mount loose again.
            if (!loose.ContainsKey(visual) && visual && visual.TryGetComponent<RepairPartMount>(out var mount))
                loose[visual] = mount.Fasteners;
        }

        void ClearBolts()
        {
            foreach (var slot in slots)
                if (slot.visual)
                {
                    if (slot.visual.TryGetComponent<RepairPartMount>(out var mount)) mount.ResetMount();
                    foreach (var bolt in slot.visual.GetComponentsInChildren<RepairBolt>(true))
                    { bolt.gameObject.SetActive(false); Destroy(bolt.gameObject); }
                }
            loose.Clear();
            foreach (var pair in proud) if (pair.Key) pair.Key.transform.localPosition = pair.Value.home;
            proud.Clear();
        }
        public override void Interact()
        {
            if(IsDismantling)return;
            if (TryGetComponent<RepairRobotFinalTest>(out var test)) test.StartTest();
        }
    }
}
