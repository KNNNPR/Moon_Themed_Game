using MoonThemedGame.Audio;
using MoonThemedGame.Weather;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    public sealed class RepairRust : MonoBehaviour
    {
        ParticleSystem particles;
        float next;
        float travel;
        bool first=true;
        System.Random random;
        float Sample(float min,float max){if(random==null)random=new System.Random(GetInstanceID());return min+(max-min)*(float)random.NextDouble();}
        public static void Turn(RepairBolt bolt,float accepted)
        {
            if(!Application.isPlaying||Mathf.Abs(accepted)<.001f)return;
            GameAudio.ToolStroke(bolt.Kind,bolt.transform.position);
            var rust=bolt.GetComponent<RepairRust>();if(!rust)rust=bolt.gameObject.AddComponent<RepairRust>();rust.travel+=Mathf.Abs(accepted);rust.Emit();
        }
        void Emit()
        {
            if(Time.time<next)return;next=Time.time+(travel<45?.12f:.23f);
            if(!particles)
            {
                var material=RepairFeedbackFx.DustMaterial;if(!material)return;
                particles=WeatherImpactFx.MakeParticles(transform,"Fastener / rust flakes",material,.45f,.01f,48,false);
                particles.gameObject.layer=gameObject.layer;
                var gravity=particles.main;gravity.gravityModifier=.25f;
                var size=particles.sizeOverLifetime;size.enabled=false;
                var renderer=particles.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;renderer.mesh=RepairFeedbackFx.FlakeMesh;
                gravity.startRotation3D=true;gravity.startRotationX=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);gravity.startRotationY=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
                var rotation=particles.rotationOverLifetime;rotation.enabled=true;rotation.separateAxes=true;rotation.x=1.7f;rotation.y=2.4f;
            }
            float unit=Mathf.Max(.1f,PlayerBody.Scale);
            if(!particles.isPlaying)particles.Play();
            int count=first?8:travel<45?3:1;
            for(int i=0;i<count;i++)particles.Emit(new ParticleSystem.EmitParams
            {
                position=transform.position+transform.up*.012f*unit,velocity=(new Vector3(Sample(-1,1),Sample(-1,1),Sample(-1,1))*.18f+transform.up*.12f)*unit,
                startSize=Sample(first?.021f:.007f,first?.046f:.016f)*unit,startLifetime=Sample(.4f,.9f),startColor=new Color(.55f,.29f,.11f,1)
            },1);
            first=false;
        }
    }
}
