using MoonThemedGame.Interactions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MoonThemedGame.Repair
{
    // Camera-relative first-person weapon. The projectile starts at the muzzle, not through walls at the eye.
    [DefaultExecutionOrder(4000), DisallowMultipleComponent]
    public sealed class PlayerPistol : RepairCarryable
    {
        public const int MagazineSize=8;
        public const float ReloadSeconds=2.8f;
        public Transform muzzle,frontSight,rearSight,rightGrip,leftGrip,magazine,slide;
        public Material effectMaterial,impactMaterial,casingMaterial;
        [Header("Editable reload animation")]
        public Transform reloadMotion;
        public AnimationClip reloadClip,emptyReloadClip;
        [Range(.5f,2)] public float reloadPlaybackSpeed=1;
        [Range(0,1)] public float magazineOutPoint=.16f,magazineInPoint=.66f,slidePoint=.84f;
        [Header("Recoil")]
        public Vector2 hipCameraKick=new Vector2(2.3f,3.2f),aimCameraKick=new Vector2(1.8f,2.6f);
        [Range(1,25)] public float muzzleKick=12;
        [Range(.04f,.4f)] public float recoilReturnTime=.16f;
        [Header("One-handed grip")]
        public Vector3 rightHandAngles=new Vector3(15,0,0);
        [Header("Optional audio (empty by default)")]
        public AudioClip shotSound,emptySound,magazineOutSound,magazineInSound,slideSound;
        [Min(.1f)] public float shotInterval=.17f;
        [Min(1)] public float range=250;
        [Min(1)] public float bulletDamage=34;
        public int Ammo {get;private set;}=MagazineSize;
        public bool IsReloading {get;private set;}
        public float AimAmount {get;private set;}
        public bool IsObstructed=>obstruction>.25f;
        public RaycastHit LastHit {get;private set;}
        public bool HitSomething {get;private set;}
        public int ShotsFired {get;private set;}
        public Vector3 RightGrip=>rightGrip?rightGrip.position:transform.position;
        public Quaternion RightHandRotation=>(rightGrip?rightGrip.rotation:transform.rotation)*Quaternion.Euler(rightHandAngles);
        public Vector3? LeftHandTarget=>IsReloading && ReloadProgress>.07f && ReloadProgress<.82f && magazine
            ?magazine.position-(rightGrip?rightGrip.right:transform.right)*(.015f*PlayerBody.Scale):(Vector3?)null;
        public float ReloadDuration=>(activeReload?activeReload.length:ReloadSeconds)/Mathf.Max(.1f,reloadPlaybackSpeed);
        public float ReloadProgress=>Mathf.Clamp01(reloadTime/(activeReload?Mathf.Max(.1f,activeReload.length):ReloadSeconds));
        // Q/E lean whenever the pistol is out; Alt preserves interaction on a shared key.
        public static bool LeanControlsActive=>Current is PlayerPistol pistol && pistol.CanOperate;
        public static bool InteractionModifierHeld=>Keyboard.current!=null &&
            (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed);
        protected override Vector3 HoldOffset=>new Vector3(.18f,-.16f,.45f);
        public override string Prompt=>Loc.T("[E] Take pistol");
        FirstPersonController player;
        FirstPersonCameraEffects cameraEffects;
        AudioSource sound;
        AnimationClip activeReload;
        Vector3 magazineRest,slideRest,kick,kickVelocity;
        Quaternion magazineRotation;
        Vector2 sway;
        Quaternion lastView;
        float reloadTime,nextShot,obstruction,drawUntil;
        int reloadStage;

        protected override void Awake()
        {
            base.Awake();
            body.mass=.8f;
            if(magazine)magazineRest=magazine.localPosition;
            if(magazine)magazineRotation=magazine.localRotation;
            if(slide)slideRest=slide.localPosition;
            var animation=GetComponent<Animation>();if(animation)animation.enabled=false;
            sound=gameObject.AddComponent<AudioSource>();sound.playOnAwake=false;
            sound.spatialBlend=.8f;sound.minDistance=1;sound.maxDistance=45;sound.dopplerLevel=0;
            sound.rolloffMode=AudioRolloffMode.Logarithmic;
        }
        protected override void OnPickedUp()
        {
            player=hand?hand.GetComponentInParent<FirstPersonController>():null;
            cameraEffects=hand?hand.GetComponentInParent<FirstPersonCameraEffects>():null;
            if(hand)lastView=hand.transform.rotation;
            drawUntil=Time.time+.22f;nextShot=Mathf.Max(nextShot,drawUntil);
            sway=Vector2.zero;kick=Vector3.zero;kickVelocity=Vector3.zero;
        }
        protected override void OnPutAway()
        {
            IsReloading=false;AimAmount=0;reloadTime=0;reloadStage=0;kick=Vector3.zero;kickVelocity=Vector3.zero;
            ResetReloadPose();
            if(cameraEffects)cameraEffects.ResetWeaponMotion();
        }
        bool CanOperate=>IsCarried && hand && hand.isActiveAndEnabled && player && player.isActiveAndEnabled &&
            !GameSettingsMenu.BlockGameplayInput && !PlayerBody.BeltOpen && (Mouse.current==null || !Mouse.current.middleButton.isPressed) && Cursor.lockState==CursorLockMode.Locked && Time.timeScale>0;

        protected override void Carry()
        {
            if(!CanOperate)return;
            var mouse=Mouse.current;
            bool aiming=mouse!=null && mouse.rightButton.isPressed && !IsReloading;
            AimAmount=Mathf.MoveTowards(AimAmount,aiming?1:0,Time.deltaTime/ .18f);
            var delta=Quaternion.Inverse(lastView)*hand.transform.rotation;
            lastView=hand.transform.rotation;
            Vector2 desired=new Vector2(Mathf.DeltaAngle(0,delta.eulerAngles.y),Mathf.DeltaAngle(0,delta.eulerAngles.x));
            sway=Vector2.Lerp(sway,Vector2.ClampMagnitude(desired*2.5f,5),1-Mathf.Exp(-Time.deltaTime*12));
            kick=Vector3.SmoothDamp(kick,Vector3.zero,ref kickVelocity,recoilReturnTime,Mathf.Infinity,Time.deltaTime);
            float closest=range*PlayerBody.Scale;
            if(Raycast(hand.transform.position,hand.transform.forward,1.1f*PlayerBody.Scale,out var wall))closest=wall.distance;
            obstruction=Mathf.InverseLerp(.85f*PlayerBody.Scale,.35f*PlayerBody.Scale,closest);
            if(GameControls.Pressed(GameCommand.Reload))BeginReload();
            if(IsReloading)UpdateReload(Time.deltaTime);
            PoseWeapon();
            if(mouse!=null && mouse.leftButton.wasPressedThisFrame)TryFire();
        }
        void PoseWeapon()
        {
            float a=Mathf.SmoothStep(0,1,AimAmount);
            var sight=rearSight?rearSight.localPosition:new Vector3(0,.075f,0);
            var offset=Vector3.Lerp(HoldOffset,new Vector3(-sight.x,-sight.y,.45f),a);
            float movement=player?new Vector2(player.GetComponent<CharacterController>().velocity.x,player.GetComponent<CharacterController>().velocity.z).magnitude/PlayerBody.Scale:0;
            float motion=Mathf.Clamp01(movement/5)*(1-a*.85f);
            offset+=new Vector3(Mathf.Sin(Time.time*8)*.009f,Mathf.Sin(Time.time*16)*.006f,0)*motion;
            offset+=new Vector3(Mathf.Sin(Time.time*1.4f)*.001f,Mathf.Sin(Time.time*1.9f)*.001f,-kick.x*.004f);
            offset+=new Vector3(0,-.09f,-.13f)*obstruction;
            var angles=new Vector3(-kick.x+sway.y*(1-a*.85f)+obstruction*30,-sway.x*(1-a*.85f)+kick.y,kick.z);
            transform.SetPositionAndRotation(hand.transform.position+hand.transform.rotation*(offset*PlayerBody.Scale),hand.transform.rotation*Quaternion.Euler(angles));
            if(slide && !IsReloading)
                slide.localPosition=slideRest+Vector3.back*(Ammo==0?.024f:Mathf.Clamp01(kick.x/5)*.026f);
        }
        public bool BeginReload()
        {
            if(!CanOperate || IsReloading || Ammo==MagazineSize || Time.time<drawUntil)return false;
            activeReload=Ammo==0 && emptyReloadClip?emptyReloadClip:reloadClip;
            if(!activeReload)return false;
            IsReloading=true;reloadTime=0;reloadStage=0;
            activeReload.SampleAnimation(gameObject,0);
            return true;
        }
        void UpdateReload(float dt)
        {
            reloadTime+=dt*Mathf.Max(.1f,reloadPlaybackSpeed);
            float progress=ReloadProgress;
            while(reloadStage<3 && progress>=(reloadStage==0?magazineOutPoint:reloadStage==1?magazineInPoint:slidePoint))
            {reloadStage++;Play(reloadStage==1?magazineOutSound:reloadStage==2?magazineInSound:slideSound,.6f);}
            activeReload.SampleAnimation(gameObject,Mathf.Min(reloadTime,activeReload.length));
            if(reloadTime<activeReload.length)return;
            Ammo=MagazineSize;IsReloading=false;
            ResetReloadPose();
            nextShot=Time.time+.12f;
        }
        void ResetReloadPose()
        {
            if(reloadMotion)reloadMotion.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
            if(magazine)magazine.SetLocalPositionAndRotation(magazineRest,magazineRotation);
            if(slide)slide.localPosition=slideRest;
        }
        public bool TryFire()
        {
            if(!CanOperate || IsReloading || Time.time<nextShot || IsObstructed)return false;
            nextShot=Time.time+shotInterval;
            if(Ammo==0){Play(emptySound,.5f);return false;}
            var origin=muzzle?muzzle.position:transform.position;
            // A muzzle embedded in a nearby wall cannot hit anything on the far side.
            var toMuzzle=origin-hand.transform.position;
            if(Raycast(hand.transform.position,toMuzzle.normalized,toMuzzle.magnitude,out _))return false;
            var direction=hand.transform.forward;
            float speed=player.GetComponent<CharacterController>().velocity.magnitude/PlayerBody.Scale;
            float spread=Mathf.Lerp(.85f,.035f,AimAmount)+Mathf.Clamp01(speed/6)*Mathf.Lerp(1.1f,.2f,AimAmount);
            var disk=Random.insideUnitCircle*Mathf.Tan(spread*Mathf.Deg2Rad);
            direction=(direction+hand.transform.right*disk.x+hand.transform.up*disk.y).normalized;
            var target=hand.transform.position+direction*range*PlayerBody.Scale;
            if(Raycast(hand.transform.position,direction,range*PlayerBody.Scale,out var eyeHit))target=eyeHit.point;
            var path=target-origin;
            HitSomething=Raycast(origin,path.normalized,path.magnitude+.02f*PlayerBody.Scale,out var hit);
            LastHit=hit;
            Ammo--;ShotsFired++;
            if(HitSomething)
            {
                var zone=hit.collider.GetComponent<ZombieHitZone>();
                bool zombieHit=zone && zone.Hit(bulletDamage,hit.point,hit.normal,path.normalized);
                var lamp=hit.collider.GetComponentInParent<BreakableLamp>();
                bool glassHit=lamp && lamp.TryBreak(hit,new Ray(origin,path.normalized),PlayerBody.Scale,effectMaterial);
                var prop=hit.collider.GetComponentInParent<HeldProp>();
                bool shattered=prop && prop.ReactToPistolHit(path.normalized,effectMaterial);
                if(!shattered && !zombieHit && !glassHit)
                {
                    if(hit.rigidbody && !hit.rigidbody.isKinematic)hit.rigidbody.AddForceAtPosition(path.normalized*(5*PlayerBody.Scale),hit.point,ForceMode.Impulse);
                    PistolEffects.Impact(hit,PlayerBody.Scale,effectMaterial,impactMaterial);
                }
            }
            PistolEffects.Shot(origin,transform.rotation,PlayerBody.Scale,effectMaterial,casingMaterial);
            Play(shotSound,.9f);
            kick+=new Vector3(muzzleKick*Mathf.Lerp(1,.85f,AimAmount),Random.Range(-1.6f,1.6f),Random.Range(-3,3));
            kick=Vector3.Min(kick,new Vector3(24,8,8));kickVelocity=Vector3.zero;
            var cameraKick=Vector2.Lerp(hipCameraKick,aimCameraKick,AimAmount);
            player.AddViewKick(Random.Range(cameraKick.x,cameraKick.y),Random.Range(-.55f,.55f));
            if(cameraEffects)cameraEffects.AddWeaponImpulse(Random.Range(-.9f,.9f),.014f*PlayerBody.Scale);
            return true;
        }
        bool Raycast(Vector3 origin,Vector3 direction,float distance,out RaycastHit nearest)
        {
            nearest=default;float best=distance;
            bool found=false;
            foreach(var hit in Physics.RaycastAll(origin,direction,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            {
                if(hit.collider is CharacterController && hit.collider.GetComponent<ZombieActor>())continue;
                if(hit.distance>best || hit.transform.IsChildOf(transform) || (player && hit.transform.IsChildOf(player.transform)))continue;
                nearest=hit;best=hit.distance;found=true;
            }
            return found;
        }
        void Play(AudioClip clip,float volume)
        {if(sound && clip){sound.pitch=1;sound.PlayOneShot(clip,volume);}}
        protected override void OnGUI()
        {
            if(!CanOperate)return;
            string state=IsReloading?Loc.T("RELOADING"):Ammo==0?Loc.T("EMPTY"):IsObstructed?Loc.T("MUZZLE BLOCKED"):Loc.T("PISTOL");
            InteractionHud.Prompt(Screen.width-160,Screen.height-145,state+"  "+Ammo+" / 8",18);
            InteractionHud.Footer(Loc.F("[LMB] Fire   [RMB HOLD] Aim   [{0}] Reload",GameControls.Label(GameCommand.Reload)));
            InteractionHud.Prompt(Screen.width/2f,Screen.height-120,Loc.F("[{0}/{1}] Lean   [{2}] Interact",GameControls.Label(GameCommand.LeanLeft),GameControls.Label(GameCommand.LeanRight),GameControls.Label(GameCommand.Interact)),14);
        }
    }
}
