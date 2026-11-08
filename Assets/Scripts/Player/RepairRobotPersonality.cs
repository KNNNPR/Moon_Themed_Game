using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    public enum RepairTemperament { Cautious, Impatient, Watchful }

    // Render-only performance after a successful check. Never moves slots or colliders.
    [DefaultExecutionOrder(125), DisallowMultipleComponent]
    public sealed class RepairRobotPersonality : MonoBehaviour
    {
        [Range(0,2)] public float motionStrength=1;
        [Range(0,60)] public float cautiousWheelAngle=26;
        [Min(.3f)] public float cautiousWheelTime=.65f;
        [Min(1)] public float impatientDuration=3.4f;
        [Min(0)] public float gazeDelay=1.1f;
        [Min(1)] public float gazeSpeed=12;
        [Range(0,40)] public float gazeYawLimit=24;
        [Range(0,25)] public float gazePitchLimit=12;
        [Min(1)] public float gazeDistance=12;
        public RepairTemperament Temperament {get;private set;}
        public bool IsReacting {get;private set;}
        public int ReactionsCompleted {get;private set;}

        enum Role { Body, Wheel, Optic }
        sealed class Visual
        {
            public MeshFilter source;
            public MeshRenderer original,copy;
            public Transform proxy;
            public Role role;
            public int wheelIndex,materialCount;
            public readonly MaterialPropertyBlock block=new MaterialPropertyBlock();
        }
        sealed class Gear {public Vector3 pivot,axis;public bool longModule;}
        readonly List<Visual> visuals=new List<Visual>();
        readonly List<Gear> gearFrames=new List<Gear>();
        readonly List<GameObject> wheelSlots=new List<GameObject>();
        readonly List<GameObject> opticSlots=new List<GameObject>();
        RepairRobotTarget owner;
        RepairRobotRevival revival;
        FirstPersonController player;
        GameObject rig;
        Vector3 pivot,eyePivot,heading,bodyOffset;
        Quaternion bodyRotation=Quaternion.identity,gazeRotation=Quaternion.identity;
        float age,length,yaw,pitch;

        internal void Configure(RepairRobotTarget target,RepairRobotRevival effects,IReadOnlyList<RepairRobotTarget.Slot> slots)
        {
            StopReaction();owner=target;revival=effects;wheelSlots.Clear();opticSlots.Clear();
            Temperament=effects.Style==RepairRevivalStyle.Finder?RepairTemperament.Impatient:
                effects.Style==RepairRevivalStyle.Skate?RepairTemperament.Watchful:RepairTemperament.Cautious;
            foreach(var slot in slots)
            {
                string label=slot.visual.name.ToLowerInvariant();
                if(label.Contains("wheel"))wheelSlots.Add(slot.visual);
                else if(label.Contains("headlight"))opticSlots.Add(slot.visual);
            }
            ReactionsCompleted=0;
        }

        internal void BeginReaction()
        {
            StopReaction();
            if(!isActiveAndEnabled||!owner||!owner.Accepts||!owner.IsRepaired||!revival.Powered)return;
            var bounds=RepairPartMount.GeometryBounds(transform);pivot=bounds.center;length=Mathf.Max(.2f,bounds.size.magnitude*.65f);
            eyePivot=pivot;
            if(opticSlots.Count>0)
            {
                eyePivot=Vector3.zero;
                foreach(var optic in opticSlots)eyePivot+=RepairPartMount.GeometryBounds(optic.transform).center;
                eyePivot/=opticSlots.Count;
            }
            heading=eyePivot-pivot;heading.y=0;heading=heading.sqrMagnitude>.0001f?heading.normalized:transform.forward;
            foreach(var wheel in wheelSlots)
            {
                MeshFilter main=null;float largest=-1;
                foreach(var mesh in wheel.GetComponentsInChildren<MeshFilter>())
                {
                    if(!mesh.sharedMesh||mesh.GetComponentInParent<RepairBolt>())continue;
                    float size=Vector3.Scale(mesh.sharedMesh.bounds.size,mesh.transform.lossyScale).sqrMagnitude;
                    if(size>largest){main=mesh;largest=size;}
                }
                var frame=new Gear{pivot=wheel.transform.position,axis=transform.right};
                if(main)
                {
                    var size=main.sharedMesh.bounds.size;float minimum=Mathf.Min(size.x,Mathf.Min(size.y,size.z)),maximum=Mathf.Max(size.x,Mathf.Max(size.y,size.z));
                    var axis=size.x<=size.y&&size.x<=size.z?Vector3.right:size.y<=size.z?Vector3.up:Vector3.forward;
                    frame.pivot=main.transform.TransformPoint(main.sharedMesh.bounds.center);frame.axis=main.transform.TransformDirection(axis).normalized;
                    frame.longModule=maximum>(size.x+size.y+size.z-minimum-maximum)*2.25f;
                }
                gearFrames.Add(frame);
            }
            rig=new GameObject("Repair personality visuals");rig.transform.SetParent(transform,false);rig.layer=gameObject.layer;
            var sourceMeshes=GetComponentsInChildren<MeshFilter>();
            foreach(var mesh in sourceMeshes)
            {
                if(!mesh.sharedMesh||mesh.transform.IsChildOf(rig.transform)||
                    !mesh.TryGetComponent<MeshRenderer>(out var renderer)||!renderer.enabled||renderer.forceRenderingOff||revival.IsEffect(mesh.transform))continue;
                Role role=Role.Body;int wheelIndex=-1;
                for(int i=0;i<wheelSlots.Count;i++)if(mesh.transform.IsChildOf(wheelSlots[i].transform)){role=Role.Wheel;wheelIndex=i;break;}
                foreach(var optic in opticSlots)if(mesh.transform.IsChildOf(optic.transform)){role=Role.Optic;break;}
                var go=new GameObject("Personality / "+mesh.name);go.layer=mesh.gameObject.layer;go.transform.SetParent(rig.transform,false);
                go.AddComponent<MeshFilter>().sharedMesh=mesh.sharedMesh;
                var copy=go.AddComponent<MeshRenderer>();copy.sharedMaterials=renderer.sharedMaterials;
                copy.shadowCastingMode=renderer.shadowCastingMode;copy.receiveShadows=renderer.receiveShadows;
                copy.lightProbeUsage=renderer.lightProbeUsage;copy.reflectionProbeUsage=renderer.reflectionProbeUsage;
                copy.renderingLayerMask=renderer.renderingLayerMask;
                visuals.Add(new Visual{source=mesh,original=renderer,copy=copy,proxy=go.transform,role=role,wheelIndex=wheelIndex,
                    materialCount=copy.sharedMaterials.Length});
                renderer.enabled=false;
            }
            player=FindFirstObjectByType<FirstPersonController>();age=yaw=pitch=0;IsReacting=true;PoseVisuals();
        }

        void LateUpdate()
        {
            if(!IsReacting)return;
            if(!owner||!owner.Accepts||!owner.IsRepaired||!revival||!revival.Powered){StopReaction();return;}
            age+=Time.deltaTime;
            float duration=Temperament==RepairTemperament.Cautious?wheelSlots.Count*cautiousWheelTime+1:impatientDuration;
            if(Temperament!=RepairTemperament.Watchful&&age>=duration){ReactionsCompleted++;StopReaction();return;}
            bodyRotation=gazeRotation=Quaternion.identity;bodyOffset=Vector3.zero;
            if(Temperament==RepairTemperament.Impatient)
            {
                float kick=Kick(age);
                bodyOffset=heading*(length*.007f*kick*motionStrength)+transform.up*(length*.002f*Mathf.Abs(kick)*motionStrength);
                bodyRotation=Quaternion.AngleAxis(-2.2f*kick*motionStrength,Vector3.Cross(transform.up,heading));
            }
            if(Temperament==RepairTemperament.Watchful)UpdateGaze(Time.deltaTime);
            PoseVisuals();
        }

        static float Pulse(float time,float center,float width){float x=Mathf.Clamp01(1-Mathf.Abs(time-center)/width);return x*x*(3-2*x);}
        static float Kick(float time)=>Pulse(time,.5f,.3f)-.75f*Pulse(time,1.25f,.26f)+.55f*Pulse(time,2.1f,.36f);
        void UpdateGaze(float dt)
        {
            float wantedYaw=0,wantedPitch=0;
            if(age>=gazeDelay&&player&&player.gameObject.activeInHierarchy&&!player.Noclip)
            {
                Vector3 target=(player.CameraRoot?player.CameraRoot.position:player.transform.position+Vector3.up)-eyePivot;
                if(target.sqrMagnitude<gazeDistance*gazeDistance&&Vector3.Dot(target,heading)>0)
                {
                    var local=Quaternion.Inverse(Quaternion.LookRotation(heading,transform.up))*target;
                    wantedYaw=Mathf.Clamp(Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,-gazeYawLimit,gazeYawLimit);
                    wantedPitch=Mathf.Clamp(-Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg,-gazePitchLimit,gazePitchLimit);
                }
            }
            yaw=Mathf.MoveTowards(yaw,wantedYaw,gazeSpeed*dt);pitch=Mathf.MoveTowards(pitch,wantedPitch,gazeSpeed*.7f*dt);
            var frame=Quaternion.LookRotation(heading,transform.up);
            gazeRotation=frame*Quaternion.Euler(Mathf.Clamp(pitch*motionStrength,-gazePitchLimit,gazePitchLimit),
                Mathf.Clamp(yaw*motionStrength,-gazeYawLimit,gazeYawLimit),0)*Quaternion.Inverse(frame);
        }
        float WheelResponse(int index)
        {
            if(Temperament==RepairTemperament.Cautious)
            {
                float t=(age-index*cautiousWheelTime)/cautiousWheelTime;
                if(t<=0||t>=.78f)return 0;
                t/=.78f;
                return Mathf.Sin(t*Mathf.PI*2)*Mathf.Pow(Mathf.Sin(t*Mathf.PI),2)*cautiousWheelAngle;
            }
            return Temperament==RepairTemperament.Impatient?Kick(age-index*.11f)*34:0;
        }
        void PoseVisuals()
        {
            foreach(var visual in visuals)
            {
                if(!visual.source||!visual.proxy)continue;
                var source=visual.source.transform;var center=visual.source.sharedMesh.bounds.center;
                Quaternion rotation=source.rotation;Vector3 worldCenter=source.TransformPoint(center);
                if(visual.role==Role.Wheel)
                {
                    float response=WheelResponse(visual.wheelIndex)*motionStrength;
                    var gear=gearFrames[visual.wheelIndex];
                    var turn=Quaternion.AngleAxis(response*(gear.longModule ? .045f : 1),gear.axis);
                    rotation=turn*source.rotation;worldCenter=gear.pivot+turn*(worldCenter-gear.pivot);
                    if(gear.longModule)worldCenter+=heading*(length*.0002f*response);
                }
                else if(visual.role==Role.Optic)
                {
                    rotation=gazeRotation*rotation;worldCenter=eyePivot+gazeRotation*(worldCenter-eyePivot);
                }
                rotation=bodyRotation*rotation;worldCenter=BodyPoint(worldCenter);
                visual.proxy.SetPositionAndRotation(worldCenter-rotation*Vector3.Scale(center,source.lossyScale),rotation);
                visual.proxy.localScale=Divide(source.lossyScale,rig.transform.lossyScale);
                // Original optics are still updated by revival, so copy current instance emission too.
                visual.original.GetPropertyBlock(visual.block);visual.copy.SetPropertyBlock(visual.block);
                for(int i=0;i<visual.materialCount;i++){visual.original.GetPropertyBlock(visual.block,i);visual.copy.SetPropertyBlock(visual.block,i);}
            }
        }
        Vector3 BodyPoint(Vector3 point)=>pivot+bodyRotation*(point-pivot)+bodyOffset;
        internal void PoseEffect(Transform effect,bool optic)
        {
            if(!IsReacting||!effect)return;
            var point=effect.position;var rotation=effect.rotation;
            if(optic){point=eyePivot+gazeRotation*(point-eyePivot);rotation=gazeRotation*rotation;}
            effect.SetPositionAndRotation(BodyPoint(point),bodyRotation*rotation);
        }
        internal void StopReaction()
        {
            IsReacting=false;bodyRotation=gazeRotation=Quaternion.identity;bodyOffset=Vector3.zero;
            foreach(var visual in visuals)if(visual.original)visual.original.enabled=true;
            visuals.Clear();gearFrames.Clear();
            if(rig){rig.SetActive(false);Destroy(rig);rig=null;}
        }
        void OnDisable()=>StopReaction();
        void OnDestroy()=>StopReaction();
        static Vector3 Divide(Vector3 a,Vector3 b)=>new Vector3(a.x/Mathf.Max(.00001f,Mathf.Abs(b.x)),a.y/Mathf.Max(.00001f,Mathf.Abs(b.y)),a.z/Mathf.Max(.00001f,Mathf.Abs(b.z)));
    }
}
