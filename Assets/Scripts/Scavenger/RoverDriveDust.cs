using System.Collections.Generic;
using MoonThemedGame.Audio;
using MoonThemedGame.Repair;
using MoonThemedGame.Weather;
using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    // Reuses real suspension hits; it never raycasts a substitute driving surface.
    [DisallowMultipleComponent,RequireComponent(typeof(ScavengerDrive))]
    public sealed class RoverDriveDust : MonoBehaviour
    {
        readonly List<RaycastHit> contacts=new List<RaycastHit>(8);
        ScavengerDrive drive;
        ParticleSystem dust,grit;
        float next;
        int pose=-1;
        System.Random random;
        public int DustEmitted {get;private set;}
        public int GritEmitted {get;private set;}
        float Range(float a,float b){if(random==null)random=new System.Random(GetInstanceID());return a+(b-a)*(float)random.NextDouble();}
        Vector3 Scatter()=>new Vector3(Range(-1,1),Range(-1,1),Range(-1,1));
        void Awake()=>drive=GetComponent<ScavengerDrive>();
        void EnsureParticles()
        {
            var material=RepairFeedbackFx.DustMaterial;if(!material)return;
            if(!dust)
            {
                dust=WeatherImpactFx.MakeParticles(transform,"Rover / ground dust trail",material,1.2f,.1f,192,false);dust.gameObject.layer=gameObject.layer;
            }
            if(!grit)
            {
                grit=WeatherImpactFx.MakeParticles(transform,"Rover / thrown regolith",material,.6f,.02f,96,false);grit.gameObject.layer=gameObject.layer;
                var main=grit.main;main.gravityModifier=Mathf.Max(.1f,drive.Length*.14f);main.startRotation3D=true;
                main.startRotationX=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);main.startRotationY=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
                var renderer=grit.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;renderer.mesh=RepairFeedbackFx.FlakeMesh;
                var size=grit.sizeOverLifetime;size.enabled=false;
            }
        }
        void LateUpdate()
        {
            if(!drive||!drive.isActiveAndEnabled||!drive.Body||drive.Body.isKinematic||drive.Airborne||drive.IsOverturned||Time.deltaTime<=0)return;
            if(pose!=drive.PoseVersion){pose=drive.PoseVersion;Clear();return;}
            var velocity=Vector3.ProjectOnPlane(drive.Body.linearVelocity,Vector3.up);float speed=velocity.magnitude;
            if(speed<drive.Length*.06f||Time.time<next)return;
            float pace=Mathf.InverseLerp(drive.Length*.06f,Mathf.Max(drive.Length*.2f,drive.MaximumForwardSpeed*.85f),speed);
            float slip=Mathf.Clamp01(Mathf.Abs(drive.SlipAngle)/55);
            float strength=Mathf.Clamp01(pace+slip*.45f+drive.DriftAmount*.3f);
            next=Time.time+Mathf.Lerp(.12f,.045f,strength);
            drive.CopyGroundContacts(contacts);
            foreach(var hit in contacts)
            {
                if(!RoverSurfaceAudio.LooseGround(hit))continue;
                EnsureParticles();if(!dust||!grit)return;
                if(!dust.isPlaying)dust.Play();
                int count=1+Mathf.RoundToInt(strength*2);
                for(int i=0;i<count;i++)
                {
                    dust.Emit(new ParticleSystem.EmitParams{position=hit.point+hit.normal*(drive.Length*.006f),
                        velocity=-velocity*.11f+hit.normal*(Range(.05f,.18f)*drive.Length)+Scatter()*(drive.Length*.08f),
                        startSize=drive.Length*Range(.035f,.07f)*Mathf.Lerp(.75f,1.35f,strength),startLifetime=Range(.65f,1.5f),
                        startColor=new Color(.43f,.41f,.36f,Mathf.Lerp(.25f,.48f,strength))},1);DustEmitted++;
                }
                if(strength<.35f)continue;
                if(!grit.isPlaying)grit.Play();
                grit.Emit(new ParticleSystem.EmitParams{position=hit.point+hit.normal*(drive.Length*.008f),
                    velocity=-velocity*Range(.12f,.22f)+hit.normal*(Range(.12f,.3f)*drive.Length)+Scatter()*(drive.Length*.12f),
                    startSize=Range(.003f,.009f)*drive.Length,startLifetime=Range(.25f,.7f),startColor=new Color(.31f,.28f,.22f,.9f)},1);GritEmitted++;
            }
        }
        void Clear()
        {
            if(dust)dust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(grit)grit.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        void OnDisable()=>Clear();
    }
}
