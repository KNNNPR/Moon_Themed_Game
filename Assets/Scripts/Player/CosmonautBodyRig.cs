using System.Collections.Generic;
using UnityEngine;
namespace MoonThemedGame.Repair
{
    // Animation is applied to the supplied deforming skeleton, not to rigid mesh pieces.
    public sealed class CosmonautBodyRig
    {
        sealed class Limb
        {
            public Transform upper,middle,end;
            public Vector3 restEnd;
            public Vector3 upperRestPosition;
            public Quaternion restRotation;
            public Quaternion handFrame;
            public Vector3 palm;
        }
        readonly Transform root,model,hips,spine;
        readonly Dictionary<Transform,Quaternion> rotations=new Dictionary<Transform,Quaternion>();
        readonly Vector3 hipsRest;
        readonly Vector3 modelRestPosition;
        readonly Limb leftArm,rightArm,leftLeg,rightLeg;
        Vector3 leftGoal,rightGoal;
        bool handsPlaced,wasGrounded=true;
        float airTime,lastVertical,landing;
        public float CrouchBlend {get;private set;}
        public float AirBlend {get;private set;}
        public float LandingCompression=>landing;
        public Transform Model=>model;
        public Vector3 RightPalm=>rightArm.end.TransformPoint(rightArm.palm);

