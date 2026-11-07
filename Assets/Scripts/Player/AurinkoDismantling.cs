using System.Collections;
using System.Collections.Generic;
using MoonThemedGame.Computer;
using MoonThemedGame.Interactions;
using MoonThemedGame.Scavenger;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    public sealed class AurinkoDismantling : MonoBehaviour
    {
        RepairRobotTarget owner;
        readonly List<AurinkoModule> modules=new List<AurinkoModule>();
        readonly HashSet<AurinkoModule> removed=new HashSet<AurinkoModule>();
        bool prepared,collected,interactionCollisionsReady;
        public bool Complete=>prepared&&modules.Count>0&&removed.Count==modules.Count;
        public int Remaining=>modules.Count-removed.Count;
        public string Status=>Complete?(Loc.Russian?"Разборка завершена. [E] Вызвать кран на красной кнопке":"Dismantling complete. [E] Call the crane at the red button"):
            (Loc.Russian?$"AURINKO / Снять деталей: {Remaining}\nОткрутите крепления и [{GameControls.Label(GameCommand.Interact)}] снимите деталь.":$"AURINKO / Modules to remove: {Remaining}\nLoosen fasteners, then [{GameControls.Label(GameCommand.Interact)}] remove the module.");
        public void Configure(RepairRobotTarget target)
        {
            owner=target;gameObject.name="SCAV_BOT_AURINKO";
            var test=GetComponent<RepairRobotFinalTest>();if(test){test.Cancel();test.enabled=false;}
            var revival=GetComponent<RepairRobotRevival>();if(revival){revival.StopTests();revival.enabled=false;}
            StationStoryFlow.Active.BeginAurinko(this);
        }
        void Update()
        {
            if(!owner||!owner.IsSettled)return;
            if(!interactionCollisionsReady)RestoreInteractionCollisions();
            if(prepared)return;
            prepared=true;
            // The chassis remains on the stand when its wheels are removed.
            var drive=GetComponent<ScavengerDrive>();if(drive)drive.enabled=false;
            foreach(var slot in RepairRobotTarget.FindSlots(transform,owner.Definition.parts))
            {
                var geometry=RepairPartMount.GeometryBounds(slot.visual.transform);
                var bolts=RepairBolt.Spawn(owner,slot.visual,geometry.center+(geometry.center-transform.position).normalized*5+Vector3.up*2,RepairBolt.KindFor(slot.visual));
                foreach(var bolt in bolts)bolt.PrepareForDismantling();
                var mount=slot.visual.GetComponent<RepairPartMount>();if(!mount)mount=slot.visual.AddComponent<RepairPartMount>();mount.BeginSecured(owner,bolts,geometry);
                var module=slot.visual.AddComponent<AurinkoModule>();module.Setup(this,owner.Definition.parts[slot.part].prefab,bolts);modules.Add(module);
            }
        }
        void RestoreInteractionCollisions()
        {
            // Drive.Setup disables the model colliders and substitutes one broad hull.
            // Once the delivery is supported, interaction needs the actual part surfaces.
            foreach(var hull in GetComponents<BoxCollider>())hull.enabled=false;
            foreach(var collider in GetComponentsInChildren<MeshCollider>())
                if(collider.sharedMesh&&!collider.isTrigger&&!collider.GetComponentInParent<RepairBolt>())
                {collider.convex=false;collider.enabled=true;}
            foreach(var slot in RepairRobotTarget.FindSlots(transform,owner.Definition.parts))
                foreach(var mesh in slot.visual.GetComponentsInChildren<MeshFilter>())
                {
                    if(!mesh.sharedMesh||mesh.GetComponentInParent<RepairBolt>())continue;
                    var collider=mesh.GetComponent<MeshCollider>();
                    if(!collider){collider=mesh.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=mesh.sharedMesh;}
                    collider.enabled=true;
                }
            interactionCollisionsReady=true;
            Physics.SyncTransforms();
        }
        internal void Removed(AurinkoModule module)
        {
            if(!removed.Add(module))return;
            if(Complete)StationStoryFlow.Active.Thank(this);
        }
        public void Collected()
        {
            if(collected||!Complete)return;collected=true;StationStoryFlow.Active.Collected(this);
        }
        public bool DebugDismantleAll()
        {
            if(!prepared||!owner||!owner.Accepts||collected)return false;
            foreach(var module in modules)if(module&&module.gameObject.activeSelf)module.RemoveImmediately();
            return Complete;
        }
    }
    public sealed class AurinkoModule : Interactable
    {
        AurinkoDismantling order;
        GameObject prefab;
        List<RepairBolt> bolts;
        bool removing,finished;
        public bool CanRemove
        {
            get{if(removing||bolts==null)return false;foreach(var b in bolts)if(b&&(b.Progress>.0001f||b.IsTurning))return false;return true;}
        }
        internal void Setup(AurinkoDismantling owner,GameObject source,List<RepairBolt> fasteners){order=owner;prefab=source;bolts=fasteners;interactionDistance=4;}
        public override bool CanInteract(Camera camera)
        {
            if(removing||!base.CanInteract(camera)||!order||!order.GetComponent<RepairRobotTarget>().Accepts)return false;
            if(!RepairCarryable.Current)return true;
            var body=camera?camera.GetComponentInParent<PlayerBody>():null;
            return CanRemove&&RepairCarryable.Current is RepairWrench&&body&&body.CanStowCarriedTool;
        }
        public override string Prompt=>CanRemove?$"[{GameControls.Label(GameCommand.Interact)}] "+(Loc.Russian?"Снять ":"Remove ")+SalvageField.PartName(prefab):
            (Loc.Russian?"Сначала открутите крепления":"Loosen the fasteners first");
        public override void Interact()
        {
            if(!CanRemove)return;
            if(RepairCarryable.Current)
            {
                var body=Camera.main?Camera.main.GetComponentInParent<PlayerBody>():null;
                if(!(RepairCarryable.Current is RepairWrench)||!body||!body.TryStowCarriedTool())return;
            }
            StartCoroutine(Remove());
        }
        IEnumerator Remove()
        {
            BeginRemoval();
            var start=transform.position;var normal=RepairBolt.Outward(order.GetComponent<RepairRobotTarget>(),gameObject,out float size);
            float age=0;while(age<.4f){age+=Time.deltaTime;transform.position=start+normal*(Mathf.SmoothStep(0,1,age/.4f)*size*.2f);yield return null;}
            FinishRemoval();
        }
        void BeginRemoval()
        {
            removing=true;SetHighlighted(false);
            var mount=GetComponent<RepairPartMount>();if(mount){mount.ResetMount();mount.enabled=false;}
            foreach(var collider in GetComponentsInChildren<Collider>())collider.enabled=false;
        }
        internal void RemoveImmediately()
        {
            if(finished)return;
            StopAllCoroutines();foreach(var bolt in bolts)if(bolt)bolt.DriveAngle(-1000);
            BeginRemoval();
            var normal=RepairBolt.Outward(order.GetComponent<RepairRobotTarget>(),gameObject,out float size);
            transform.position+=normal*(size*.2f);FinishRemoval();
        }
        void FinishRemoval()
        {
            if(finished)return;finished=true;
            var normal=RepairBolt.Outward(order.GetComponent<RepairRobotTarget>(),gameObject,out float size);
            var center=RepairPartMount.GeometryBounds(transform).center;
            var prop=RepairPartProp.Create(prefab,center,center.y-size);
            prop.transform.rotation=transform.rotation;prop.transform.localScale=transform.lossyScale;
            prop.transform.position+=center-prop.RendererBounds().center;
            prop.ReleaseFromDelivery(normal*.35f+Vector3.up*.1f);
            order.Removed(this);gameObject.SetActive(false);
        }
    }
}
