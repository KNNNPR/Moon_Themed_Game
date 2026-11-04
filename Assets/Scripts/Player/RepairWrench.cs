using UnityEngine;

namespace MoonThemedGame.Repair
{
    // Wrench for bolts or screwdriver for screws: aim at one and scroll up to drive it in,
    // down to back it out; a click also gives one turn.
    public sealed class RepairWrench : RepairCarryable
    {
        public Fastener drives = Fastener.Bolt;
        public string displayName = "WRENCH";
        RepairBolt target;
        float swing;

        static float Reach => 2.2f * PlayerBody.Scale;
        protected override bool AlignInHand => true;
        protected override Vector3 HoldOffset => new Vector3(0.26f, -0.24f, 0.5f);
        public override string Prompt => Loc.F("E — Take {0}", Loc.T(displayName));

        protected override void Carry()
        {
            target = RepairBolt.Aimed(new Ray(hand.transform.position, hand.transform.forward), Reach);
            int direction = 0;
            float scroll = Scroll();
            if (scroll > 0.01f || MouseClicked()) direction = 1;
            else if (scroll < -0.01f) direction = -1;
            if (target && target.Kind == drives && target.Seated && direction != 0 && !(direction > 0 && target.Tight))
            {
                target.Turn(direction);
                swing = 1;
            }
            swing = Mathf.MoveTowards(swing, 0, Time.deltaTime * 5);
            var length = LongAxis();
            var axis = drives == Fastener.Screw ? length : length == Vector3.forward ? Vector3.up : Vector3.forward;
            transform.rotation *= Quaternion.AngleAxis(-40 * Mathf.Sin(swing * Mathf.PI), axis);
        }

        protected override string CarryHint
        {
            get
            {
                string what = Loc.T(drives == Fastener.Bolt ? "bolt" : "screw");
                if (!target) return Loc.F("Aim at a {0} on a fitted part", what);
                if (target.Kind != drives) return Loc.T(target.Kind == Fastener.Bolt ? "That's a bolt — take the wrench" : "That's a screw — take the screwdriver");
                if (!target.Seated) return Loc.T("The part sticks out — knock it in with the hammer first");
                if (target.Tight) return Loc.T(drives == Fastener.Bolt ? "Bolt tight" : "Screw tight");
                return Loc.F("Scroll up — tighten {0}  {1}/{2}", what, target.TurnsDone, target.Turns);
            }
        }
    }
}