        public CosmonautBodyRig(Transform root,CosmonautBodyAssets assets)
        {
            this.root=root;
            var instance=Object.Instantiate(assets.model,root);instance.name="Cosmonaut / player skin";model=instance.transform;
            float normalization=1.65f/Mathf.Max(.001f,assets.sourceEyeHeight);
            model.localScale=Vector3.one*normalization;model.localPosition=new Vector3(0,-assets.sourceFeetY*normalization,-.08f);model.localRotation=Quaternion.identity;
            modelRestPosition=model.localPosition;
            foreach(var animator in instance.GetComponentsInChildren<Animator>())animator.enabled=false;
            foreach(var collider in instance.GetComponentsInChildren<Collider>())collider.enabled=false;
            var bones=new Dictionary<string,Transform>();foreach(var t in instance.GetComponentsInChildren<Transform>()){bones[t.name]=t;rotations[t]=t.localRotation;}
            hips=bones["Bone"];spine=bones["Bone.001"];hipsRest=hips.localPosition;
            leftArm=Bind("Bone.006.L","Bone.007.L","Bone.008.L");rightArm=Bind("Bone.006.R","Bone.007.R","Bone.008.R");
            leftLeg=Bind("Bone.009.L","Bone.010.L","Bone.011.L");rightLeg=Bind("Bone.009.R","Bone.010.R","Bone.011.R");
            foreach(var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var original=skin.sharedMesh;
                var shadows=new GameObject("Cosmonaut / complete body shadows");shadows.transform.SetParent(skin.transform,false);
                var shadow=shadows.AddComponent<SkinnedMeshRenderer>();shadow.sharedMesh=original;shadow.bones=skin.bones;shadow.rootBone=skin.rootBone;shadow.localBounds=skin.localBounds;
                shadow.sharedMaterial=assets.material;shadow.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;shadow.updateWhenOffscreen=true;
                skin.sharedMesh=assets.firstPersonMesh;skin.sharedMaterial=assets.material;skin.updateWhenOffscreen=true;skin.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Limb Bind(string a,string b,string c)
            {
                var limb=new Limb{upper=bones[a],middle=bones[b],end=bones[c],upperRestPosition=bones[a].localPosition};
                limb.restEnd=root.InverseTransformPoint(limb.end.position);limb.restRotation=Quaternion.Inverse(root.rotation)*limb.end.rotation;
                if(bones.TryGetValue(c+"_end",out var tip))limb.palm=limb.end.InverseTransformPoint(Vector3.Lerp(limb.end.position,tip.position,.55f));
                var fingers=limb.palm.sqrMagnitude>.000001f?limb.palm.normalized:Vector3.up;
                var knuckles=Vector3.ProjectOnPlane(limb.end.InverseTransformDirection(root.forward),fingers).normalized;
                if(knuckles.sqrMagnitude<.01f)knuckles=Vector3.ProjectOnPlane(Vector3.right,fingers).normalized;
                limb.handFrame=Quaternion.LookRotation(fingers,knuckles);
                return limb;
            }
        }
        public void SetViewPitch(float pitch,float crouch=0)
        {
            // Lean the first-person torso behind the eyes when looking down.
            // Foot IK keeps the boots at their original stance beneath the capsule.
            float lean=Mathf.SmoothStep(0,1,Mathf.InverseLerp(20,75,pitch));
            // Crouching lowers the eyes farther than the shoulders; keep the collar
            // behind the camera even when looking up, without moving the capsule.
            float retreat=Mathf.Max(lean*.24f,Mathf.Clamp01(crouch)*.34f);
            model.localPosition=modelRestPosition-Vector3.forward*retreat;
        }
        public void Animate(float phase,float walk,Vector3 velocity,float crouch,bool grounded,float vertical,float dt)
        {
            foreach(var entry in rotations)entry.Key.localRotation=entry.Value;
            leftArm.upper.localPosition=leftArm.upperRestPosition;rightArm.upper.localPosition=rightArm.upperRestPosition;
            hips.localPosition=hipsRest;
            CrouchBlend=Mathf.MoveTowards(CrouchBlend,crouch,dt*5);
            AirBlend=Mathf.MoveTowards(AirBlend,grounded?0:1,dt*5);
            if(!grounded)airTime+=dt;
            if(grounded && !wasGrounded && airTime>.12f){landing=Mathf.Clamp01(Mathf.Abs(lastVertical)*.08f);airTime=0;}
            wasGrounded=grounded;lastVertical=vertical;landing=Mathf.MoveTowards(landing,0,dt*3);
            float unit=root.lossyScale.y;
            hips.localPosition+=hips.parent.InverseTransformVector(-root.up*((CrouchBlend*.58f+landing*.09f)*unit)-root.forward*(CrouchBlend*.07f*unit));
            spine.rotation=Quaternion.AngleAxis(CrouchBlend*14+landing*5,root.right)*spine.rotation;
            var direction=Vector3.ProjectOnPlane(velocity,root.up);if(direction.sqrMagnitude>.0001f)direction.Normalize();else direction=root.forward;
            float stride=(.17f+.08f*Mathf.Clamp01(velocity.magnitude/Mathf.Max(.01f,unit*5)))*walk*(1-CrouchBlend*.5f);
            Leg(leftLeg,phase);Leg(rightLeg,phase+Mathf.PI);
            void Leg(Limb limb,float p)
            {
                var foot=root.TransformPoint(limb.restEnd)+direction*(Mathf.Sin(p)*stride*unit);
                foot+=root.up*((Mathf.Max(0,Mathf.Cos(p))*.065f*walk+AirBlend*.18f)*unit);
                foot-=root.forward*(AirBlend*.1f*unit);
                Solve(limb,foot,root.forward);limb.end.rotation=root.rotation*Quaternion.AngleAxis(AirBlend*12,Vector3.right)*limb.restRotation;
            }
        }
        public void PoseHands(Vector3? left,Vector3? right,float phase,float walk,float dt,Quaternion? rightOrientation=null)
        {
            float unit=root.lossyScale.y;
            // The first-person torso retreats to keep the collar behind the crouched
            // camera. Bring the gripping shoulder slightly forward with the weapon.
            if(rightOrientation.HasValue)
            {
                var lean=root.forward*(CrouchBlend*.14f*unit);
                rightArm.upper.position+=lean;
                if(left.HasValue)leftArm.upper.position+=lean;
            }
            var a=left ?? leftArm.upper.position+(-root.up*.55f-root.right*.035f+root.forward*(.05f+Mathf.Sin(phase)*walk*.08f))*unit;
            var b=right ?? rightArm.upper.position+(-root.up*.55f+root.right*.035f+root.forward*(.05f-Mathf.Sin(phase)*walk*.08f))*unit;
            float t=1-Mathf.Exp(-dt*20);leftGoal=handsPlaced?Vector3.Lerp(leftGoal,a,t):a;
            rightGoal=handsPlaced && !rightOrientation.HasValue?Vector3.Lerp(rightGoal,b,t):b;handsPlaced=true;
            Pose(leftArm,leftGoal,-root.right*.6f-root.up*.5f-root.forward*.35f,left.HasValue,null);
            var rightPole=rightOrientation.HasValue?root.right*.2f-root.up-root.forward*.15f:root.right*.6f-root.up*.5f-root.forward*.35f;
            Pose(rightArm,rightGoal,rightPole,right.HasValue,rightOrientation);
            void Pose(Limb arm,Vector3 goal,Vector3 pole,bool grip,Quaternion? orientation)
            {
                if(!grip){Solve(arm,goal,pole);return;}
                if(orientation.HasValue)
                {
                    var rotation=orientation.Value*Quaternion.Inverse(arm.handFrame);
                    // Solve the wrist for the final palm orientation, then apply it once.
                    var wrist=goal-rotation*Vector3.Scale(arm.palm,arm.end.lossyScale);
                    float reach=(Vector3.Distance(arm.upper.position,arm.middle.position)+Vector3.Distance(arm.middle.position,arm.end.position))*.97f;
                    var offset=wrist-arm.upper.position;
                    // A first-person arm anchor follows the held weapon when the
                    // torso retreats from the camera. Keep limb lengths and gun framing.
                    if(offset.magnitude>reach)arm.upper.position+=offset.normalized*(offset.magnitude-reach);
                    Solve(arm,wrist,pole);arm.end.rotation=rotation;
                    return;
                }
                for(int i=0;i<3;i++)Solve(arm,goal-arm.end.TransformVector(arm.palm),pole);
            }
        }
        public void TwistRightWrist(Vector3 axis,float degrees)
        {
            rightArm.end.rotation=Quaternion.AngleAxis(degrees,axis)*rightArm.end.rotation;
            for(int i=0;i<3;i++)Solve(rightArm,rightGoal-rightArm.end.TransformVector(rightArm.palm),root.right*.6f-root.up*.5f-root.forward*.35f);
        }
        static void Solve(Limb limb,Vector3 target,Vector3 pole)
        {
            var origin=limb.upper.position;
            float upper=Vector3.Distance(origin,limb.middle.position),lower=Vector3.Distance(limb.middle.position,limb.end.position);
            var offset=target-origin;var direction=offset.sqrMagnitude>.000001f?offset.normalized:Vector3.down;
            float distance=Mathf.Clamp(offset.magnitude,Mathf.Abs(upper-lower)+.001f,(upper+lower)*.995f);
            var bend=Vector3.ProjectOnPlane(pole,direction).normalized;if(bend.sqrMagnitude<.1f)bend=Vector3.Cross(direction,Vector3.right).normalized;
            float along=(upper*upper+distance*distance-lower*lower)/(2*distance);
            var elbow=origin+direction*along+bend*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
            limb.upper.rotation=Quaternion.FromToRotation(limb.middle.position-origin,elbow-origin)*limb.upper.rotation;
            limb.middle.rotation=Quaternion.FromToRotation(limb.end.position-limb.middle.position,origin+direction*distance-limb.middle.position)*limb.middle.rotation;
        }
    }
}
