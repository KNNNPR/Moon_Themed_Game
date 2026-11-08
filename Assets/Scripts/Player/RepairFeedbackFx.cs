using System.Collections.Generic;
using MoonThemedGame.Weather;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoonThemedGame.Repair
{
    // Contact-only cosmetics. No forces, colliders, work progress or gameplay timers.
    [DisallowMultipleComponent]
    public sealed class RepairFeedbackFx : MonoBehaviour
    {
        RepairRobotTarget owner;
        Transform effects;
        ParticleSystem dust,chips,sparks,vapor;
        LineRenderer connector;
        bool electrical,battery,seated,contacted,vented,spun;
        float seatedAt,nextSpark,lastSpark=-10;
        System.Random random;
        static Mesh flakeMesh;
        public int ContactBursts {get;private set;}
        public int HammerBursts {get;private set;}
        public int SparkBursts {get;private set;}
        public int WheelBursts {get;private set;}
        public int VentBursts {get;private set;}
        public static Material DustMaterial=>Resources.Load<Material>("RepairFeedbackParticle");
        float Unit=>Mathf.Max(.1f,PlayerBody.Scale);
        float Range(float min,float max){if(random==null)random=new System.Random(GetInstanceID());return min+(max-min)*(float)random.NextDouble();}
        Vector3 Scatter(){var point=new Vector3(Range(-1,1),Range(-1,1),Range(-1,1));return point.sqrMagnitude>1?point.normalized*Range(0,1):point;}
        internal static Mesh FlakeMesh
        {
            get
            {
                if(!flakeMesh)
                {
                    flakeMesh=new Mesh{name="Repair / chipped coating",hideFlags=HideFlags.DontSave,
                        vertices=new[]{new Vector3(-.5f,-.2f,0),new Vector3(.25f,-.45f,0),new Vector3(.5f,.05f,0),new Vector3(.1f,.5f,0),new Vector3(-.4f,.25f,0)},
                        uv=new[]{new Vector2(0,.3f),new Vector2(.75f,0),new Vector2(1,.55f),new Vector2(.6f,1),new Vector2(.1f,.75f)},triangles=new[]{0,1,2,0,2,3,0,3,4}};
                    flakeMesh.RecalculateNormals();flakeMesh.RecalculateBounds();
                }
                return flakeMesh;
            }
        }
        static readonly Color DustColor=new Color(.52f,.49f,.43f,.36f);

        public static RepairFeedbackFx For(GameObject part)
        {
            if(!part||!Application.isPlaying)return null;
            var fx=part.GetComponent<RepairFeedbackFx>();if(!fx)fx=part.AddComponent<RepairFeedbackFx>();
            fx.owner=part.GetComponentInParent<RepairRobotTarget>();
            string name=part.name.ToLowerInvariant();fx.battery=name.Contains("battery");
            fx.electrical=fx.battery||name.Contains("headlight")||name.Contains("connector");return fx;
        }
        internal void ResetForOrder(RepairRobotTarget robot)
        {
            owner=robot;seated=contacted=vented=spun=false;lastSpark=-10;
            ContactBursts=HammerBursts=SparkBursts=WheelBursts=VentBursts=0;
            if(effects){effects.gameObject.SetActive(false);Destroy(effects.gameObject);}
            effects=null;dust=chips=sparks=vapor=null;connector=null;
        }
        Transform Host
        {
            get
            {
                if(!effects){effects=new GameObject("Repair / contact VFX").transform;effects.SetParent(transform,false);effects.gameObject.layer=gameObject.layer;}
                return effects;
            }
        }
        ParticleSystem Particles(ref ParticleSystem field,string name,bool solid=false)
        {
            if(field){if(!field.isPlaying)field.Play();return field;}var material=DustMaterial;if(!material)return null;
            field=WeatherImpactFx.MakeParticles(Host,name,material,1,.01f,64,false);
            field.gameObject.layer=gameObject.layer;
            var main=field.main;main.gravityModifier=solid?.16f*Unit:0;
            if(solid)
            {
                var renderer=field.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;renderer.mesh=FlakeMesh;
                main.startRotation3D=true;main.startRotationX=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);main.startRotationY=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
                var rotation=field.rotationOverLifetime;rotation.enabled=true;rotation.separateAxes=true;rotation.x=1.7f;rotation.y=2.4f;rotation.z=.8f;
                var size=field.sizeOverLifetime;size.enabled=false;
                var color=field.colorOverLifetime;var gradient=new Gradient();
                gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                    new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,.7f),new GradientAlphaKey(0,1)});color.color=gradient;
            }
            return field;
        }
        void Puff(Vector3 point,Vector3 direction,int count,float size,float speed,Color color,bool steam=false)
        {
            var system=steam?Particles(ref vapor,"Repair / vent residue"):Particles(ref dust,"Repair / joint dust");if(!system)return;
            count=Mathf.RoundToInt(count*1.5f);size*=1.8f;speed*=1.25f;color.a=Mathf.Clamp01(color.a*1.5f);
            for(int i=0;i<count;i++)
                system.Emit(new ParticleSystem.EmitParams{position=point+Scatter()*(size*.16f),
                    velocity=(direction+Scatter()*.65f)*Range(.4f,1)*speed,
                    startSize=size*Range(.6f,1.2f),startLifetime=Range(.65f,steam?1.8f:1.25f),startColor=color},1);
        }
        internal void InitialContact()
        {
            if(contacted)return;contacted=true;ContactBursts++;
            var bounds=RepairPartMount.GeometryBounds(transform);var normal=Outward(bounds);
            var side=Vector3.Cross(normal,Vector3.up).normalized;
            float width=Vector3.Dot(bounds.extents,Abs(side));
            for(int i=-1;i<=1;i+=2)
                Puff(bounds.center+side*(width*.7f*i)-Vector3.up*bounds.extents.y*.45f,normal*.35f+side*i,6,.045f*Unit,.15f*Unit,DustColor);
        }
        internal void Seated()
        {
            if(seated)return;seated=true;seatedAt=Time.time;nextSpark=Time.time+.2f;InitialContact();
            if(!electrical)return;
            var bounds=RepairPartMount.GeometryBounds(transform);var normal=Outward(bounds);
            var target=bounds.center+Vector3.up*bounds.extents.y*.4f;
            using(var surface=new Surface(transform))
            {
                var anchor=surface.Point(target,normal,bounds);
                normal=surface.Normal;
                connector=Arc("Connector / power contact",surface,anchor,normal,bounds,.015f*Unit,0,250,.004f*Unit,new Color(.2f,.25f,.2f,.8f));
            }
        }
        internal void Hammer(Vector3 viewer)
        {
            HammerBursts++;
            var bounds=RepairPartMount.GeometryBounds(transform);var normal=(viewer-bounds.center).normalized;
            if(normal.sqrMagnitude<.1f)normal=Outward(bounds);
            Vector3 point;
            using(var surface=new Surface(transform))
            {
                point=surface.Point(bounds.center,normal,bounds);
                normal=surface.Normal;
            }
            var system=Particles(ref chips,"Repair / coating chips",true);
            if(system)for(int i=0;i<11;i++)system.Emit(new ParticleSystem.EmitParams{position=point,
                velocity=(normal*.5f+Scatter()*.7f)*Range(.18f,.55f)*Unit,
                startSize=Range(.008f,.027f)*Unit,startLifetime=Range(.45f,1f),startColor=new Color(.43f,.37f,.28f,.98f)},1);
            Puff(point,normal,5,.035f*Unit,.14f*Unit,DustColor);
            Puff(new Vector3(bounds.center.x,bounds.min.y,bounds.center.z),Vector3.down,7,.033f*Unit,.11f*Unit,DustColor);
        }
        bool HasSupply=>battery||owner&&owner.TryGetComponent<RepairRobotRevival>(out var revival)&&revival.Powered;
        bool Secured=>owner&&owner.IsPartSecured(gameObject);
        internal void ContactFault()
        {
            if(!seated||!electrical||!HasSupply||Secured||owner&&owner.IsDismantling||Time.time-lastSpark<3)return;
            lastSpark=Time.time;nextSpark=Time.time+Range(4,7);SparkBursts++;
            var bounds=RepairPartMount.GeometryBounds(transform);var normal=Outward(bounds);
            Vector3 point=connector?connector.transform.position:bounds.center;
            var system=Particles(ref sparks,"Repair / brief contact spark",true);
            if(system)for(int i=0;i<6;i++)system.Emit(new ParticleSystem.EmitParams{position=point,
                velocity=(normal+Scatter()*.65f)*Range(.4f,1.1f)*Unit,
                startSize=Range(.006f,.014f)*Unit,startLifetime=Range(.12f,.28f),startColor=new Color(2.2f,1.75f,1.1f,1)},1);
        }
        internal void WheelStarted()
        {
            if(spun)return;spun=true;WheelBursts++;
            var bounds=RepairPartMount.GeometryBounds(transform);var normal=Outward(bounds);
            Puff(new Vector3(bounds.center.x,bounds.min.y+Mathf.Min(bounds.size.y*.15f,.06f*Unit),bounds.center.z),normal*.6f+Vector3.down*.4f,10,.055f*Unit,.23f*Unit,DustColor);
        }
        internal void BootVent()
        {
            if(vented)return;vented=true;VentBursts++;
            var bounds=RepairPartMount.GeometryBounds(transform);var normal=Outward(bounds);
            // Residual dust/condensation from the power module's upper seam, not fire.
            Puff(bounds.center+normal*Vector3.Dot(bounds.extents,Abs(normal))+Vector3.up*bounds.extents.y*.6f,
                Vector3.up+normal*.35f,5,.05f*Unit,.09f*Unit,new Color(.53f,.55f,.51f,.18f),true);
        }
        void Update()
        {
            if(!seated||!electrical||owner&&(!owner.Accepts||owner.IsDismantling))return;
            bool supply=HasSupply,secured=Secured;
            if(connector)
            {
                float elapsed=Time.time-seatedAt;
                float glow=supply?(secured?.85f:Mathf.Exp(-elapsed*.6f)*(.5f+.5f*Mathf.Sin(elapsed*36))+.2f):.035f;
                var color=secured?new Color(.44f,.72f,.48f,.8f):new Color(.9f,.62f,.25f,.85f);
                color*=glow;connector.startColor=connector.endColor=color;
            }
            if(supply&&!secured&&Time.time>=nextSpark)
            {
                if(Range(0,1)<.35f)ContactFault();else nextSpark=Time.time+Range(4,7);
            }
        }
        void OnEnable()
        {
            // Clear old cosmetic marks too when recompiling an existing Play session.
            if(!Application.isPlaying)return;
            foreach(var line in GetComponentsInChildren<LineRenderer>(true))
                if(line.name=="Fastener / polished thread contact"||line.name=="Hammer / worn coating")
                {line.gameObject.SetActive(false);Destroy(line.gameObject);}
        }
        Vector3 Outward(Bounds bounds)
        {
            var normal=owner?bounds.center-owner.transform.position:transform.forward;normal.y=0;
            return normal.sqrMagnitude>.001f?normal.normalized:transform.forward;
        }
        LineRenderer Arc(string name,Surface surface,Vector3 point,Vector3 normal,Bounds bounds,float radius,float start,float sweep,float width,Color color)
        {
            var material=DustMaterial;if(!material)return null;
            var host=new GameObject(name);host.layer=gameObject.layer;host.transform.SetParent(Host,false);
            host.transform.SetPositionAndRotation(point,Quaternion.LookRotation(normal));
            // Geometry is projected onto real triangles, including non-readable FBX meshes.
            var line=host.AddComponent<LineRenderer>();line.sharedMaterial=material;line.useWorldSpace=false;line.alignment=LineAlignment.TransformZ;
            line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.textureMode=LineTextureMode.Stretch;
            line.startWidth=line.endWidth=width;line.startColor=line.endColor=color;line.numCapVertices=2;line.positionCount=13;
            var run=new List<Vector3>();var best=new List<Vector3>();
            for(int i=0;i<13;i++)
            {
                float angle=(start+sweep*i/12f)*Mathf.Deg2Rad;
                var wanted=point+host.transform.right*(Mathf.Cos(angle)*radius)+host.transform.up*(Mathf.Sin(angle)*radius);
                if(surface.TryPoint(wanted,normal,bounds,out var hit))run.Add(host.transform.InverseTransformPoint(hit+normal*.0009f*Unit));
                else{if(run.Count>best.Count)best=new List<Vector3>(run);run.Clear();}
            }
            if(run.Count>best.Count)best=run;
            // Clip a scuff at holes and module edges instead of bridging empty air.
            if(best.Count<3){host.SetActive(false);Destroy(host);return null;}
            line.positionCount=best.Count;line.SetPositions(best.ToArray());
            return line;
        }
        void OnDisable()
        {
            foreach(var system in new[]{dust,chips,sparks,vapor})if(system)system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        static Vector3 Abs(Vector3 v)=>new Vector3(Mathf.Abs(v.x),Mathf.Abs(v.y),Mathf.Abs(v.z));
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void ResetMesh(){if(flakeMesh)Destroy(flakeMesh);flakeMesh=null;}
        sealed class Surface:System.IDisposable
        {
            readonly List<MeshCollider> proxies=new List<MeshCollider>();
            readonly List<Mesh> baked=new List<Mesh>();
            public Vector3 Normal {get;private set;}
            public Surface(Transform root)
            {
                foreach(var mesh in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if(!mesh.sharedMesh||mesh.GetComponentInParent<RepairBolt>())continue;
                    if(mesh.TryGetComponent<Renderer>(out var original)&&!original.enabled&&
                        mesh.GetComponentsInChildren<SkinnedMeshRenderer>().Length>0)continue;
                    var host=new GameObject("Repair VFX / surface query"){hideFlags=HideFlags.HideAndDontSave,layer=31};
                    host.transform.SetPositionAndRotation(mesh.transform.position,mesh.transform.rotation);host.transform.localScale=mesh.transform.lossyScale;
                    var proxy=host.AddComponent<MeshCollider>();proxy.sharedMesh=mesh.sharedMesh;proxies.Add(proxy);
                }
                foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if(!skin.enabled||!skin.sharedMesh||skin.GetComponentInParent<RepairBolt>())continue;
                    var mesh=new Mesh{name="Repair VFX / deformed surface"};skin.BakeMesh(mesh);
                    // Thin deformed shells can expose either side of a triangle.
                    // Make only this temporary query mesh two-sided; keep the
                    // authored render mesh and all gameplay colliders unchanged.
                    var faces=mesh.triangles;var doubleFaces=new int[faces.Length*2];System.Array.Copy(faces,doubleFaces,faces.Length);
                    for(int i=0;i<faces.Length;i+=3){doubleFaces[faces.Length+i]=faces[i];doubleFaces[faces.Length+i+1]=faces[i+2];doubleFaces[faces.Length+i+2]=faces[i+1];}
                    mesh.triangles=doubleFaces;mesh.RecalculateBounds();baked.Add(mesh);
                    var host=new GameObject("Repair VFX / deformed surface query"){hideFlags=HideFlags.HideAndDontSave,layer=31};
                    host.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);host.transform.localScale=skin.transform.lossyScale;
                    var proxy=host.AddComponent<MeshCollider>();proxy.sharedMesh=mesh;proxies.Add(proxy);
                }
                Physics.SyncTransforms();
            }
            public Vector3 Point(Vector3 wanted,Vector3 normal,Bounds bounds)
            {
                if(TryPoint(wanted,normal,bounds,out var point))return point;
                // A module may be hollow at the bounding-box centre. Find nearby
                // visible metal rather than drawing wear or emitting from empty air.
                var a=Vector3.Cross(normal,Mathf.Abs(normal.y)>.9f?Vector3.forward:Vector3.up).normalized;
                var b=Vector3.Cross(normal,a).normalized;float xExtent=Vector3.Dot(bounds.extents,Abs(a)),yExtent=Vector3.Dot(bounds.extents,Abs(b));
                float closest=float.PositiveInfinity;Vector3 result=wanted,hitNormal=normal;
                for(int x=-3;x<=3;x++)for(int y=-3;y<=3;y++)
                    if(TryPoint(wanted+a*(x*xExtent*.29f)+b*(y*yExtent*.29f),normal,bounds,out point)&&(point-wanted).sqrMagnitude<closest)
                    {closest=(point-wanted).sqrMagnitude;result=point;hitNormal=Normal;}
                Normal=hitNormal;return result;
            }
            public bool TryPoint(Vector3 wanted,Vector3 normal,Bounds bounds,out Vector3 point)
            {
                point=default;Normal=normal;float reach=Mathf.Max(.1f,bounds.size.magnitude+.05f),nearest=float.PositiveInfinity;
                var ray=new Ray(wanted+normal*reach,-normal);
                foreach(var proxy in proxies)if(proxy.Raycast(ray,out var hit,reach*2)&&hit.distance<nearest&&Vector3.Dot(hit.normal,normal)>.1f)
                {nearest=hit.distance;point=hit.point;Normal=hit.normal;}
                return !float.IsPositiveInfinity(nearest);
            }
            public void Dispose()
            {
                foreach(var proxy in proxies)if(proxy){proxy.enabled=false;proxy.gameObject.SetActive(false);Object.Destroy(proxy.gameObject);}
                foreach(var mesh in baked)Object.Destroy(mesh);
            }
        }
    }
}

