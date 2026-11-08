using MoonThemedGame.Audio;
using MoonThemedGame.Interactions;
using MoonThemedGame.Repair;
using MoonThemedGame.Scavenger;
using UnityEngine;

namespace MoonThemedGame.Weather
{
    [DisallowMultipleComponent]
    public sealed class HordeZombie : MonoBehaviour
    {
        public ZombieActor Actor {get;private set;}
        public bool Retreating {get;private set;}
        MoonWeatherSystem weather;
        ScavengerLink link;
        CharacterController motor;
        Collider hull;
        Collider[] suitColliders,roverColliders;
        Vector3 previousHullCenter,retreatStart;
        float nextHit,retreatTime,corpseAge,nextBump;
        bool corpseCollisionRestored;
        bool emerging=true;
        float riseTime;
        int poseVersion;
        Vector3 surface;
        public void Initialize(HordeWeatherEvent owner,MoonWeatherSystem system,ScavengerLink scavenger,ZombieActor actor)
        {
            weather=system;link=scavenger;Actor=actor;motor=actor.GetComponent<CharacterController>();
            hull=link.Drive?link.Drive.GetComponent<BoxCollider>():link.state.GetComponent<Collider>();
            if(!hull)hull=link.state.GetComponentInChildren<Collider>();
            previousHullCenter=hull?hull.bounds.center:link.robot.position;
            poseVersion=link.Drive?link.Drive.PoseVersion:0;
            suitColliders=actor.GetComponentsInChildren<Collider>(true);
            roverColliders=link.robot.GetComponentsInChildren<Collider>(true);
            // The standing suit's bone shapes are for pistol hit zones. Its capsule
            // becomes the solid obstacle as soon as emergence finishes.
            foreach(var own in suitColliders)
                foreach(var robot in roverColliders)Physics.IgnoreCollision(own,robot);
            surface=transform.position;actor.enabled=false;motor.enabled=false;
            transform.position=surface-Vector3.up*(1.8f*Unit);
        }
        float Unit=>Mathf.Max(.01f,Mathf.Abs(transform.lossyScale.y));
        void Update()
        {
            if(Retreating)
            {
                retreatTime+=Time.deltaTime;
                transform.position=retreatStart-Vector3.up*(2.5f*Unit*Mathf.SmoothStep(0,1,retreatTime/2));
                if(retreatTime>=2)Destroy(gameObject);
                return;
            }
            if(!weather||weather.Current!=WeatherKind.Horde||!weather.isActiveAndEnabled){Retreat();return;}
            if(Actor.IsDead){emerging=false;RestoreCorpseCollisions();return;}
            if(emerging)
            {
                riseTime+=Time.deltaTime;transform.position=surface-Vector3.up*(1.8f*Unit*(1-Mathf.SmoothStep(0,1,riseTime/1.2f)));
                if(riseTime>=1.2f)
                {
                    emerging=false;motor.enabled=true;Actor.enabled=true;
                    foreach(var robot in roverColliders)if(robot)Physics.IgnoreCollision(motor,robot,false);
                }
            }
            // Entering shelter stops pursuit/attacks instead of drawing zombies through the base.
            if(!emerging&&!Actor.IsDead)Actor.enabled=link&&link.state&&link.state.HasSignal&&!weather.IsBaseFootprint(link.robot.position,2);
        }
        void FixedUpdate()
        {
            if(!hull||!link||!link.Drive||!Actor)return;
            var center=hull.bounds.center;var before=previousHullCenter;previousHullCenter=center;
            if(poseVersion!=link.Drive.PoseVersion){poseVersion=link.Drive.PoseVersion;return;}
            if(emerging||Retreating||Actor.IsDead||weather.Current!=WeatherKind.Horde||
                GameSettingsMenu.BlockGameplayInput||DeveloperConsole.BlockGameplayInput)return;
            var body=link.Drive.Body;var velocity=body?body.linearVelocity:Vector3.zero;
            float speed=Vector3.ProjectOnPlane(velocity,Vector3.up).magnitude;
            // Skin contact must count even when the solver correctly prevents overlap.
            var capsuleCenter=transform.TransformPoint(motor.center);
            float radius=motor.radius*Mathf.Max(Mathf.Abs(transform.lossyScale.x),Mathf.Abs(transform.lossyScale.z));
            float halfSegment=Mathf.Max(0,motor.height*Unit*.5f-radius);
            var nearest=hull.ClosestPoint(capsuleCenter);
            var segment=capsuleCenter+Vector3.up*Mathf.Clamp(nearest.y-capsuleCenter.y,-halfSegment,halfSegment);
            float contactRadius=radius+motor.skinWidth*Unit+.04f*Unit;
            bool touching=(hull.ClosestPoint(segment)-segment).sqrMagnitude<=contactRadius*contactRadius;
            var swept=new Bounds(motor.bounds.center,motor.bounds.size+2*hull.bounds.extents);
            // Predict the next physics step: disable the capsule on a lethal hit
            // before it can behave like a concrete wall in front of the rover.
            var travel=center+velocity*Time.fixedDeltaTime-before;
            bool crossed=travel.sqrMagnitude>.0001f&&swept.IntersectRay(new Ray(before,travel.normalized),out float distance)&&distance<=travel.magnitude;
            if(!touching&&!crossed)return;
            if(Time.time>=nextBump&&(speed>.15f||motor.velocity.magnitude>.5f)){nextBump=Time.time+.6f;GameAudio.At(Sound.ZombieBang,hull.ClosestPoint(capsuleCenter),Mathf.Lerp(.3f,1,Mathf.Clamp01(speed/10)));}
            if(speed>=weather.hordeRamSpeed)
            {
                if(Actor.RunOver(hull.ClosestPoint(motor.bounds.center),velocity.normalized,velocity)&&body&&!body.isKinematic)
                {
                    // A heavy rover loses some momentum, without an artificial upward kick.
                    float transfer=Mathf.Clamp(65/Mathf.Max(1,body.mass+65),.06f,.22f);
                    body.AddForce(-Vector3.ProjectOnPlane(velocity,Vector3.up)*transfer,ForceMode.VelocityChange);
                }
            }
            else
            {
                if(touching&&Actor.enabled&&Time.time>=nextHit)
                {
                    nextHit=Time.time+Actor.attackInterval;
                    link.state.ApplyEnergyDamage(weather.hordeContactDamage);
                    link.GetComponent<ScavengerFeedHud>()?.Notify(Loc.T("HORDE CONTACT [ENERGY LOST]"));
                }
                // Yield even at crawling speed, including when the pursuit AI is paused.
                var outward=Vector3.ProjectOnPlane(capsuleCenter-nearest,Vector3.up);
                if(outward.sqrMagnitude<.001f)outward=Vector3.ProjectOnPlane(capsuleCenter-center,Vector3.up);
                if(outward.sqrMagnitude<.001f)outward=link.Drive.Heading;
                outward.Normalize();
                float shove=Mathf.Max(Unit*.5f,speed*1.3f)*Time.fixedDeltaTime;
                if(Actor.PushByVehicle(outward,shove)<shove*.35f)
                {
                    // A wall behind the zombie should let it slide sideways out of the lane.
                    var sideways=Vector3.Cross(Vector3.up,outward);
                    float side=Vector3.Dot(capsuleCenter-center,sideways)>=0?1:-1;
                    if(Actor.PushByVehicle(sideways*side,shove)<shove*.35f)
                        Actor.PushByVehicle(-sideways*side,shove);
                }
            }
        }
        void RestoreCorpseCollisions()
        {
            if(corpseCollisionRestored||!hull)return;
            corpseAge+=Time.deltaTime;if(corpseAge<.15f)return;
            // Clear the initial impact overlap before enabling the moving bones.
            // Afterwards the corpse collides with the rover and can be pushed again.
            foreach(var own in suitColliders)
                if(own&&own!=motor&&own.enabled&&Physics.ComputePenetration(own,own.transform.position,own.transform.rotation,
                    hull,hull.transform.position,hull.transform.rotation,out _,out _))return;
            foreach(var own in suitColliders)
                if(own&&own!=motor)foreach(var robot in roverColliders)if(robot)Physics.IgnoreCollision(own,robot,false);
            corpseCollisionRestored=true;
        }
        public void Retreat()
        {
            if(Retreating||!Actor)return;
            Retreating=true;retreatStart=transform.position;retreatTime=0;
            Actor.enabled=false;if(motor)motor.enabled=false;
            foreach(var collider in GetComponentsInChildren<Collider>())collider.enabled=false;
            foreach(var body in GetComponentsInChildren<Rigidbody>()){body.isKinematic=true;body.useGravity=false;}
        }
    }
}
