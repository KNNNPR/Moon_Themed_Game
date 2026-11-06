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
        RepairBolt working;
        float engage, cycle=1, turnStart;
        int turnDirection;
        public bool Engaged=>engage>.01f && working;
        public Vector3 WorkAxis=>working?working.transform.up:Vector3.up;
        public float WristAngle {get;private set;}
        public Vector3 HandleGrip
        {
            get
            {
                var mesh=GetComponent<MeshFilter>().sharedMesh;
                var axis=LongAxis();
                return transform.TransformPoint(mesh.bounds.center-axis*(Vector3.Dot(mesh.bounds.extents,axis)*.55f));
            }
        }

        static float Reach => 2.2f * PlayerBody.Scale;
        protected override bool AlignInHand => true;
        protected override Vector3 HoldOffset => new Vector3(0.26f, -0.24f, 0.5f);
        public override string Prompt => Loc.F("E — Take {0}", Loc.T(displayName));

        protected override void Carry()
        {
            target = RepairBolt.Aimed(new Ray(hand.transform.position, hand.transform.forward), Reach);
            int direction = 0;
            float scroll = Scroll();
            if (scroll > 0.01f || MouseHeld()) direction = 1;
            else if (scroll < -0.01f) direction = -1;
            bool valid=target && target.Kind==drives && target.Seated;
            if(cycle>=1 && valid)working=target;
            bool near=working && Vector3.Distance(working.transform.position,hand.transform.position)<1.15f*PlayerBody.Scale;
            engage=Mathf.MoveTowards(engage,valid && near?1:0,Time.deltaTime*7);
            cycle=Mathf.Min(1,cycle+Time.deltaTime/.54f);
            if (valid && direction != 0 && cycle>=1 && !target.IsTurning && !(direction > 0 && target.Tight) && !(direction<0 && target.TurnsDone==0))
            {
                working=target;
                turnStart=target.VisualTurns;
                turnDirection=direction;
                target.Turn(direction);
                cycle=0;
            }
            if(!Engaged)return;
            var filter=GetComponent<MeshFilter>();
            if(!filter || !filter.sharedMesh)return;
            var bounds=filter.sharedMesh.bounds;
            var length=LongAxis();
            var normal=working.transform.up;
            float turn=60*(working.VisualTurns-turnStart);
            float reset=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.68f,1,cycle));
            float angle=turn-60*turnDirection*reset;
            WristAngle=angle;
            Quaternion rotation;
            if(drives==Fastener.Screw)
                rotation=Quaternion.AngleAxis(angle,normal)*Quaternion.FromToRotation(length,-normal);
            else
            {
                var size=bounds.size;
                var thin=size.x<=size.y && size.x<=size.z?Vector3.right:size.y<=size.z?Vector3.up:Vector3.forward;
                var along=Vector3.ProjectOnPlane(working.transform.position-hand.transform.position,normal).normalized;
                if(along.sqrMagnitude<.01f)along=Vector3.Cross(normal,hand.transform.right).normalized;
                rotation=Quaternion.AngleAxis(angle,normal)*Quaternion.LookRotation(along,normal)*Quaternion.Inverse(Quaternion.LookRotation(length,thin));
            }
            // Rotate around the working end, never swing the whole tool into the fastener.
            var tip=bounds.center+length*(Vector3.Dot(bounds.extents,length)*.88f);
            var contact=working.transform.position+normal*((.003f+Mathf.Sin(reset*Mathf.PI)*.018f)*PlayerBody.Scale);
            var position=contact-rotation*Vector3.Scale(tip,transform.lossyScale);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position,position,engage),Quaternion.Slerp(transform.rotation,rotation,engage));
        }

        protected override void OnPutAway(){engage=0;cycle=1;working=null;WristAngle=0;}

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
