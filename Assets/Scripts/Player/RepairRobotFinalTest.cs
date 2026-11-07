using System.Collections.Generic;
using System.Text;
using MoonThemedGame.Interactions;
using MoonThemedGame.Scavenger;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    public enum RepairDiagnosticPhase { None, Power, Lights, RunningGear }

    [DefaultExecutionOrder(200), DisallowMultipleComponent]
    public sealed class RepairRobotFinalTest : MonoBehaviour
    {
        internal enum Reason { Missing, Installing, Unseated, Fasteners }
        internal struct Fault { public GameObject visual, prefab; public Reason reason; public int count; }
        [Min(1.5f)] public float duration = 3.4f;
        [Min(1)] public float resultDuration = 6;
        public bool IsRunning { get; private set; }
        public bool Passed { get; private set; }
        public int FaultCount => faults.Count;
        public RepairDiagnosticPhase Phase { get; private set; }
        public string Status
        {
            get
            {
                if (!IsRunning) return Passed ? T("[OK] ROBOT READY", "[OK] РОБОТ ГОТОВ") : T("[!] CHECK FAILED", "[!] ПРОВЕРКА НЕ ПРОЙДЕНА");
                return Phase == RepairDiagnosticPhase.Power ? T("SYSTEM CHECK // POWER", "ПРОВЕРКА // ПИТАНИЕ") :
                    Phase == RepairDiagnosticPhase.Lights ? T("SYSTEM CHECK // LIGHTS", "ПРОВЕРКА // ФАРЫ") :
                    T("SYSTEM CHECK // RUNNING GEAR", "ПРОВЕРКА // ХОДОВАЯ");
            }
        }
        readonly List<Fault> faults = new List<Fault>();
        readonly StringBuilder text = new StringBuilder();
        RepairRobotTarget owner;
        RepairRobotRevival revival;
        float elapsed, resultUntil;
        bool wasRepaired;
        static string T(string en, string ru) => Loc.Russian ? ru : en;

        internal void Configure(RepairRobotTarget target, RepairRobotRevival effects)
        {
            Cancel(); owner = target; revival = effects;
            Passed = wasRepaired = false; resultUntil = 0; faults.Clear();
        }

        public bool StartTest()
        {
            if (IsRunning || !owner || !owner.Accepts || !revival) return false;
            IsRunning = true; Passed = false; elapsed = 0; resultUntil = 0;
            owner.GetDiagnosticFaults(faults);
            Phase = RepairDiagnosticPhase.Power;
            revival.SetInspection(Phase, 0);
            return true;
        }

        void Update()
        {
            if (!owner) return;
            if (!owner.Accepts) { if (IsRunning) Cancel(); return; }
            bool repaired = owner.IsRepaired;
            if (!IsRunning && repaired && !wasRepaired) StartTest();
            if (!repaired && Passed) { Passed = false; resultUntil = 0; }
            wasRepaired = repaired;
            if (!IsRunning)
            {
                if (!Passed && Time.time < resultUntil) owner.GetDiagnosticFaults(faults);
                return;
            }
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            Phase = progress < .22f ? RepairDiagnosticPhase.Power : progress < .52f ? RepairDiagnosticPhase.Lights : RepairDiagnosticPhase.RunningGear;
            float stage = Phase == RepairDiagnosticPhase.Power ? progress / .22f :
                Phase == RepairDiagnosticPhase.Lights ? Mathf.InverseLerp(.22f, .52f, progress) : Mathf.InverseLerp(.52f, 1, progress);
            owner.GetDiagnosticFaults(faults);
            revival.SetInspection(Phase, stage);
            float rattle = Phase == RepairDiagnosticPhase.RunningGear ? Mathf.Sin(stage * Mathf.PI) : .2f;
            foreach (var fault in faults)
                if (fault.visual && fault.visual.TryGetComponent<RepairPartMount>(out var mount)) mount.InspectionPlay = rattle;
            if (progress < 1) return;
            Passed = owner.IsRepaired && revival.Powered && faults.Count == 0;
            revival.CompleteInspection(Passed);
            IsRunning = false; Phase = RepairDiagnosticPhase.None;
            ResetPlay(); resultUntil = Time.time + resultDuration;
        }

        internal void Cancel()
        {
            IsRunning = false; Phase = RepairDiagnosticPhase.None; resultUntil = 0;
            if (revival) revival.SetInspection(RepairDiagnosticPhase.None, 0);
            ResetPlay();
        }
        void ResetPlay()
        {
            if (!owner) return;
            foreach (var mount in owner.GetComponentsInChildren<RepairPartMount>(true)) mount.InspectionPlay = 0;
        }
        void OnDisable() => Cancel();

        string Summary()
        {
            text.Clear(); text.Append(Status);
            if (!IsRunning && Passed) return text.ToString();
            int count = Mathf.Min(3, faults.Count);
            for (int i = 0; i < count; i++)
            {
                var fault = faults[i];
                text.Append('\n').Append(SalvageField.PartName(fault.prefab)).Append(": ");
                text.Append(fault.reason == Reason.Missing ? T("MISSING", "НЕТ ДЕТАЛИ") :
                    fault.reason == Reason.Installing ? T("FITTING", "УСТАНОВКА") :
                    fault.reason == Reason.Unseated ? T("SEAT WITH HAMMER", "ПОДБЕЙТЕ МОЛОТКОМ") : T("TIGHTEN FASTENERS", "ЗАТЯНИТЕ КРЕПЛЕНИЯ"));
                if (fault.reason == Reason.Fasteners) text.Append(" ×").Append(fault.count);
            }
            if (faults.Count > count) text.Append('\n').Append(T("More affected modules: ", "Ещё неисправных узлов: ")).Append(faults.Count - count);
            return text.ToString();
        }

        void OnGUI()
        {
            if ((!IsRunning && Time.time >= resultUntil) || !owner || owner.IsBeingCollected || GameSettingsMenu.IsOpen || DeveloperConsole.IsOpen) return;
            var camera = Camera.main; if (!camera) return;
            var player = camera.GetComponentInParent<FirstPersonController>();
            if (player && !player.isActiveAndEnabled) return;
            var bounds = RepairPartMount.GeometryBounds(owner.transform);
            if (Vector3.Distance(camera.transform.position, bounds.ClosestPoint(camera.transform.position)) > 5 * PlayerBody.Scale ||
                Vector3.Dot(camera.transform.forward, bounds.center - camera.transform.position) <= 0) return;
            InteractionHud.Prompt(Screen.width * .5f, Screen.height * .15f, Summary(), 15);
            if (Passed && !IsRunning) return;
            var old = GUI.color; GUI.color = new Color(1, .69f, .3f, .9f);
            for (int i = 0; i < Mathf.Min(3, faults.Count); i++)
            {
                if (!faults[i].visual) continue;
                var point = camera.WorldToScreenPoint(RepairPartMount.GeometryBounds(faults[i].visual.transform).center);
                if (point.z <= 0 || point.x < 8 || point.x > Screen.width - 8 || point.y < 8 || point.y > Screen.height - 8) continue;
                GUI.DrawTexture(new Rect(Mathf.Round(point.x) - 3, Mathf.Round(Screen.height - point.y) - 3, 6, 6), Texture2D.whiteTexture);
            }
            GUI.color = old;
        }
    }
}
