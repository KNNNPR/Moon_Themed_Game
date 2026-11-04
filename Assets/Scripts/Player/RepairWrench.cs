using UnityEngine;

namespace MoonThemedGame.Repair
{
    // Aim at a bolt and scroll up to screw it in, down to back it out; a click also gives one turn.
    public sealed class RepairWrench : RepairCarryable
    {
        const float Reach = 2.2f;
        RepairBolt target;
        float swing;

        protected override Vector3 HoldOffset => new Vector3(0.26f, -0.24f, 0.5f);
        public override string Prompt => "E — Take wrench";

        protected override void Carry()
        {
            target = FindBolt();
            int direction = 0;
            float scroll = Scroll();
            if (scroll > 0.01f || MouseClicked()) direction = 1;
            else if (scroll < -0.01f) direction = -1;
            if (target && direction != 0 && !(direction > 0 && target.Tight))
            {
                target.Turn(direction);
                swing = 1;
            }
            swing = Mathf.MoveTowards(swing, 0, Time.deltaTime * 5);
            transform.rotation *= Quaternion.Euler(0, 0, -40 * Mathf.Sin(swing * Mathf.PI));
        }

        RepairBolt FindBolt()
        {
            var ray = new Ray(hand.transform.position, hand.transform.forward);
            RepairBolt bolt = null;
            float nearest = Reach;
            foreach (var hit in Physics.RaycastAll(ray, Reach, ~0, QueryTriggerInteraction.Collide))
            {
                var candidate = hit.collider.GetComponent<RepairBolt>();
                if (!candidate && hit.collider.isTrigger) continue;
                if (hit.distance >= nearest) continue;
                nearest = hit.distance;
                bolt = candidate;
            }
            return bolt;
        }

        protected override string CarryHint => !target ? Loc.T("Aim at a bolt on a fitted part")
            : target.Tight ? Loc.T("Bolt tight")
            : Loc.F("Scroll up — tighten bolt  {0}/{1}", target.TurnsDone, RepairBolt.Turns);
    }
}
