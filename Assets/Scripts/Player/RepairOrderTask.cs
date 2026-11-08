using System.Collections.Generic;
using MoonThemedGame.Interactions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MoonThemedGame.Repair
{
    public enum RepairOrderTaskKind { None, Wheel, Connector, Casing }

    [DefaultExecutionOrder(250),DisallowMultipleComponent]
    public sealed class RepairOrderTask : MonoBehaviour
    {
        sealed class Dent
        {
            public MeshRenderer original;
            public SkinnedMeshRenderer copy;
            public float weight=100;
        }
        static readonly List<RepairOrderTask> active=new List<RepairOrderTask>();
        readonly List<Dent> dents=new List<Dent>();
        public RepairSpecialStepSettings settings;
        public RepairOrderTaskKind Kind {get;private set;}
        public GameObject Part {get;private set;}
        public bool Complete {get;private set;}
        public bool Engaged {get;private set;}
        public bool Aligned {get;private set;}
        public float Alignment {get;private set;}=-.8f;
        public float Seating {get;private set;}
        public int Hits {get;private set;}
        int requiredHits=2;
        public int RequiredHits=>requiredHits;
        public bool Pending=>isActiveAndEnabled&&!Complete&&Part&&Kind!=RepairOrderTaskKind.None&&settings&&settings.enableSpecialRepairs;
        public bool PendingFor(GameObject part)=>Pending&&Part==part;
        RepairRobotTarget owner;
        int fittedFrame=-1;
        float response=1,releaseLookUntil;
        Vector3 pivot,axle;
        bool longWheel;
        static string T(string en,string ru)=>Loc.Russian?ru:en;

        internal void Configure(RepairRobotTarget robot,IReadOnlyList<RepairRobotTarget.Slot> slots,int orderNumber,int[] missing)
        {
            ResetTask();owner=robot;settings=Resources.Load<RepairSpecialStepSettings>("RepairSpecialStepSettings");
            if(!settings||!settings.enableSpecialRepairs||robot.IsDismantling||missing==null)return;
            requiredHits=Mathf.Max(1,settings.panelHits);
            var preferred=(RepairOrderTaskKind)(1+Mathf.Abs(orderNumber-1)%3);
            // Work with an actually missing slot; never invent extra damage or parts.
            for(int pass=0;pass<3&&!Part;pass++)
            {
                var desired=pass==0?preferred:pass==1?RepairOrderTaskKind.Wheel:RepairOrderTaskKind.Connector;
                foreach(int index in missing)
                    if(index>=0&&index<slots.Count&&slots[index].visual&&!slots[index].visual.activeSelf&&Classify(slots[index].visual)==desired)
                    {Part=slots[index].visual;Kind=desired;break;}
            }
            if(!Part)foreach(int index in missing)
                if(index>=0&&index<slots.Count&&slots[index].visual&&!slots[index].visual.activeSelf)
                {Part=slots[index].visual;Kind=Classify(Part);break;}
        }
        static RepairOrderTaskKind Classify(GameObject part)
        {
            string label=part.name.ToLowerInvariant();
            return label.Contains("wheel")?RepairOrderTaskKind.Wheel:label.Contains("body")?RepairOrderTaskKind.Casing:RepairOrderTaskKind.Connector;
        }
        public string Instruction=>Kind==RepairOrderTaskKind.Casing?
            T("[LMB] Straighten the dent with the hammer","[ЛКМ] Выровняйте вмятину молотком")+$"  {Hits}/{RequiredHits}":
            Kind==RepairOrderTaskKind.Wheel&&Aligned?T("[LMB + MOUSE UP] Push wheel onto axle","[ЛКМ + МЫШЬ ВВЕРХ] Посадите колесо на ось")+$"  {Mathf.RoundToInt(Seating*100)}%":
            T("[LMB + MOUSE LEFT/RIGHT] Align the marks","[ЛКМ + МЫШЬ ВЛЕВО/ВПРАВО] Совместите метки");
        public string Description=>Kind==RepairOrderTaskKind.Casing?T("CASING [STRAIGHTEN DENT]","КОЖУХ [ВЫРОВНЯТЬ ВМЯТИНУ]"):
            Kind==RepairOrderTaskKind.Wheel?T("WHEEL [ALIGN AND SEAT]","КОЛЕСО [СОВМЕСТИТЬ И ПОСАДИТЬ]"):
            T("CONNECTOR [ALIGN MARKS]","РАЗЪЁМ [СОВМЕСТИТЬ МЕТКИ]");

        internal void Fitted()
        {
            if(!Pending)return;
            fittedFrame=Time.frameCount;
            var bounds=RepairPartMount.GeometryBounds(Part.transform);pivot=Part.transform.InverseTransformPoint(bounds.center);
            axle=Vector3.right;
            foreach(var mesh in Part.GetComponentsInChildren<MeshFilter>())
            {
                if(!mesh.sharedMesh||mesh.GetComponentInParent<RepairBolt>())continue;
                var size=mesh.sharedMesh.bounds.size;
                var direction=size.x<=size.y&&size.x<=size.z?Vector3.right:size.y<=size.z?Vector3.up:Vector3.forward;
                axle=Part.transform.InverseTransformDirection(mesh.transform.TransformDirection(direction)).normalized;
                float min=Mathf.Min(size.x,Mathf.Min(size.y,size.z)),max=Mathf.Max(size.x,Mathf.Max(size.y,size.z));
                longWheel=max>(size.x+size.y+size.z-min-max)*2.25f;break;
            }
            if(Kind==RepairOrderTaskKind.Casing)CreateDents();
        }
        void CreateDents()
        {
            foreach(var mesh in Part.GetComponentsInChildren<MeshFilter>())
            {
                if(mesh.GetComponentInParent<RepairBolt>()||!mesh.TryGetComponent<MeshRenderer>(out var renderer)||!renderer.enabled)continue;
                var blend=settings.DentFor(mesh.sharedMesh);if(!blend)continue;
                var go=new GameObject("Repair casing / dent");go.layer=mesh.gameObject.layer;go.transform.SetParent(mesh.transform,false);
                var copy=go.AddComponent<SkinnedMeshRenderer>();copy.sharedMesh=blend;copy.sharedMaterials=renderer.sharedMaterials;
                copy.localBounds=blend.bounds;copy.updateWhenOffscreen=true;copy.shadowCastingMode=renderer.shadowCastingMode;copy.receiveShadows=renderer.receiveShadows;
                var properties=new MaterialPropertyBlock();renderer.GetPropertyBlock(properties);copy.SetPropertyBlock(properties);
                for(int i=0;i<copy.sharedMaterials.Length;i++){renderer.GetPropertyBlock(properties,i);copy.SetPropertyBlock(properties,i);}
                copy.SetBlendShapeWeight(0,100);renderer.enabled=false;dents.Add(new Dent{original=renderer,copy=copy});
            }
        }
        internal void HammerHit()
        {
            if(!Pending||Kind!=RepairOrderTaskKind.Casing)return;
            Hits++;if(Hits>=RequiredHits)FinishTask();
        }
        internal void ProcessMotion(Vector2 delta,bool held)
        {
            if(!Pending||Kind==RepairOrderTaskKind.Casing||!Part.activeSelf||
                Part.TryGetComponent<RepairPartMount>(out var mount)&&mount.IsInstalling)return;
            if(!held){Engaged=false;return;}
            Engaged=true;
            delta=Vector2.ClampMagnitude(delta*ComfortSettings.ToolSensitivity,50);
            if(!Aligned)
            {
                float previous=Alignment;
                Alignment=Mathf.Clamp(Alignment+delta.x/Mathf.Max(40,settings.alignmentPixels),-1,1);
                // A wide snap zone and crossing assistance avoid precision hunting.
                if(Mathf.Abs(Alignment)<=settings.alignmentTolerance||previous<0&&Alignment>0)
                {Alignment=0;Aligned=true;if(Kind==RepairOrderTaskKind.Connector)FinishTask();}
                return;
            }
            if(Kind==RepairOrderTaskKind.Wheel)
            {
                Seating=Mathf.Clamp01(Seating+Mathf.Max(0,delta.y)/Mathf.Max(30,settings.seatingPixels));
                if(Seating>=.999f)FinishTask();
            }
        }
        internal bool Pose(Vector3 home,Quaternion rotation,Vector3 scale,Vector3 outward,float size,out Vector3 position,out Quaternion orientation)
        {
            position=home;orientation=rotation;
            if(!Part||Kind==RepairOrderTaskKind.None||fittedFrame<0||response<=.001f)return false;
            float tilt=Kind==RepairOrderTaskKind.Casing?6*(1-Hits/(float)RequiredHits):
                Kind==RepairOrderTaskKind.Wheel?Alignment*(longWheel?4:22):Alignment*9;
            var turn=Quaternion.AngleAxis(tilt*response,Kind==RepairOrderTaskKind.Wheel?axle:Vector3.up);
            orientation=rotation*turn;
            position=home+rotation*Vector3.Scale(pivot,scale)-orientation*Vector3.Scale(pivot,scale);
            float gap=Kind==RepairOrderTaskKind.Wheel?1-Seating:Kind==RepairOrderTaskKind.Casing?1-Hits/(float)RequiredHits:1;
            position+=outward*(size*.07f*response*gap);
            return true;
        }
        void FinishTask()
        {
            Complete=true;Engaged=false;Alignment=0;Seating=1;releaseLookUntil=Time.unscaledTime+.12f;
            if(owner)owner.SpecialSeatComplete(Part);
        }
        internal void DebugComplete(){if(Part){Hits=RequiredHits;FinishTask();response=0;RestoreDents();}}
        internal void ResetTask()
        {
            Engaged=false;Complete=false;Aligned=false;Alignment=-.8f;Seating=0;Hits=0;response=1;releaseLookUntil=0;fittedFrame=-1;
            Part=null;Kind=RepairOrderTaskKind.None;longWheel=false;RestoreDents();
        }
        void RestoreDents()
        {
            foreach(var dent in dents){if(dent.original)dent.original.enabled=true;if(dent.copy){dent.copy.gameObject.SetActive(false);Destroy(dent.copy.gameObject);}}
            dents.Clear();
        }
        internal void CancelToLegacy()
        {
            bool pending=Part&&!Complete&&fittedFrame>=0;var visual=Part;float remaining=Kind==RepairOrderTaskKind.Casing?1-Hits/(float)RequiredHits:1;
            ResetTask();if(pending&&owner&&!owner.IsDismantling)owner.RestoreNormalSeat(visual,remaining);
        }
        internal bool Aimed(Camera camera)=>CanUse(camera);
        bool CanUse(Camera camera,bool allowStow=false)
        {
            if(!Pending||Kind==RepairOrderTaskKind.Casing||!owner||!owner.Accepts||!Part.activeSelf||!camera||!camera.isActiveAndEnabled||
                GameSettingsMenu.BlockGameplayInput||PlayerBody.BeltOpen||HeldProp.Holding||Time.frameCount<=fittedFrame)return false;
            if(Part.TryGetComponent<RepairPartMount>(out var mount)&&mount.IsInstalling)return false;
            var player=camera.GetComponentInParent<FirstPersonController>();if(!player||!player.enabled||player.Noclip)return false;
            if(RepairCarryable.Current)
            {
                if(!allowStow||!(RepairCarryable.Current is RepairWrench||RepairCarryable.Current is RepairTool))return false;
                var body=player.GetComponent<PlayerBody>();if(!body||!body.CanStowCarriedTool)return false;
            }
            var offset=RepairPartMount.GeometryBounds(Part.transform).center-camera.transform.position;
            return offset.magnitude<=2.8f*PlayerBody.Scale&&Vector3.Angle(camera.transform.forward,offset)<20;
        }
        public static bool ConsumesLook
        {
            get
            {
                var mouse=Mouse.current;if(mouse==null||!mouse.leftButton.isPressed||mouse.rightButton.isPressed)return false;
                var camera=Camera.main;foreach(var task in active)
                    if(task&&(task.CanUse(camera,mouse.leftButton.wasPressedThisFrame)||task.releaseLookUntil>Time.unscaledTime&&task.owner&&task.owner.Accepts&&!GameSettingsMenu.BlockGameplayInput&&!RepairCarryable.Current&&!HeldProp.Holding))return true;
                return false;
            }
        }
        void Update()
        {
            if(Part&&settings&&!settings.enableSpecialRepairs){CancelToLegacy();return;}
            float target=Complete?0:1;response=Mathf.MoveTowards(response,target,Time.deltaTime*4);
            foreach(var dent in dents)
                if(dent.copy){float wanted=Complete?0:100*(1-Hits/(float)RequiredHits);dent.weight=Mathf.Lerp(dent.weight,wanted,1-Mathf.Exp(-Time.deltaTime*14));dent.copy.SetBlendShapeWeight(0,dent.weight);}
            if(Complete&&dents.Count>0&&dents.TrueForAll(d=>!d.copy||d.weight<.1f))RestoreDents();
            var mouse=Mouse.current;var camera=Camera.main;
            if(mouse!=null&&mouse.leftButton.wasPressedThisFrame&&!mouse.rightButton.isPressed&&RepairCarryable.Current&&CanUse(camera,true))
                camera.GetComponentInParent<PlayerBody>()?.TryStowCarriedTool();
            if(mouse==null||!CanUse(camera)||mouse.rightButton.isPressed){Engaged=false;return;}
            ProcessMotion(mouse.delta.ReadValue()*(900f/Mathf.Max(480,Screen.height)),mouse.leftButton.isPressed);
        }
        void OnGUI()
        {
            if(!Pending||!Part.activeSelf||!owner||!owner.Accepts||InteractionHud.Suppressed||GameSettingsMenu.BlockGameplayInput||PlayerBody.BeltOpen)return;
            var camera=Camera.main;if(!camera)return;
            var player=camera.GetComponentInParent<FirstPersonController>();if(!camera.isActiveAndEnabled||!player||!player.enabled||player.Noclip)return;
            var center=RepairPartMount.GeometryBounds(Part.transform).center;var point=camera.WorldToScreenPoint(center);
            if(point.z<=0||Vector3.Distance(center,camera.transform.position)>4*PlayerBody.Scale)return;
            var old=GUI.color;GUI.color=new Color(1,.75f,.3f);
            GUI.DrawTexture(new Rect(Mathf.Round(point.x)-3,Mathf.Round(Screen.height-point.y)-3,6,6),Texture2D.whiteTexture);
            if(Kind!=RepairOrderTaskKind.Casing&&CanUse(camera,true))
            {
                float x=Screen.width*.5f,y=Screen.height*.73f;GUI.color=new Color(.05f,.07f,.06f,.85f);GUI.DrawTexture(new Rect(x-85,y-5,170,14),Texture2D.whiteTexture);
                GUI.color=new Color(.6f,.9f,.7f);GUI.DrawTexture(new Rect(x-14,y-3,28,10),Texture2D.whiteTexture);
                GUI.color=Color.white;GUI.DrawTexture(new Rect(x+Alignment*75-2,y-7,4,18),Texture2D.whiteTexture);
                GUI.color=old;
                var interactor=player.GetComponent<PlayerInteractor>();
                if(!interactor||interactor.CurrentTarget!=owner)RepairHud.Hint(x,y+24,Instruction+(Engaged?"\n"+T("[RMB] Leave","[ПКМ] Отойти"):""));
            }
            GUI.color=old;
        }
        void OnEnable(){if(!active.Contains(this))active.Add(this);}
        void OnDisable(){active.Remove(this);if(gameObject.activeInHierarchy)CancelToLegacy();else{Engaged=false;RestoreDents();}}
        void OnDestroy()=>RestoreDents();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void ResetRegistry()=>active.Clear();
    }
}
