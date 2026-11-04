using UnityEngine;

namespace MoonThemedGame.Repair
{
    // A hand tool from the belt with no job of its own yet (screwdriver, hammer).
    public sealed class RepairTool : RepairCarryable
    {
        public string displayName = "TOOL";

        protected override bool AlignInHand => true;
        protected override Vector3 HoldOffset => new Vector3(0.26f, -0.24f, 0.5f);
        public override string Prompt => Loc.F("E — Take {0}", Loc.T(displayName));
    }
}
