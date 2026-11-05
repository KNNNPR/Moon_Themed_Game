using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Interactions
{
    [DisallowMultipleComponent]
    public sealed class AppleStation : Interactable
    {
        public AppleStationAssets assets;
        public Transform dropPoint;
        [Min(0.1f)] public float dispenseCooldown=1;
        [Min(1)] public int maximumNearbyApples=6;
        readonly List<HeldProp> dispensed=new List<HeldProp>();
        float nextDispense;
        public override string Prompt => !assets ? "Apple Station" : Time.time<nextDispense ? Loc.T("DISPENSING...") :
            NearbyCount>=maximumNearbyApples ? Loc.T("Collect the apples first") :
            !TrayIsClear ? Loc.T("Clear the tray") :
            Loc.F("[{0}] Dispense Suspicious Apple",GameControls.Label(GameCommand.Interact));
        public Vector3 DropPosition => dropPoint ? dropPoint.position : transform.TransformPoint(assets.localDropPosition);
        public Vector3 OutwardDirection => transform.TransformDirection(assets.localOutwardDirection).normalized;
        int NearbyCount
        {
            get
            {
                dispensed.RemoveAll(p=>!p || p.IsHeld || Vector3.Distance(p.transform.position,DropPosition)>4);
                return dispensed.Count;
            }
        }
        public bool TrayIsClear
        {
            get
            {
                if(!assets || !assets.applePrefab)return false;
                var box=assets.applePrefab.GetComponent<BoxCollider>();
                if(!box)return false;
                Vector3 half=Vector3.Scale(box.size,assets.applePrefab.transform.localScale)*0.48f;
                return !Physics.CheckBox(DropPosition,half,assets.applePrefab.transform.rotation,
                    Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            }
        }
        public override void Interact()=>TryDispense(out _);
        public bool TryDispense(out HeldProp apple)
        {
            apple=null;
            if(!isActiveAndEnabled || !assets || !assets.applePrefab ||
                GameSettingsMenu.BlockGameplayInput || Time.time<nextDispense ||
                NearbyCount>=maximumNearbyApples || !TrayIsClear)return false;
            var fruit=Instantiate(assets.applePrefab);
            fruit.name="Suspicious Apple";
            SceneManager.MoveGameObjectToScene(fruit,gameObject.scene);
            var renderer=fruit.GetComponent<Renderer>();
            fruit.transform.position+=DropPosition-renderer.bounds.center;
            apple=fruit.GetComponent<HeldProp>();
            dispensed.Add(apple);nextDispense=Time.time+dispenseCooldown;
            var animator=GetComponent<Animator>();
            float shakeDuration=0;
            if(animator && animator.runtimeAnimatorController)
            {
                foreach(var clip in animator.runtimeAnimatorController.animationClips)
                    if(clip.name=="shake")shakeDuration=clip.length;
                animator.Play("Base Layer.shake",0,0);
            }
            // The moving tray must finish shaking before a physical apple lands in it.
            if(shakeDuration>0)fruit.SetActive(false);
            StartCoroutine(Release(fruit.GetComponent<Rigidbody>(),shakeDuration));
            return true;
        }
        IEnumerator Release(Rigidbody body,float delay)
        {
            if(delay>0)
            {
                yield return new WaitForSeconds(delay);
                if(!body)yield break;
                body.gameObject.SetActive(true);
                var renderer=body.GetComponent<Renderer>();
                body.transform.position+=DropPosition-renderer.bounds.center;
            }
            // HeldProp.Start sleeps new props. Release after it has initialized.
            yield return null;
            if(!body || body.isKinematic)yield break;
            body.WakeUp();
            body.linearVelocity=Vector3.down*0.65f+OutwardDirection*0.04f;
            body.angularVelocity=Vector3.Cross(Vector3.up,OutwardDirection)*0.6f;
        }
        public void Configure(AppleStationAssets definition)
        {
            assets=definition;interactionDistance=3;
            if(assets.animatorController)
            {
                var animator=GetComponent<Animator>();
                if(!animator)animator=gameObject.AddComponent<Animator>();
                if(!animator.runtimeAnimatorController)animator.runtimeAnimatorController=assets.animatorController;
            }
            if(!dropPoint)
            {
                dropPoint=new GameObject("Suspicious Apple drop point").transform;
                dropPoint.SetParent(transform,false);dropPoint.localPosition=assets.localDropPosition;
            }
            // A full bounding box would seal the recessed tray. Replace only that box.
            var filter=GetComponent<MeshFilter>();
            foreach(var box in GetComponents<BoxCollider>())
                if(filter && (box.size-filter.sharedMesh.bounds.size).sqrMagnitude<0.000001f &&
                    (box.center-filter.sharedMesh.bounds.center).sqrMagnitude<0.000001f)box.enabled=false;
            var collision=GetComponent<MeshCollider>();
            if(!collision)collision=gameObject.AddComponent<MeshCollider>();
            collision.sharedMesh=assets.collisionMesh;
        }
        static void Install(Scene scene,LoadSceneMode mode)
        {
            var definition=Resources.Load<AppleStationAssets>("AppleStationAssets");
            if(!definition || !definition.stationMesh)return;
            foreach(var root in scene.GetRootGameObjects())
                foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
                    if(filter.sharedMesh==definition.stationMesh)
                    {
                        var station=filter.GetComponent<AppleStation>();
                        if(!station)station=filter.gameObject.AddComponent<AppleStation>();
                        station.Configure(definition);
                    }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRegistration(){SceneManager.sceneLoaded-=Install;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register(){SceneManager.sceneLoaded-=Install;SceneManager.sceneLoaded+=Install;}
    }
}
