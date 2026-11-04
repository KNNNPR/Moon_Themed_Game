using UnityEngine;

namespace MoonThemedGame.Repair
{
    // The hammer: tight-fitting parts (wheels, body panels) stick out when fitted; aim at one and
    // click to knock it home before its bolts or screws will turn.
    public sealed class RepairTool : RepairCarryable
    {
        public string displayName = "HAMMER";
        RepairRobotTarget robot;
        GameObject target;
        float swing, cooldown;

        static float Reach => 2.6f * PlayerBody.Scale;
        protected override bool AlignInHand => true;
        protected override Vector3 HoldOffset => new Vector3(0.26f, -0.22f, 0.5f);
        public override string Prompt => Loc.F("E — Take {0}", Loc.T(displayName));

        protected override void Carry()
        {
            var view = new Ray(hand.transform.position, hand.transform.forward);
            target = null;
            robot = null;
            foreach (var candidate in FindObjectsByType<RepairRobotTarget>(FindObjectsSortMode.None))
            {
                var found = candidate.FindProud(view, Reach);
                if (found) { target = found; robot = candidate; break; }
            }
            cooldown -= Time.deltaTime;
            if (MouseClicked() && cooldown <= 0)
            {
                swing = 1;
                cooldown = 0.35f;
                if (target) robot.Strike(target);
            }
            swing = Mathf.MoveTowards(swing, 0, Time.deltaTime * 4);
            float arc = Mathf.Sin(swing * Mathf.PI);
            transform.rotation = Quaternion.AngleAxis(-55 * arc, hand.transform.right) * transform.rotation;
            transform.position += hand.transform.forward * (0.08f * arc * PlayerBody.Scale);
        }

        protected override string CarryHint => target
            ? Loc.F("LMB — knock the part in  {0}/{1}", RepairRobotTarget.HammerHits - robot.HitsLeft(target), RepairRobotTarget.HammerHits)
            : Loc.T("Aim at a part that sticks out");
    }
}
