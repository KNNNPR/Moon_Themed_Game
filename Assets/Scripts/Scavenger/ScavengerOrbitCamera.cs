using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MoonThemedGame.Scavenger
{
    // Independent orbit with a horizon-stable chase view and a soft return behind a moving car.
    [DisallowMultipleComponent]
    public sealed class ScavengerOrbitCamera : MonoBehaviour
    {
        [Min(0.01f)] public float mouseSensitivity = 0.14f;
        [Min(1)] public float gamepadTurnSpeed = 140;
        [Range(5, 65)] public float defaultPitch = 22;
        public Vector2 pitchLimits = new Vector2(8, 65);
        [Min(0)] public float recenterDelay = 0.9f;
        [Min(0.01f)] public float followSmoothing = 0.13f;
        [Min(0.01f)] public float lookSmoothing = 0.055f;
        [Min(0.5f)] public float distanceInRadii = 3.3f;
        [Range(0,1)] public float travelInfluence = 0.4f;
        [Range(0,0.2f)] public float lookAheadTime = 0.12f;
        [Header("Sense of speed")]
        [Range(0,20)] public float speedFieldOfView = 10;
        [Range(0,0.3f)] public float speedPullback = 0.12f;
        [Range(0,8)] public float speedPitchDrop = 3;
        [Range(0,3)] public float maximumCornerBank = 1.25f;
        [Range(0,.15f)] public float cornerFollowLag=.075f;
        [Range(0,3)] public float accelerationFieldOfView=1.5f;
        [Range(0,.1f)] public float accelerationPullback=.04f;
        [Header("Landing (quickly settles)")]
        [Range(0,.3f)] public float landingDip=.18f;
        [Range(0,4)] public float landingNod=2.5f;

        readonly RaycastHit[] hits = new RaycastHit[24];
        Transform robot;
        ScavengerDrive drive;
        Vector3 localCenter, pivot, pivotVelocity;
        float radius, desiredYaw, desiredPitch, yaw, pitch, yawVelocity, pitchVelocity;
        float distance, distanceVelocity, idle;
        Camera viewCamera;
        float baseFieldOfView, speedBlend, cornerBank, accelerationBlend, previousSpeed;
        float landingTime=10,landingStrength;
        int poseVersion;
        bool wasLookAllowed, manualOrbit;
        public float Yaw => yaw;
        public float Pitch => pitch;
        public float Distance => distance;

        public void Initialize(Transform target, ScavengerDrive vehicle, Vector3 center, float size)
        {
            if(drive)drive.Landed-=OnLanded;
            robot=target; drive=vehicle; localCenter=center; radius=Mathf.Max(0.1f,size);
            if(isActiveAndEnabled&&drive)drive.Landed+=OnLanded;
            if(!viewCamera) { viewCamera=GetComponent<Camera>();if(viewCamera)baseFieldOfView=viewCamera.fieldOfView; }
            ResetView(true);
        }
        void OnEnable(){if(drive){drive.Landed-=OnLanded;drive.Landed+=OnLanded;}}
        void OnDisable(){if(drive)drive.Landed-=OnLanded;ClearMotion();if(viewCamera)viewCamera.fieldOfView=baseFieldOfView;}
        void OnLanded(float strength)
        {
            // A disabled feed must not replay an old impact when the operator reconnects.
            if(!viewCamera||!viewCamera.enabled||!MoonThemedGame.Interactions.ComfortSettings.CameraShake)return;
            landingStrength=Mathf.Clamp01(strength);landingTime=0;
        }
        void ClearMotion(){landingTime=10;landingStrength=accelerationBlend=0;previousSpeed=PlanarSpeed;}

        float Heading
        {
            get
            {
                if(!drive)return robot.eulerAngles.y;
                float heading=Quaternion.LookRotation(drive.Heading).eulerAngles.y;
                if(!drive.Body || drive.IsOverturned || (drive.ForwardSpeed<drive.Length*0.2f && !drive.IsForwardDrift))return heading;
                var velocity=Vector3.ProjectOnPlane(drive.Body.linearVelocity,Vector3.up);
                float travel=Mathf.Atan2(velocity.x,velocity.z)*Mathf.Rad2Deg;
                return Mathf.LerpAngle(heading,travel,travelInfluence);
            }
        }
        Vector3 TargetPivot
        {
            get
            {
                var center=robot.TransformPoint(localCenter)+Vector3.up*radius*0.25f;
                if(drive && drive.Body && !drive.IsOverturned)
                    center+=Vector3.ClampMagnitude(Vector3.ProjectOnPlane(drive.Body.linearVelocity,Vector3.up)*lookAheadTime,radius*0.55f);
                return center;
            }
        }

        public void ResetView(bool snap)
        {
            if(!robot)return;
            desiredYaw=Heading; desiredPitch=Mathf.Clamp(defaultPitch,pitchLimits.x,pitchLimits.y);
            idle=0; wasLookAllowed=false; manualOrbit=false;
            if(!snap)return;
            ClearMotion();poseVersion=drive?drive.PoseVersion:0;
            speedBlend=SpeedRatio;cornerBank=0;UpdateLens();
            yaw=desiredYaw; pitch=desiredPitch; yawVelocity=pitchVelocity=0;
            pivot=TargetPivot; pivotVelocity=Vector3.zero;
            distance=radius*distanceInRadii; distanceVelocity=0;
            Place(0,true);
        }

        public void Tick(bool allowLook)
        {
            float dt=Time.deltaTime;
            var look=Vector2.zero;
            bool reset=false;
#if ENABLE_INPUT_SYSTEM
            if(Mouse.current!=null) { look+=Mouse.current.delta.ReadValue()*mouseSensitivity; reset=Mouse.current.middleButton.wasPressedThisFrame; }
            if(Gamepad.current!=null) { look+=Gamepad.current.rightStick.ReadValue()*gamepadTurnSpeed*dt; reset|=Gamepad.current.rightStickButton.wasPressedThisFrame; }
            reset|=MoonThemedGame.Interactions.GameControls.Pressed(MoonThemedGame.Interactions.GameCommand.RobotCameraReset);
#elif ENABLE_LEGACY_INPUT_MANAGER
            look=new Vector2(Input.GetAxisRaw("Mouse X"),Input.GetAxisRaw("Mouse Y"))*mouseSensitivity*12;
            reset=Input.GetKeyDown(KeyCode.C)||Input.GetMouseButtonDown(2);
#endif
            Advance(dt,look*MoonThemedGame.Interactions.ComfortSettings.RobotSensitivity,reset,allowLook);
        }

        // Degree deltas can also be supplied by touch controls or a replay.
        public void Advance(float dt, Vector2 lookDelta, bool reset, bool allowLook)
        {
            if(!robot)return;
            if(drive&&poseVersion!=drive.PoseVersion){ResetView(true);return;}
            if(!allowLook || !wasLookAllowed) { lookDelta=Vector2.zero; reset=false; }
            wasLookAllowed=allowLook;
            if(dt<=0)return;
            landingTime+=dt;
            if(!MoonThemedGame.Interactions.ComfortSettings.CameraShake)landingStrength=0;
            float speed=PlanarSpeed;
            float acceleration=drive&&!drive.IsOverturned?Mathf.Clamp01((speed-previousSpeed)/
                Mathf.Max(.001f,dt*drive.Length*drive.acceleration*drive.DebugSpeedMultiplier)):0;
            previousSpeed=speed;
            accelerationBlend=Mathf.Lerp(accelerationBlend,acceleration,1-Mathf.Exp(-10*dt));
            speedBlend=Mathf.Lerp(speedBlend,SpeedRatio,1-Mathf.Exp(-5*dt));
            UpdateLens();
            bool manual=lookDelta.sqrMagnitude>0.000001f;
            if(manual)
            {
                manualOrbit=true;
                desiredYaw=Mathf.Repeat(desiredYaw+lookDelta.x,360);
                desiredPitch=Mathf.Clamp(desiredPitch-lookDelta.y,pitchLimits.x,pitchLimits.y);
                idle=0;
            }
            else idle+=dt;
            if(reset) { desiredYaw=Heading; desiredPitch=Mathf.Clamp(defaultPitch,pitchLimits.x,pitchLimits.y); idle=0; manualOrbit=false; }
            else if(!manual && idle>=recenterDelay && drive && Mathf.Abs(drive.ForwardSpeed)>drive.Length*0.1f)
                manualOrbit=false;
            if(!manualOrbit)
            {
                desiredYaw=Heading;
                desiredPitch=Mathf.Clamp(defaultPitch-speedPitchDrop*speedBlend,pitchLimits.x,pitchLimits.y);
            }

            float bankTarget=drive && drive.Body && !drive.IsOverturned && !drive.Airborne && !manualOrbit ?
                -Mathf.Clamp(Vector3.Dot(drive.Body.angularVelocity,Vector3.up)/2,-1,1)*maximumCornerBank*speedBlend : 0;
            cornerBank=MoonThemedGame.Interactions.ComfortSettings.CameraShake?Mathf.Lerp(cornerBank,bankTarget,1-Mathf.Exp(-6*dt)):0;

            float turn=drive&&drive.Body?Mathf.Clamp01(Mathf.Abs(drive.Body.angularVelocity.y)/2):0;
            yaw=Mathf.SmoothDampAngle(yaw,desiredYaw,ref yawVelocity,manual?lookSmoothing:
                followSmoothing+cornerFollowLag*speedBlend*turn,Mathf.Infinity,dt);
            pitch=Mathf.SmoothDampAngle(pitch,desiredPitch,ref pitchVelocity,lookSmoothing,Mathf.Infinity,dt);
            pivot=Vector3.SmoothDamp(pivot,TargetPivot,ref pivotVelocity,landingTime<.6f?.055f:.08f,Mathf.Infinity,dt);
            Place(dt,false);
        }

        void Place(float dt, bool snap)
        {
            float pulse=MoonThemedGame.Interactions.ComfortSettings.CameraShake&&landingTime<.65f?landingStrength*Mathf.Exp(-9*landingTime)*Mathf.Sin(23*landingTime):0;
            var origin=pivot-Vector3.up*(radius*landingDip*pulse);
            var away=Quaternion.Euler(pitch+landingNod*pulse,yaw,0)*Vector3.back;
            float wanted=radius*distanceInRadii*(1+speedPullback*speedBlend+accelerationPullback*accelerationBlend);
            float castRadius=Mathf.Clamp(radius*0.12f,0.06f,0.25f);
            int count=Physics.SphereCastNonAlloc(origin,castRadius,away,hits,wanted,~(1<<31),QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)
            {
                var hit=hits[i];
                if(hit.distance<=0 || hit.transform.IsChildOf(robot) || hit.transform.IsChildOf(transform))continue;
                wanted=Mathf.Min(wanted,Mathf.Max(radius*0.18f,hit.distance-radius*0.06f));
            }
            // Pull in immediately at walls; restore the normal distance smoothly afterwards.
            if(snap || wanted<distance) { distance=wanted; distanceVelocity=0; }
            else distance=Mathf.SmoothDamp(distance,wanted,ref distanceVelocity,0.2f,Mathf.Infinity,dt);
            transform.SetPositionAndRotation(origin+away*distance,Quaternion.LookRotation(-away,Vector3.up)*Quaternion.AngleAxis(cornerBank,Vector3.forward));
        }

        float PlanarSpeed=>drive&&drive.Body?Vector3.ProjectOnPlane(drive.Body.linearVelocity,Vector3.up).magnitude:0;
        float SpeedRatio => drive && drive.Body && !drive.IsOverturned ?
            Mathf.Clamp01(Vector3.ProjectOnPlane(drive.Body.linearVelocity,Vector3.up).magnitude/Mathf.Max(0.01f,drive.MaximumForwardSpeed)) : 0;
        void UpdateLens()
        { if(viewCamera)viewCamera.fieldOfView=baseFieldOfView+MoonThemedGame.Interactions.ComfortSettings.DynamicFov*(speedFieldOfView*Mathf.SmoothStep(0,1,speedBlend)+accelerationFieldOfView*accelerationBlend); }
    }
}
