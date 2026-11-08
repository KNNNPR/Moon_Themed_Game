using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using MoonThemedGame.Repair;
using Cursor=UnityEngine.Cursor;

namespace MoonThemedGame.Interactions
{
    [DefaultExecutionOrder(10000),DisallowMultipleComponent]
    public sealed class OperatorWakeUp : MonoBehaviour
    {
        public string bedName="Plane";
        public MeshRenderer bed;
        public Transform lyingPose,sittingPose,standingPose;
        [Min(2)] public float duration=5.4f;
        [Range(-30,30)] public float standingPitch=12;
        public static OperatorWakeUp Active {get;private set;}
        public static bool IsPlaying=>Active && Active.playing;
        public float Progress=>Mathf.Clamp01(elapsed/Mathf.Max(2,duration));
        public Vector3 StandingPlayerPosition=>standingPlayerPosition;
        public Vector3 LyingEyePosition=>lyingPosition;
        public Vector3 StandingEyePosition=>standingPosition;
        static string requestedScene;

        FirstPersonController player;
        FirstPersonCameraEffects effects;
        PlayerBody body;
        Camera view;
        bool playing,restoreMovement,restoreEffects,restoreBody;
        float elapsed,originalFov,originalNear;
        Vector3 cameraLocalPosition,lyingPosition,sittingPosition,standingPosition,standingPlayerPosition;
        Quaternion cameraLocalRotation,lyingRotation,sittingRotation,standingRotation;
        readonly Dictionary<Renderer,bool> rendererStates=new Dictionary<Renderer,bool>();
        PanelSettings panel;
        GameObject overlay;
        VisualElement blackout,topLid,bottomLid;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset(){Active=null;requestedScene=null;SceneManager.sceneLoaded-=Loaded;}
        public static void Request(string scene)
        {
            requestedScene=scene;SceneManager.sceneLoaded-=Loaded;SceneManager.sceneLoaded+=Loaded;
        }
        static void Loaded(Scene scene,LoadSceneMode mode)
        {
            if(string.IsNullOrEmpty(requestedScene))return;
            if(!scene.name.Equals(requestedScene,StringComparison.OrdinalIgnoreCase)){requestedScene=null;return;}
            requestedScene=null;SceneManager.sceneLoaded-=Loaded;
            var sequence=FindFirstObjectByType<OperatorWakeUp>();
            if(!sequence)sequence=new GameObject("Operator / Wake Up From Bed").AddComponent<OperatorWakeUp>();
            sequence.Begin();
        }
        [ContextMenu("Preview Wake Up")]
        public void Begin()
        {
            if(playing || !Application.isPlaying)return;
            player=FindFirstObjectByType<FirstPersonController>();
            view=player?player.GetComponentInChildren<Camera>():null;
            if(!player || !view){Debug.LogError("Wake up requires the operator and their camera.",this);return;}
            if(!bed)
                foreach(var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                    if(renderer.name.Equals(bedName,StringComparison.OrdinalIgnoreCase)){bed=renderer;break;}
            if(!bed){Debug.LogError("Wake up bed '"+bedName+"' not found.",this);return;}
            Active=this;playing=true;elapsed=0;
            originalFov=view.fieldOfView;originalNear=view.nearClipPlane;
            cameraLocalPosition=view.transform.localPosition;cameraLocalRotation=view.transform.localRotation;
            restoreMovement=player.enabled;player.enabled=false;
            effects=view.GetComponentInParent<FirstPersonCameraEffects>();restoreEffects=effects && effects.enabled;if(effects)effects.enabled=false;
            ConfigurePoses();HideBody();BuildOverlay();
            Cursor.lockState=CursorLockMode.None;Cursor.visible=false;ApplyPose();
        }
        void Start(){if(playing)HideBody();}
        void ConfigurePoses()
        {
            var bounds=bed.bounds;standingPlayerPosition=player.transform.position;
            var capsule=player.GetComponent<CharacterController>();
            var origin=standingPlayerPosition+Vector3.up*2;
            float ground=float.NegativeInfinity;
            foreach(var hit in Physics.RaycastAll(origin,Vector3.down,12,~0,QueryTriggerInteraction.Ignore))
                if(!hit.transform.IsChildOf(player.transform) && hit.normal.y>.65f && hit.point.y<=standingPlayerPosition.y && hit.point.y>ground)ground=hit.point.y;
            if(capsule && !float.IsNegativeInfinity(ground))standingPlayerPosition.y+=ground-capsule.bounds.min.y+.02f;
            var outwards=Vector3.ProjectOnPlane(standingPlayerPosition-bounds.center,Vector3.up).normalized;
            if(outwards.sqrMagnitude<.1f)outwards=-bed.transform.forward;
            float yaw=Quaternion.LookRotation(outwards,Vector3.up).eulerAngles.y;
            standingPosition=standingPlayerPosition+(view.transform.position-player.transform.position);
            standingRotation=Quaternion.Euler(standingPitch,yaw,0);
            var lengthAxis=bounds.size.x>=bounds.size.z?Vector3.right:Vector3.forward;
            float halfLength=Mathf.Max(bounds.extents.x,bounds.extents.z);
            var head=bounds.center-lengthAxis*(halfLength*.52f);
            float edgeDistance=1/Mathf.Max(Mathf.Abs(outwards.x)/Mathf.Max(.01f,bounds.extents.x),Mathf.Abs(outwards.z)/Mathf.Max(.01f,bounds.extents.z));
            var edge=bounds.center+outwards*edgeDistance*.82f;
            // Query the real mattress triangles, rather than a box around its headboard.
            var meshCollider=bed.GetComponent<MeshCollider>();bool temporary=!meshCollider;
            if(!meshCollider){meshCollider=bed.gameObject.AddComponent<MeshCollider>();meshCollider.sharedMesh=bed.GetComponent<MeshFilter>().sharedMesh;}
            Physics.SyncTransforms();
            float Surface(Vector3 point)=>meshCollider.Raycast(new Ray(new Vector3(point.x,bounds.max.y+2,point.z),Vector3.down),out var hit,bounds.size.y+4)?hit.point.y:bounds.center.y;
            lyingPosition=new Vector3(head.x,Surface(head)+.34f,head.z);
            sittingPosition=new Vector3(edge.x,Surface(edge)+1.05f,edge.z);
            if(temporary)Destroy(meshCollider);
            lyingRotation=Quaternion.Euler(-78,yaw,-8);sittingRotation=Quaternion.Euler(22,yaw+7,3);
            if(lyingPose){lyingPosition=lyingPose.position;lyingRotation=lyingPose.rotation;}
            if(sittingPose){sittingPosition=sittingPose.position;sittingRotation=sittingPose.rotation;}
            if(standingPose)
            {
                var offset=standingPosition-standingPlayerPosition;
                standingPosition=standingPose.position;standingRotation=standingPose.rotation;standingPlayerPosition=standingPosition-offset;
            }
        }
        void HideBody()
        {
            if(!player)return;
            if(!body){body=player.GetComponent<PlayerBody>();restoreBody=body && body.enabled;if(body)body.enabled=false;}
            foreach(var renderer in player.GetComponentsInChildren<Renderer>(true))
            {if(!rendererStates.ContainsKey(renderer))rendererStates.Add(renderer,renderer.enabled);renderer.enabled=false;}
        }
        void BuildOverlay()
        {
            var assets=Resources.Load<SettingsMenuAssets>("SettingsMenuAssets");
            panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=assets.theme;
            panel.scaleMode=PanelScaleMode.ConstantPixelSize;
            overlay=new GameObject("Wake Up / eyelids");overlay.transform.SetParent(transform,false);
            var document=overlay.AddComponent<UIDocument>();document.panelSettings=panel;document.sortingOrder=2000;
            var root=document.rootVisualElement;root.pickingMode=PickingMode.Ignore;
            root.style.position=Position.Absolute;root.style.left=root.style.right=root.style.top=root.style.bottom=0;
            blackout=Black(root);blackout.style.top=blackout.style.bottom=0;
            topLid=Black(root);topLid.style.top=0;
            bottomLid=Black(root);bottomLid.style.bottom=0;
        }
        static VisualElement Black(VisualElement root)
        {
            var element=new VisualElement {pickingMode=PickingMode.Ignore};element.style.position=Position.Absolute;
            element.style.left=element.style.right=0;element.style.backgroundColor=Color.black;root.Add(element);return element;
        }
        void Update()
        {
            if(!playing || DeveloperConsole.IsOpen)return;
            elapsed+=Time.deltaTime;
        }
        void LateUpdate()
        {
            if(!playing)return;
            HideBody();ApplyPose();if(Progress>=1)Finish();
        }
        static float Ease(float x)=>Mathf.SmoothStep(0,1,Mathf.Clamp01(x));
        void ApplyPose()
        {
            if(!view)return;
            float t=Progress;
            Vector3 position;Quaternion rotation;
            if(t<.22f)
            {
                float motion=ComfortSettings.CameraShake?1:0;
                position=lyingPosition+Vector3.up*(Mathf.Sin(t*35)*.008f*motion);rotation=lyingRotation*Quaternion.Euler(Mathf.Sin(t*22)*.45f*motion,0,0);
            }
            else if(t<.55f)
            {
                float u=Ease((t-.22f)/.33f);position=Vector3.Lerp(lyingPosition,sittingPosition,u)+Vector3.up*(Mathf.Sin(u*Mathf.PI)*.12f);
                rotation=Quaternion.Slerp(lyingRotation,sittingRotation,u);
            }
            else
            {
                float u=Ease((t-.55f)/.37f);position=Vector3.Lerp(sittingPosition,standingPosition,u)-Vector3.up*(Mathf.Sin(u*Mathf.PI)*.08f);
                rotation=Quaternion.Slerp(sittingRotation,standingRotation,u)*Quaternion.Euler(0,0,ComfortSettings.CameraShake?Mathf.Sin(u*Mathf.PI*2)*1.2f*(1-u):0);
            }
            view.transform.SetPositionAndRotation(position,rotation);view.fieldOfView=Mathf.Lerp(originalFov-7*ComfortSettings.DynamicFov,originalFov,Ease((t-.18f)/.74f));view.nearClipPlane=.025f;
            if(blackout!=null)
            {
                blackout.style.opacity=1-Ease(elapsed/.4f);
                float blink=elapsed>.85f && elapsed<1.08f?Mathf.Sin((elapsed-.85f)/.23f*Mathf.PI)*.16f:0;
                float covered=Mathf.Clamp01(1-Ease((elapsed-.15f)/1.05f)+blink)*50;
                topLid.style.height=Length.Percent(covered);bottomLid.style.height=Length.Percent(covered);
            }
        }
        public void Finish()
        {
            if(!playing)return;playing=false;
            if(view){view.transform.localPosition=cameraLocalPosition;view.transform.localRotation=cameraLocalRotation;view.fieldOfView=originalFov;view.nearClipPlane=originalNear;}
            if(player)
            {
                var angles=standingRotation.eulerAngles;
                player.SetViewPose(standingPlayerPosition,angles.y,Mathf.DeltaAngle(0,angles.x));
                foreach(var entry in rendererStates)if(entry.Key)entry.Key.enabled=entry.Value;
                if(body)body.enabled=restoreBody;if(effects)effects.enabled=restoreEffects;player.enabled=restoreMovement;
            }
            if(overlay)Destroy(overlay);if(panel)Destroy(panel);overlay=null;panel=null;
            Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;if(Active==this)Active=null;
        }
        void OnDisable(){if(playing)Finish();}
        void OnDestroy(){if(Active==this)Active=null;}
    }
}
