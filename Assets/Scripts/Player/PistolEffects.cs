using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    internal static class PistolEffects
    {
        static readonly Queue<GameObject> marks=new Queue<GameObject>();
        public static void Shot(Vector3 muzzle,Quaternion rotation,float scale,Material fx,Material brass)
        {
            var flash=Particles("Pistol / muzzle flash",muzzle,rotation,fx,.035f,.07f*scale,new Color(1,.65f,.18f),0);
            flash.Emit(3);Object.Destroy(flash.gameObject,.12f);
            var light=flash.gameObject.AddComponent<Light>();light.color=new Color(1,.65f,.25f);light.intensity=22;light.range=3*scale;
            var smoke=Particles("Pistol / muzzle smoke",muzzle,rotation,fx,.45f,.04f*scale,new Color(.7f,.72f,.7f,.24f),.18f*scale);
            var size=smoke.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.5f,1,4));
            smoke.Emit(5);Object.Destroy(smoke.gameObject,.8f);
            var casing=GameObject.CreatePrimitive(PrimitiveType.Cylinder);casing.name="Pistol / spent casing";
            casing.transform.SetPositionAndRotation(muzzle+rotation*new Vector3(.025f,0,-.09f)*scale,rotation*Quaternion.Euler(0,0,90));
            casing.transform.localScale=new Vector3(.008f,.01f,.008f)*scale;
            if(brass)casing.GetComponent<Renderer>().sharedMaterial=brass;
            var body=casing.AddComponent<Rigidbody>();body.mass=.008f;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity=rotation*new Vector3(Random.Range(1.2f,2f),Random.Range(.5f,1),Random.Range(-.5f,0))*scale;
            body.angularVelocity=Random.insideUnitSphere*22;
            Object.Destroy(casing,4);
        }
        public static void Impact(RaycastHit hit,float scale,Material fx,Material markMaterial)
        {
            bool metal=hit.collider.sharedMaterial && hit.collider.sharedMaterial.name.ToLowerInvariant().Contains("metal");
            string name=hit.collider.name.ToLowerInvariant();
            metal|=name.Contains("fence") || name.Contains("cargo") || name.Contains("robot") || name.Contains("metal");
            var dust=Particles("Pistol / impact dust",hit.point+hit.normal*.006f*scale,Quaternion.LookRotation(hit.normal),fx,.55f,.035f*scale,new Color(.5f,.48f,.42f,.6f),.5f*scale);
            var size=dust.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.2f,1,3));
            dust.Emit(8);Object.Destroy(dust.gameObject,1);
            if(metal)
            {
                var sparks=Particles("Pistol / sparks",hit.point,Quaternion.LookRotation(hit.normal),fx,.18f,.008f*scale,new Color(1,.7f,.25f),3*scale);
                sparks.Emit(7);Object.Destroy(sparks.gameObject,.4f);
            }
            var mark=GameObject.CreatePrimitive(PrimitiveType.Quad);mark.name="Pistol / bullet impact";
            Object.Destroy(mark.GetComponent<Collider>());
            mark.transform.SetPositionAndRotation(hit.point+hit.normal*.002f*scale,Quaternion.LookRotation(hit.normal)*Quaternion.Euler(0,0,Random.Range(0,360)));
            mark.transform.localScale=Vector3.one*(.025f*scale);
            mark.transform.SetParent(hit.collider.transform,true);
            var renderer=mark.GetComponent<Renderer>();if(markMaterial)renderer.sharedMaterial=markMaterial;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            marks.Enqueue(mark);
            while(marks.Count>48){var old=marks.Dequeue();if(old)Object.Destroy(old);}
            Object.Destroy(mark,35);
        }
        static ParticleSystem Particles(string name,Vector3 position,Quaternion rotation,Material material,float lifetime,float size,Color color,float speed)
        {
            var go=new GameObject(name);go.transform.SetPositionAndRotation(position,rotation);
            var system=go.AddComponent<ParticleSystem>();system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=system.main;main.loop=false;main.duration=.05f;main.startLifetime=new ParticleSystem.MinMaxCurve(lifetime*.65f,lifetime);
            main.startSize=new ParticleSystem.MinMaxCurve(size*.7f,size*1.3f);main.startSpeed=new ParticleSystem.MinMaxCurve(speed*.5f,speed);
            main.startColor=color;main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=32;main.playOnAwake=false;
            var emission=system.emission;emission.enabled=false;
            var shape=system.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=35;shape.radius=size*.2f;
            var fade=system.colorOverLifetime;fade.enabled=true;var gradient=new Gradient();
            gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)});fade.color=gradient;
            var renderer=go.GetComponent<ParticleSystemRenderer>();if(material)renderer.sharedMaterial=material;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            system.Play();return system;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void Reset()=>marks.Clear();
    }

}
