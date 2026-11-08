using MoonThemedGame.Weather;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    [DefaultExecutionOrder(100),DisallowMultipleComponent]
    public sealed class CraneGrabFx : MonoBehaviour
    {
        ParticleSystem dust,sparks;
        Transform grip;
        Vector3 previousGrip;
        float scale=1,started,liftStarted=-1,nextPuff,wind;
        bool watching;
        System.Random random;
        public int GrabBursts {get;private set;}
        public int LiftPuffs {get;private set;}
        float Range(float a,float b){if(random==null)random=new System.Random(GetInstanceID());return a+(b-a)*(float)random.NextDouble();}
        Vector3 Scatter()=>new Vector3(Range(-1,1),Range(-1,1),Range(-1,1));
        void EnsureParticles()
        {
            var material=RepairFeedbackFx.DustMaterial;if(!material)return;
            if(!dust)
            {
                dust=WeatherImpactFx.MakeParticles(transform,"Crane / grasp dust and updraft",material,2,.2f,128,false);
                dust.gameObject.layer=gameObject.layer;
                var velocity=dust.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;velocity.y=0;
            }
            if(!sparks)
            {
                sparks=WeatherImpactFx.MakeParticles(transform,"Crane / contact sparks",material,.25f,.02f,16,false);sparks.gameObject.layer=gameObject.layer;
                var main=sparks.main;main.gravityModifier=.18f;var size=sparks.sizeOverLifetime;size.enabled=false;
                var renderer=sparks.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;renderer.mesh=RepairFeedbackFx.FlakeMesh;
            }
        }
        public void Grasp(Transform claw,RepairRobotTarget robot)
        {
            if(!claw||!robot)return;
            EnsureParticles();if(!dust||!sparks)return;
            var bounds=new Bounds(robot.transform.position,Vector3.zero);bool found=false;
            foreach(var renderer in robot.GetComponentsInChildren<MeshRenderer>())
                if(renderer.enabled&&!renderer.name.StartsWith("Interaction /"))
                {if(!found){bounds=renderer.bounds;found=true;}else bounds.Encapsulate(renderer.bounds);}
            scale=Mathf.Max(.3f,bounds.size.magnitude*.5f);
            grip=claw;previousGrip=claw.position;started=Time.time;liftStarted=-1;nextPuff=0;watching=true;GrabBursts++;
            var contact=Vector3.Lerp(claw.position,new Vector3(bounds.center.x,bounds.max.y,bounds.center.z),.5f);
            Cloud(contact,30,Vector3.up*(scale*.22f));
            if(!sparks.isPlaying)sparks.Play();
            for(int i=0;i<3;i++)sparks.Emit(new ParticleSystem.EmitParams{position=contact+Scatter()*(scale*.015f),
                velocity=(Vector3.up*.6f+Scatter())*Range(.3f,.7f)*scale,startSize=Range(.007f,.015f)*scale,
                startLifetime=Range(.14f,.3f),startColor=new Color(2,1.6f,.95f,1)},1);
        }
        void Cloud(Vector3 point,int count,Vector3 velocity)
        {
            if(!dust)return;if(!dust.isPlaying)dust.Play();
            for(int i=0;i<count;i++)dust.Emit(new ParticleSystem.EmitParams{position=point+Scatter()*(scale*.075f),
                velocity=velocity+Scatter()*(scale*.18f),startSize=Range(.08f,.17f)*scale,
                startLifetime=Range(1.2f,2.4f),startColor=new Color(.46f,.43f,.37f,.5f)},1);
        }
        public void StopLift(){watching=false;grip=null;}
        void LateUpdate()
        {
            float targetWind=0;
            if(watching&&grip&&Time.deltaTime>0)
            {
                float up=Mathf.Clamp((grip.position.y-previousGrip.y)/Time.deltaTime,0,scale*2);previousGrip=grip.position;
                if(up>scale*.025f)
                {
                    if(liftStarted<0)liftStarted=Time.time;
                    targetWind=Mathf.Min(scale*.8f,up*.6f+scale*.12f);
                    if(Time.time-liftStarted<1.8f&&Time.time>=nextPuff)
                    {nextPuff=Time.time+.08f;Cloud(grip.position-Vector3.up*(scale*.07f),2,Vector3.up*(up*.3f+scale*.12f));LiftPuffs++;}
                }
                if(Time.time-started>6||liftStarted>=0&&Time.time-liftStarted>2.4f)StopLift();
            }
            wind=Mathf.MoveTowards(wind,targetWind,Time.deltaTime*scale*2);
            if(dust){var velocity=dust.velocityOverLifetime;velocity.y=wind;}
        }
        void OnDisable()
        {
            StopLift();wind=0;
            if(dust)dust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(sparks)sparks.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
