using UnityEngine;

namespace MoonThemedGame.Interactions
{
    // Mesh particles preserve the chunky game style without spawning dozens of rigidbodies.
    public sealed class AppleShotBurst : MonoBehaviour
    {
        static Mesh chunk;
        Material fragmentMaterial;

        public static void Emit(Bounds bounds,Vector3 direction,Vector3 inheritedVelocity,Material template,int count,float lifetime)
        {
            var root=new GameObject("Apple / shot burst");root.transform.position=bounds.center;
            var burst=root.AddComponent<AppleShotBurst>();
            var shader=template?template.shader:Shader.Find("Universal Render Pipeline/Particles/Unlit");
            burst.fragmentMaterial=template?new Material(template):new Material(shader);
            burst.fragmentMaterial.name="Apple / fragment material";
            burst.fragmentMaterial.SetTexture("_BaseMap",Texture2D.whiteTexture);
            burst.fragmentMaterial.SetColor("_BaseColor",Color.white);
            float diameter=Mathf.Clamp(bounds.size.magnitude/Mathf.Sqrt(3),.06f,2);
            count=Mathf.Clamp(count,8,128);lifetime=Mathf.Clamp(lifetime,.5f,4);
            var particles=root.AddComponent<ParticleSystem>();particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=particles.main;main.loop=false;main.playOnAwake=false;main.duration=.05f;
            main.simulationSpace=ParticleSystemSimulationSpace.World;main.startSpeed=0;
            main.startSize=new ParticleSystem.MinMaxCurve(diameter*.045f,diameter*.10f);
            main.startLifetime=new ParticleSystem.MinMaxCurve(lifetime*.65f,lifetime);
            main.gravityModifier=.06f;main.maxParticles=count;main.startRotation3D=true;
            var emission=particles.emission;emission.enabled=false;var shape=particles.shape;shape.enabled=false;
            var fade=particles.colorOverLifetime;fade.enabled=true;var gradient=new Gradient();
            gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,.7f),new GradientAlphaKey(0,1)});fade.color=gradient;
            var spin=particles.rotationOverLifetime;spin.enabled=true;spin.separateAxes=true;
            spin.x=new ParticleSystem.MinMaxCurve(-5,5);spin.y=new ParticleSystem.MinMaxCurve(-5,5);spin.z=new ParticleSystem.MinMaxCurve(-5,5);
            var collision=particles.collision;collision.enabled=true;collision.type=ParticleSystemCollisionType.World;
            collision.mode=ParticleSystemCollisionMode.Collision3D;collision.quality=ParticleSystemCollisionQuality.Low;
            collision.dampen=.4f;collision.bounce=.25f;collision.radiusScale=.45f;
            var renderer=root.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;
            renderer.mesh=Chunk();renderer.sharedMaterial=burst.fragmentMaterial;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            particles.Play();
            for(int i=0;i<count;i++)
            {
                var radial=Random.onUnitSphere;
                bool skin=i<count*.6f;
                var color=skin?Color.Lerp(new Color(.38f,.025f,.015f),new Color(.85f,.13f,.045f),Random.value)
                    :Color.Lerp(new Color(.72f,.64f,.36f),new Color(1,.94f,.7f),Random.value);
                particles.Emit(new ParticleSystem.EmitParams{
                    position=bounds.center+Vector3.Scale(Random.insideUnitSphere,bounds.extents)*.65f,
                    velocity=inheritedVelocity+radial*Random.Range(.8f,2.2f)*diameter+direction.normalized*(diameter*.65f),
                    startColor=color,rotation3D=Random.insideUnitSphere*180},1);
            }
            Destroy(root,lifetime+.3f);
        }
        static Mesh Chunk()
        {
            if(chunk)return chunk;
            chunk=new Mesh{name="Apple / pixel chunk"};
            chunk.vertices=new[]{new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(-.5f,.5f,-.5f),
                new Vector3(-.5f,-.5f,.5f),new Vector3(.5f,-.5f,.5f),new Vector3(.5f,.5f,.5f),new Vector3(-.5f,.5f,.5f)};
            chunk.triangles=new[]{0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,2,3,7,2,7,6,1,2,6,1,6,5,3,0,4,3,4,7};
            chunk.RecalculateNormals();chunk.RecalculateBounds();return chunk;
        }
        void OnDestroy(){if(fragmentMaterial)Destroy(fragmentMaterial);}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset(){if(chunk)Destroy(chunk);chunk=null;}
    }
}
