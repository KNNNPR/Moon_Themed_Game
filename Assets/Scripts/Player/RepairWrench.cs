using MoonThemedGame.Interactions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MoonThemedGame.Repair
{
    // Both tools use pointer motion. Holding/clicking without movement does no work.
    public sealed class RepairWrench : RepairCarryable
    {
        public Fastener drives = Fastener.Bolt;
        public string displayName = "WRENCH";
        [Min(.2f)] public float pixelsPerDegree = 1.15f;
        [Range(50, 100)] public float strokeDegrees = 82;
        [Range(.1f, .8f)] public float circleAssistance = .4f;
        [Range(.1f, .6f)] public float finalResistance = .28f;
        RepairBolt target, working;
        float engage, strokeAngle, highWater, screwAngle, releaseLookUntil;
        int strokeDirection = 1;
        bool session, wasHeld, hasTangent, legacyReverseStroke;
        Vector2 tangent;
        public bool Engaged => engage > .01f && working;
        public bool Working => session;
        public Vector3 WorkAxis => working ? working.transform.up : Vector3.up;
        public float WristAngle { get; private set; }
        public Vector3 HandleGrip
        {
            get
            {
                var filter = GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh) return RendererBounds().center;
                var mesh = filter.sharedMesh; var axis = LongAxis();
                return transform.TransformPoint(mesh.bounds.center - axis * (Vector3.Dot(mesh.bounds.extents, axis) * .55f));
            }
        }
        static float Reach => 2.2f * PlayerBody.Scale;
        static float WorkReach => 1.5f * PlayerBody.Scale;
        protected override bool AlignInHand => true;
        protected override Vector3 HoldOffset => new Vector3(.26f, -.24f, .5f);
        public override string Prompt => $"[{GameControls.Label(GameCommand.Interact)}] " + (Loc.Russian ? "Взять " : "Take ") + Loc.T(displayName);

        // Read before FirstPersonController.UpdateLook, including the very first press.
        // This never owns the belt's LookFrozen flag.
        public static bool ConsumesLook
        {
            get
            {
                var tool = Current as RepairWrench; var mouse = Mouse.current;
                if (!tool || !tool.InputAllowed || mouse == null || mouse.rightButton.isPressed) return false;
                if (tool.session || Time.unscaledTime < tool.releaseLookUntil) return true;
                if (!mouse.leftButton.wasPressedThisFrame) return false;
                var candidate = RepairBolt.Aimed(new Ray(tool.hand.transform.position, tool.hand.transform.forward), Reach);
                return tool.Valid(candidate) && tool.Near(candidate);
            }
        }
        bool InputAllowed => IsCarried && hand && hand.enabled && Cursor.lockState == CursorLockMode.Locked &&
            !GameSettingsMenu.BlockGameplayInput && !DeveloperConsole.BlockGameplayInput && !PlayerBody.BeltOpen &&
            (!hand.GetComponentInParent<FirstPersonController>() || hand.GetComponentInParent<FirstPersonController>().enabled);
        bool Valid(RepairBolt bolt) => bolt && bolt.isActiveAndEnabled && bolt.Kind == drives && bolt.Seated;
        bool Near(RepairBolt bolt) => bolt && hand && Vector3.Distance(bolt.transform.position, hand.transform.position) <= WorkReach;
        static string T(string en, string ru) => Loc.Russian ? ru : en;

        void Update()
        {
            if (session && (!InputAllowed || !Valid(working) || !Near(working))) EndWork();
        }
        internal bool BeginWork(RepairBolt bolt)
        {
            if (!InputAllowed || !Valid(bolt) || !Near(bolt)) return false;
            working = target = bolt; session = true;
            wasHeld = hasTangent = false; strokeAngle = highWater = screwAngle = WristAngle = 0;
            releaseLookUntil = 0;
            return true;
        }
        internal void EndWork(bool automatic = false)
        {
            session = wasHeld = hasTangent = false;
            releaseLookUntil = automatic ? Time.unscaledTime + .12f : 0;
        }

        // Hardware-independent path for motion, return and torque validation.
        internal float ProcessMotion(Vector2 delta, bool held, bool reverse, float dt)
        {
            if (!session || !Valid(working) || dt <= 0) return 0;
            if (!held)
            {
                wasHeld = hasTangent = false;
                strokeAngle = Mathf.MoveTowards(strokeAngle, 0, dt * 600);
                screwAngle = Mathf.MoveTowards(screwAngle, 0, dt * 600);
                WristAngle = drives == Fastener.Bolt ? strokeAngle * strokeDirection : screwAngle;
                if (working.Tight && !working.IsTurning) EndWork(true);
                return 0;
            }
            if (!wasHeld)
            {
                if (Mathf.Abs(WristAngle) > 1)
                {
                    strokeAngle = Mathf.MoveTowards(strokeAngle, 0, dt * 600);
                    screwAngle = Mathf.MoveTowards(screwAngle, 0, dt * 600);
                    WristAngle = drives == Fastener.Bolt ? strokeAngle * strokeDirection : screwAngle;
                    return 0;
                }
                strokeAngle = highWater = screwAngle = 0; hasTangent = false;
                legacyReverseStroke=reverse;
                strokeDirection = reverse ? -1 : 0; wasHeld = true;
            }
            delta = Vector2.ClampMagnitude(delta, 800 * dt);
            float before = working.Progress;
            if (drives == Fastener.Bolt)
            {
                if(strokeDirection==0)
                {
                    if(Mathf.Abs(delta.x)<.15f)return 0;
                    strokeDirection=delta.x<0?-1:1;
                }
                float raw = delta.x * (legacyReverseStroke?1:strokeDirection) / pixelsPerDegree;
                float free = Mathf.Min(Mathf.Max(0, raw), Mathf.Max(0, highWater - strokeAngle));
                float motion = raw < 0 ? raw : free + TorqueMotion(raw - free, strokeDirection);
                float desired = Mathf.Clamp(strokeAngle + motion, 0, strokeDegrees);
                if (desired > highWater)
                {
                    float accepted = Mathf.Abs(working.DriveAngle((desired - highWater) * strokeDirection));
                    highWater += accepted; strokeAngle = highWater;
                }
                else strokeAngle = desired;
                WristAngle = strokeAngle * strokeDirection;
            }
            else if (delta.sqrMagnitude >= .12f)
            {
                if (hasTangent)
                {
                    float arc = -Vector2.SignedAngle(tangent, delta);
                    if (Mathf.Abs(arc) < 100)
                    {
                        arc = Mathf.Clamp(arc, -900 * dt, 900 * dt);
                        float raw = arc * circleAssistance * (reverse ? -1 : 1);
                        screwAngle += working.DriveAngle(TorqueMotion(Mathf.Abs(raw), raw >= 0 ? 1 : -1) * (raw >= 0 ? 1 : -1));
                        WristAngle = Mathf.Clamp(screwAngle, -85, 85);
                    }
                }
                tangent = delta.normalized; hasTangent = true;
            }
            return working.Progress - before;
        }

        float TorqueMotion(float rawDegrees, int direction)
        {
            float head = working.VisualHeadAngle;
            if (direction > 0)
            {
                float free = Mathf.Min(rawDegrees, Mathf.Max(0, 102 - head));
                return free + (rawDegrees - free) * finalResistance;
            }
            float stiffInput = Mathf.Max(0, head - 102) / finalResistance;
            return Mathf.Min(rawDegrees, stiffInput) * finalResistance + Mathf.Max(0, rawDegrees - stiffInput);
        }

        protected override void Carry()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed) EndWork();
            target = session ? working : RepairBolt.Aimed(new Ray(hand.transform.position, hand.transform.forward), Reach);
            bool valid = Valid(target) && Near(target);
            if (!session && valid && mouse != null && mouse.leftButton.wasPressedThisFrame && !mouse.rightButton.isPressed) BeginWork(target);
            if (session)
            {
                var delta = mouse != null ? mouse.delta.ReadValue() * (900f / Mathf.Max(480, Screen.height)) : Vector2.zero;
                ProcessMotion(delta, MouseHeld(), GameControls.Held(GameCommand.Sprint), Time.deltaTime);
            }
            else
            {
                WristAngle = Mathf.MoveTowards(WristAngle, 0, Time.deltaTime * 600);
                if (valid && !target.Tight) working = target;
            }
            float desiredEngage = session ? 1 : valid && !target.Tight ? .5f : 0;
            engage = Mathf.MoveTowards(engage, desiredEngage, Time.deltaTime * 9);
            if (!Engaged) return;
            var filter = GetComponent<MeshFilter>(); if (!filter || !filter.sharedMesh) return;
            var bounds = filter.sharedMesh.bounds; var length = LongAxis(); var normal = working.transform.up;
            Quaternion rotation;
            if (drives == Fastener.Screw)
                rotation = Quaternion.AngleAxis(WristAngle, normal) * Quaternion.FromToRotation(length, -normal);
            else
            {
                var size = bounds.size;
                var thin = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
                var along = Vector3.ProjectOnPlane(working.transform.position - hand.transform.position, normal).normalized;
                if (along.sqrMagnitude < .01f) along = Vector3.Cross(normal, hand.transform.right).normalized;
                rotation = Quaternion.AngleAxis(WristAngle, normal) * Quaternion.LookRotation(along, normal) * Quaternion.Inverse(Quaternion.LookRotation(length, thin));
            }
            var tip = bounds.center + length * (Vector3.Dot(bounds.extents, length) * .88f);
            float pressure = Mathf.InverseLerp(RepairBolt.FinalProgress, 1, working.Progress);
            var contact = working.transform.position + normal * (Mathf.Lerp(.006f, .001f, pressure) * PlayerBody.Scale);
            var position = contact - rotation * Vector3.Scale(tip, transform.lossyScale);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, position, engage), Quaternion.Slerp(transform.rotation, rotation, engage));
        }

        protected override void OnPutAway()
        {
            EndWork(); engage = 0; working = target = null; WristAngle = strokeAngle = screwAngle = highWater = releaseLookUntil = 0;
        }
        protected override string CarryHint
        {
            get
            {
                if (!target) return T("Aim at a fitted fastener", "Наведитесь на установленное крепление");
                if (target.Kind != drives) return T("Use the matching tool", "Возьмите подходящий инструмент");
                if (!target.Seated) return T("Wait for fitting / seat the part with the hammer", "Дождитесь установки / подбейте деталь молотком");
                if (!Near(target)) return T("Move closer to the fastener", "Подойдите ближе к креплению");
                if(target.Dismantling && target.Progress<=.0001f)
                {
                    var module=target.GetComponentInParent<AurinkoModule>();
                    return module && module.CanRemove?$"[{GameControls.Label(GameCommand.Interact)}] "+T("Remove module / tool returns to toolbox","Снять деталь / инструмент вернётся в тулбокс"):
                        T("LOOSE / [RMB] Move to the next fastener","ОТКРУЧЕНО / [ПКМ] Перейдите к следующему креплению");
                }
                string stage = target.Tight ? T("TIGHT", "ЗАТЯНУТО") : target.Progress >= target.ProgressLimit - .0001f && target.ProgressLimit < .999f
                    ? T("TACK THE OPPOSITE FASTENER (amber point)", "ПРИХВАТИТЕ ПРОТИВОПОЛОЖНОЕ КРЕПЛЕНИЕ (жёлтая точка)") :
                    target.Progress >= RepairBolt.FinalProgress ? T("FINAL TURN / RESISTANCE", "ПОСЛЕДНИЙ ДОРОВОТ / СОПРОТИВЛЕНИЕ") :
                    target.Progress >= RepairBolt.SnugProgress ? T("CLAMP THE MODULE", "ПРИЖМИТЕ ДЕТАЛЬ") : T("CATCH THE THREAD", "ПРИХВАТИТЕ РЕЗЬБУ");
                if(target.Dismantling)stage=T("DISMANTLE / ","РАЗБОРКА / ")+(drives==Fastener.Bolt?T("DRAG LEFT TO LOOSEN","ВЕДИТЕ ВЛЕВО, ЧТОБЫ ОТКРУТИТЬ"):T("TURN COUNTER-CLOCKWISE TO LOOSEN","КРУТИТЕ ПРОТИВ ЧАСОВОЙ СТРЕЛКИ"));
                string action = !session ? T("[LMB] Engage tool", "[ЛКМ] Установить инструмент") : drives == Fastener.Bolt
                    ? T("[LMB] Right: tighten / left: loosen / release to reset", "[ЛКМ] Вправо: затянуть / влево: открутить / отпустите для возврата")
                    : T("[LMB] Circle: clockwise tighten / counter-clockwise loosen", "[ЛКМ] Круг: по часовой затянуть / против открутить");
                return action + "\n" + stage + $"  {Mathf.RoundToInt(target.Progress * 100)}%" + (session ? "\n" + T("[RMB] Leave", "[ПКМ] Отойти") : "");
            }
        }
        protected override void OnGUI()
        {
            var interactor=hand?hand.GetComponentInParent<PlayerInteractor>():null;
            if(IsCarried&&interactor&&interactor.CurrentTarget is AurinkoModule ready&&ready.CanRemove)
            {
                if(!GameSettingsMenu.BlockGameplayInput&&!PlayerBody.BeltOpen)InteractionHud.Footer(Loc.F("[{0}] Drop",GameControls.Label(GameCommand.Throw)));
                return; // The module's [E] prompt takes priority over the tool hint.
            }
            base.OnGUI();
            if (!IsCarried || GameSettingsMenu.BlockGameplayInput || DeveloperConsole.BlockGameplayInput || PlayerBody.BeltOpen || !target || !hand) return;
            var next = target.NextFastener;
            if (!next || target.Progress < target.ProgressLimit - .0001f || target.ProgressLimit >= .999f) return;
            var point = hand.WorldToScreenPoint(next.transform.position);
            if (point.z <= 0 || point.x < 5 || point.y < 5 || point.x > Screen.width - 5 || point.y > Screen.height - 5) return;
            var old = GUI.color; GUI.color = new Color(1, .72f, .3f);
            GUI.DrawTexture(new Rect(Mathf.Round(point.x) - 3, Mathf.Round(Screen.height - point.y) - 3, 6, 6), Texture2D.whiteTexture); GUI.color = old;
        }
    }
}
