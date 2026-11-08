using System.Collections;
using System.Collections.Generic;
using MoonThemedGame.Computer;
using MoonThemedGame.Interactions;
using MoonThemedGame.Repair;
using MoonThemedGame.Scavenger;
using MoonThemedGame.Weather;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace MoonThemedGame.Audio
{
    // All sources obey the existing AudioListener master slider, including radio and world loops.
    public sealed class GameAudio : MonoBehaviour
    {
        sealed class Channel
        {
            public AudioSource source;public AudioLowPassFilter low;public AudioHighPassFilter high;public AudioDistortionFilter distortion;public bool roomRadio;
        }
        static GameAudio active;
        static GameAudioCatalog catalog;
        public static GameAudioCatalog Catalog=>catalog?catalog:catalog=Resources.Load<GameAudioCatalog>("GameAudioCatalog");
        static GameAudio Instance
        {
            get
            {
                if(!Application.isPlaying)return null;
                if(!active){var host=new GameObject("Station / Audio");active=host.AddComponent<GameAudio>();DontDestroyOnLoad(host);}
                return active;
            }
        }
        readonly List<Channel> channels=new List<Channel>();
        readonly List<AudioSource> voices=new List<AudioSource>();
        readonly Dictionary<AudioSource,int> voicePlays=new Dictionary<AudioSource,int>();
        readonly Dictionary<Sound,float> last=new Dictionary<Sound,float>();
        readonly Dictionary<object,object> hovered=new Dictionary<object,object>();
        readonly Dictionary<object,int> hoverFrames=new Dictionary<object,int>();
        readonly HashSet<VisualElement> wired=new HashSet<VisualElement>();
        AudioSource music,inside,outside,vent,engine,acceleration,ride,concreteRide,metalRide,radiation,tool,networkTone,hacker;
        float networkEnds,networkFade,networkVolume;
        Sound networkCue;
        int lossKind,lossSatellite=-1;
        readonly RaycastHit[] surfaceHits=new RaycastHit[48];
        Sound groundRide=Sound.MoonRide;
        float surfaceAt;
        float toolUntil;
        AudioClip desiredMusic;
        ScavengerLink link;
        PcMonitorTerminal pc;
        MoonWeatherSystem weather;
        float resolveAt,transmission,batteryAt;
        bool wasLow;
        int musicScene=-1;
        public static int EventsPlayed {get;private set;}
        public static Sound LastEvent {get;private set;}

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset(){active=null;catalog=null;EventsPlayed=0;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install(){var instance=Instance;}
        void Awake(){active=this;AudioListener.volume=PlayerPrefs.GetFloat("audio.master",.8f);}
        AudioSource Source(string label,Transform parent=null)
        {
            var host=new GameObject(label);host.transform.SetParent(parent?parent:transform,false);
            var source=host.AddComponent<AudioSource>();source.playOnAwake=false;source.dopplerLevel=0;source.rolloffMode=AudioRolloffMode.Logarithmic;
            Register(source);return source;
        }
        void Register(AudioSource source,bool roomRadio=false)
        {
            if(!source)return;
            var existing=channels.Find(c=>c.source==source);
            if(existing!=null){existing.roomRadio|=roomRadio;return;}
            source.ignoreListenerVolume=false;
            var low=source.GetComponent<AudioLowPassFilter>();if(!low)low=source.gameObject.AddComponent<AudioLowPassFilter>();
            var high=source.GetComponent<AudioHighPassFilter>();if(!high)high=source.gameObject.AddComponent<AudioHighPassFilter>();
            var distortion=source.GetComponent<AudioDistortionFilter>();if(!distortion)distortion=source.gameObject.AddComponent<AudioDistortionFilter>();
            channels.Add(new Channel{source=source,low=low,high=high,distortion=distortion,roomRadio=roomRadio});
            low.enabled=high.enabled=distortion.enabled=transmission>.001f;
        }
        public static void Route(AudioSource source){Instance?.Register(source);}
        public static void RouteRadio(AudioSource source){Instance?.Register(source,true);}
        public static Camera RadioOperatorCamera=>active&&active.link&&active.link.Connected&&active.link.robotCamera&&active.link.robotCamera.enabled
            ?active.link.OperatorCamera:null;
        public static float RadioVolume(AudioClip clip)
        {
            if(!Catalog)return .3f;var tracks=Catalog.RadioTracks;
            for(int i=0;i<tracks.Length;i++)if(tracks[i]==clip)
                return Catalog.radioVolume*(Catalog.radioTrackGains!=null&&i<Catalog.radioTrackGains.Length?Catalog.radioTrackGains[i]:1);
            return Catalog.radioVolume;
        }
        public static void Carried(RepairCarryable item,bool drop)
        {
            if(!item)return;
            var cue=item is PlayerPistol?(drop?Sound.GunDrop:Sound.GunPickup):
                item is RepairPartProp?(drop?Sound.PartDrop:Sound.PartPickup):!drop?Sound.ToolPickup:
                item is RepairTool?Sound.HammerDrop:item is RepairWrench wrench&&wrench.drives==Fastener.Screw?Sound.ScrewdriverDrop:Sound.WrenchDrop;
            At(cue,item.RendererBounds().center);
        }
        public static void ToolStroke(Fastener kind,Vector3 point)
        {
            var service=Instance;if(!service||!Catalog)return;
            var cue=kind==Fastener.Bolt?Sound.Wrench:Sound.Screwdriver;
            if(!service.tool)service.tool=service.Source("Working tool");
            var source=service.tool;var clip=Catalog.Clip(cue);if(!clip)return;
            if(source.clip!=clip){source.Stop();source.clip=clip;}
            source.transform.position=point;source.spatialBlend=1;source.minDistance=3;source.maxDistance=22;source.loop=true;
            if(!source.isPlaying){source.volume=0;source.Play();}service.toolUntil=Time.unscaledTime+.14f;
        }
        public static AudioSource At(Sound sound,Vector3 position,float gain=1)=>Instance?.Play(sound,position,true,gain);
        public static AudioSource Ui(Sound sound,float gain=1)=>Instance?.Play(sound,Vector3.zero,false,gain);
        public static AudioSource PistolShot(AudioClip custom,Vector3 point)=>Instance?.Play(Sound.Gunshot,point,true,custom?.9f:1,custom);
        public static void RoverConnecting()
        {
            var service=Instance;if(!service)return;
            service.lossKind=0;service.lossSatellite=-1;
            service.NetworkTone(Sound.ConnectionLost,.32f);
        }
        public static void RoverExiting()=>Instance?.NetworkTone(Sound.ConnectionLost,.55f);
        public static void RoverReturned()
        {
            if(!active)return;
            active.StopNetwork();if(active.hacker)active.hacker.Stop();
            active.lossKind=0;active.lossSatellite=-1;
        }
        public static void RoverAttacked()
        {
            var service=Instance;if(!service||!Catalog)return;
            service.StopNetwork();
            if(!service.hacker)service.hacker=service.Source("Rover / hacker attack");
            service.hacker.Stop();service.hacker.clip=Catalog.Clip(Sound.HackerLaugh);service.hacker.loop=false;
            service.hacker.spatialBlend=0;service.hacker.priority=16;service.hacker.volume=Catalog.Volume(Sound.HackerLaugh);
            if(service.hacker.clip){service.hacker.Play();EventsPlayed++;LastEvent=Sound.HackerLaugh;}
        }
        AudioSource NetworkTone(Sound cue,float seconds,float gain=1)
        {
            if(!Catalog||!Catalog.Clip(cue))return null;
            StopNetwork();if(!networkTone)networkTone=Source("Rover / network tone");
            networkCue=cue;networkTone.clip=Catalog.Clip(cue);networkTone.loop=false;networkTone.spatialBlend=0;networkTone.priority=20;
            networkVolume=Catalog.Volume(cue)*gain;networkTone.volume=networkVolume;networkTone.Play();
            networkEnds=Time.unscaledTime+Mathf.Min(seconds,networkTone.clip.length);networkFade=.12f;
            EventsPlayed++;LastEvent=cue;return networkTone;
        }
        void StopNetwork()
        {
            if(networkTone)networkTone.Stop();
            // Also clear any older pooled warning when updating an existing session.
            if(Catalog)foreach(var voice in voices)if(voice&&(voice.clip==Catalog.Clip(Sound.LosingConnection)||voice.clip==Catalog.Clip(Sound.ConnectionLost)))voice.Stop();
        }
        AudioSource Play(Sound sound,Vector3 position,bool spatial,float gain,AudioClip custom=null)
        {
            if(sound==Sound.LosingConnection||sound==Sound.ConnectionLost)return NetworkTone(sound,sound==Sound.LosingConnection?.6f:.55f,gain);
            if(!Catalog)return null;var clip=custom?custom:Catalog.Clip(sound);if(!clip)return null;
            float spacing=sound==Sound.Keyboard?.065f:sound==Sound.Icon||sound==Sound.MenuHover||sound==Sound.BigHover?.065f:
                sound==Sound.ZombieBang?.35f:sound==Sound.MetalBang||sound==Sound.DirtBang||sound==Sound.FenceBang?.25f:
                sound==Sound.Wrench||sound==Sound.Screwdriver?.22f:sound==Sound.ZombieRoar?.25f:.025f;
            if(last.TryGetValue(sound,out float at)&&Time.unscaledTime-at<spacing)return null;
            last[sound]=Time.unscaledTime;
            AudioSource voice=null;foreach(var item in voices)if(item&&!item.isPlaying){voice=item;break;}
            if(!voice){if(voices.Count<32){voice=Source("One shot");voices.Add(voice);}else{voice=voices[0];voices.RemoveAt(0);voices.Add(voice);voice.Stop();}}
            voice.transform.position=position;voice.spatialBlend=spatial?1:0;voice.minDistance=3;voice.maxDistance=60;
            voice.pitch=sound==Sound.Keyboard?Random.Range(.94f,1.07f):1;voice.volume=(custom?1:Catalog.Volume(sound))*Mathf.Clamp01(gain);
            voice.panStereo=0;voice.loop=false;voice.clip=clip;voice.Play();
            voicePlays.TryGetValue(voice,out int generation);voicePlays[voice]=++generation;
            if(sound==Sound.GameShot||sound==Sound.Gunshot)StartCoroutine(ShotTail(voice,clip,voice.volume,generation));
            if(sound==Sound.Keyboard)voice.SetScheduledEndTime(AudioSettings.dspTime+.06);
            EventsPlayed++;LastEvent=sound;return voice;
        }
        IEnumerator ShotTail(AudioSource source,AudioClip clip,float volume,int generation)
        {
            float fade=Mathf.Min(.16f,clip.length*.45f),start=Time.unscaledTime,end=start+clip.length;
            while(source&&source.isPlaying&&source.clip==clip&&voicePlays.TryGetValue(source,out int current)&&current==generation)
            {
                source.volume=volume*Mathf.SmoothStep(0,1,Mathf.Clamp01((end-Time.unscaledTime)/fade));
                if(Time.unscaledTime>=end){source.Stop();yield break;}
                yield return null;
            }
        }
        public static AudioSource Loop(Sound cue,Transform anchor,float gain=1)
        {
            var service=Instance;if(!service||!Catalog||!Catalog.Clip(cue))return null;
            var source=service.Source(cue.ToString(),anchor);source.clip=Catalog.Clip(cue);source.loop=true;
            source.spatialBlend=anchor?1:0;source.minDistance=3;source.maxDistance=55;source.volume=Catalog.Volume(cue)*gain;source.Play();return source;
        }
        public static void Hover(object owner,object target,Sound sound=Sound.Icon)
        {
            var service=Instance;if(!service||owner==null)return;
            service.hovered.TryGetValue(owner,out var before);service.hovered[owner]=target;
            service.hoverFrames.TryGetValue(owner,out int frame);service.hoverFrames[owner]=Time.frameCount;
            if(target!=null&&(!Equals(before,target)||Time.frameCount-frame>1))Ui(sound);
        }
        public static void WireUi(VisualElement root)
        {
            var service=Instance;if(!service||root==null)return;
            root.Query<Button>().ForEach(button=>
            {
                if(!service.wired.Add(button))return;
                button.RegisterCallback<PointerEnterEvent>(_=>{if(button.enabledInHierarchy)Ui(Sound.Icon);});
                button.RegisterCallback<ClickEvent>(_=>{if(button.enabledInHierarchy)Ui(Sound.Icon);});
            });
        }
        public static void CheckRobot(Transform robot)=>Instance?.StartCoroutine(Instance.CheckSequence(robot));
        IEnumerator CheckSequence(Transform robot)
        {
            var check=robot?At(Sound.RobotCheck,robot.position):null;
            yield return new WaitForSecondsRealtime(check&&check.clip?check.clip.length:.1f);
            if(!robot)yield break;
            var screen=At(Sound.ScreenOn,robot.position);if(!screen)yield break;
            float duration=screen.clip.length,volume=screen.volume;
            for(float t=0;t<duration;t+=Time.unscaledDeltaTime){if(!screen||!screen.isPlaying)yield break;screen.volume=volume*Mathf.Clamp01((duration-t)/Mathf.Min(1,duration));yield return null;}
        }
        public static void VoiceWord()
        {
            var service=Instance;if(service)service.StartCoroutine(service.Voice());
        }
        IEnumerator Voice()
        {
            var source=Ui(Sound.RadioVoice);if(!source)yield break;
            var clip=source.clip;yield return new WaitForSecondsRealtime(.075f);if(source&&source.clip==clip)source.Stop();
        }
        public static void Zombie(ZombieActor zombie,Sound sound)
        {
            if(!zombie)return;
            var avatar=zombie.targetOverride?zombie.targetOverride.GetComponent<CrazyCosmonautsPlayer>():null;
            if(avatar)
            {
                var offset=zombie.transform.position-avatar.transform.position;float gain=Mathf.Clamp01(1-offset.magnitude/40);
                var source=Ui(sound,gain);if(source)source.panStereo=Mathf.Clamp(Vector3.Dot(offset.normalized,avatar.transform.right)*.7f,-.7f,.7f);
            }
            else At(sound,zombie.transform.position);
        }
        public static void WatchZombie(ZombieActor zombie){if(zombie&&!zombie.GetComponent<ZombieAudio>())zombie.gameObject.AddComponent<ZombieAudio>();}
        public static void Footsteps(GameObject owner,bool game=false)
        {if(owner&&!owner.GetComponent<MovementAudio>()){var step=owner.AddComponent<MovementAudio>();step.pcGame=game;}}
        public static void Collision(Collision collision,Vector3 point)
        {
            float speed=collision.relativeVelocity.magnitude;if(speed<1.5f)return;
            if(collision.collider.GetComponentInParent<ZombieActor>()){At(Sound.ZombieBang,point,Mathf.Clamp01(speed/10));return;}
            string objectName=collision.collider.name.ToLowerInvariant();
            if(objectName.Contains("fence")||collision.collider.GetComponentInParent<LocationGate>())
            {At(Sound.FenceBang,point,Mathf.Clamp01(speed/10));return;}
            var renderer=collision.collider.GetComponent<Renderer>();if(!renderer)renderer=collision.collider.GetComponentInParent<Renderer>();
            bool metal=MoonThemedGame.World.LandscapeCollisions.IsChargeBuilding(collision.collider.gameObject)||
                objectName.Contains("station")||objectName.Contains("container")||objectName.Contains("ship");
            if(renderer)foreach(var material in renderer.sharedMaterials)
                if(material){string name=material.name.ToLowerInvariant();if(name.Contains("metal")||name.Contains("cargo")||name.Contains("excavator")||name.Contains("moon_details")||name.Contains("modem"))metal=true;}
            if(metal)At(Sound.MetalBang,point,Mathf.Clamp01(speed/10));
        }
        void Update()
        {
            var scene=SceneManager.GetActiveScene();
            if(musicScene!=scene.handle)
            {
                musicScene=scene.handle;link=null;pc=null;weather=null;hovered.Clear();hoverFrames.Clear();resolveAt=0;
                foreach(var voice in voices)if(voice)voice.Stop();
                StopNetwork();if(hacker)hacker.Stop();lossKind=0;lossSatellite=-1;
                foreach(var loop in new[]{inside,outside,vent,engine,acceleration,ride,concreteRide,metalRide,radiation})if(loop)Destroy(loop.gameObject);
                inside=outside=vent=engine=acceleration=ride=concreteRide=metalRide=radiation=null;wasLow=false;
            }
            bool menu=scene.name.Equals("MENU",System.StringComparison.OrdinalIgnoreCase);
            desiredMusic=Catalog?(menu?Catalog.Clip(Sound.MenuMusic):StationStoryFlow.Active&&StationStoryFlow.Active.CreditsVisible?Catalog.Clip(Sound.CreditsMusic):null):null;
            if(!music){music=Source("Menu / credits music");music.volume=0;}
            float desiredVolume=desiredMusic&&Catalog?Catalog.Volume(menu?Sound.MenuMusic:Sound.CreditsMusic):0;
            if(music.clip!=desiredMusic)
            {
                music.volume=Mathf.MoveTowards(music.volume,0,Time.unscaledDeltaTime*.7f);
                if(music.volume<.001f){music.Stop();music.clip=desiredMusic;music.loop=true;if(desiredMusic)music.Play();}
            }
            else music.volume=Mathf.MoveTowards(music.volume,desiredVolume,Time.unscaledDeltaTime*.5f);
            if(Time.unscaledTime>=resolveAt)
            {
                resolveAt=Time.unscaledTime+.5f;
                foreach(var source in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))Register(source);
                if(scene.name=="Operator")
                {
                    if(!link)link=FindFirstObjectByType<ScavengerLink>();if(!weather)weather=MoonWeatherSystem.Active;if(!pc)pc=FindFirstObjectByType<PcMonitorTerminal>();
                    var human=FindFirstObjectByType<FirstPersonController>();if(human)Footsteps(human.gameObject);
                    if(weather&&!inside)
                    {
                        inside=Loop(Sound.BaseAmbient,null,0);outside=Loop(Sound.MoonAmbient,null,0);
                        var anchor=new GameObject("Base / quiet spatial ventilation");SceneManager.MoveGameObjectToScene(anchor,scene);
                        anchor.transform.position=weather.shelterCenter+Vector3.up*(weather.shelterSize.y*.3f);vent=Loop(Sound.Ventilation,anchor.transform,.045f);
                        if(vent){vent.minDistance=2;vent.maxDistance=30;}
                    }
                    if(link&&link.robot&&!acceleration)
                    {engine=Loop(Sound.Engine,link.robot,0);acceleration=Loop(Sound.Acceleration,link.robot,0);ride=Loop(Sound.MoonRide,link.robot,0);concreteRide=Loop(Sound.ConcreteRide,link.robot,0);metalRide=Loop(Sound.MetalRide,link.robot,0);radiation=Loop(Sound.Radiation,link.robot,0);}
                }
                var settings=FindFirstObjectByType<GameSettingsMenu>();if(settings&&settings.Root!=null)WireUi(settings.Root);
            }
            bool robot=link&&link.Connected&&link.robotCamera&&link.robotCamera.enabled;
            if(networkTone&&networkTone.isPlaying)
            {if(Time.unscaledTime>=networkEnds)networkTone.Stop();else networkTone.volume=networkVolume*Mathf.Clamp01((networkEnds-Time.unscaledTime)/networkFade);}
            if(tool){float target=Time.unscaledTime<toolUntil?.5f:0;tool.volume=Mathf.MoveTowards(tool.volume,target,Time.unscaledDeltaTime*5);if(target==0&&tool.volume<.001f)tool.Stop();}
            transmission=Mathf.MoveTowards(transmission,robot?1:0,Time.unscaledDeltaTime*5);
            float noise=link?link.Noise:0;
            for(int i=channels.Count-1;i>=0;i--)
            {
                var c=channels[i];if(!c.source){channels.RemoveAt(i);continue;}
                bool filter=transmission>.001f;c.low.enabled=c.high.enabled=c.distortion.enabled=filter;
                if(c.roomRadio)
                {
                    // Radio is beside the operator, not part of the remote feed.
                    c.distortion.enabled=false;
                    if(filter){c.low.cutoffFrequency=Mathf.Lerp(22000,1800,transmission);c.high.cutoffFrequency=80*transmission;}
                }
                else if(filter){c.low.cutoffFrequency=Mathf.Lerp(22000,Mathf.Lerp(4400,900,noise),transmission);c.high.cutoffFrequency=120*transmission;c.distortion.distortionLevel=transmission*Mathf.Lerp(.13f,.48f,noise);}
            }
            if(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame)Ui(Sound.Escape);
            if(weather)
            {
                var camera=weather.ViewCamera;float shelter=camera&&weather.IsSheltered(camera.transform.position)?1:0;
                Fade(inside,shelter*(Catalog?Catalog.Volume(Sound.BaseAmbient):.25f));Fade(outside,(1-shelter)*(Catalog?Catalog.Volume(Sound.MoonAmbient):.25f));
            }
            if(link&&link.state&&link.Drive&&Catalog)
            {
                float speed=Vector3.ProjectOnPlane(link.Drive.Body.linearVelocity,Vector3.up).magnitude;
                float amount=Mathf.Clamp01(speed/Mathf.Max(1,link.Drive.MaximumForwardSpeed));bool running=link.robot.gameObject.activeInHierarchy&&link.state.HasSignal;
                Fade(engine,running?(.09f+.2f*amount)*Catalog.Volume(Sound.Engine):0);if(engine)engine.pitch=.7f+amount*.75f;
                Fade(acceleration,running?amount*.24f*Catalog.Volume(Sound.Acceleration):0);if(acceleration)acceleration.pitch=.75f+amount*.7f;
                if(Time.unscaledTime>=surfaceAt){surfaceAt=Time.unscaledTime+.12f;SampleSurface();}
                float rolling=running&&!link.Drive.Airborne&&!link.Drive.IsOverturned?amount*.3f:0;
                Fade(ride,groundRide==Sound.MoonRide?rolling*Catalog.Volume(Sound.MoonRide):0);if(ride)ride.pitch=.8f+amount*.5f;
                Fade(concreteRide,groundRide==Sound.ConcreteRide?rolling*Catalog.Volume(Sound.ConcreteRide):0);if(concreteRide)concreteRide.pitch=.8f+amount*.5f;
                Fade(metalRide,groundRide==Sound.MetalRide?rolling*Catalog.Volume(Sound.MetalRide):0);if(metalRide)metalRide.pitch=.8f+amount*.5f;
                Fade(radiation,running&&link.state.Irradiated?.2f*Catalog.Volume(Sound.Radiation):0);
                bool low=link.state.LowCharge;
                if(robot&&low&&(!wasLow||Time.unscaledTime>batteryAt)){Ui(Sound.BatteryLow);batteryAt=Time.unscaledTime+18;}wasLow=low;
                int nextLoss=robot&&link.Phase==LinkPhase.Live
                    ? !link.Powered?2:link.Uplink==UplinkStatus.OutOfRange||link.Uplink==UplinkStatus.NoSatellite?1:0
                    : 0;
                int satellite=link.UplinkSatelliteIndex;
                if(nextLoss!=0&&(nextLoss!=lossKind||(nextLoss==1&&satellite!=lossSatellite)))
                    NetworkTone(Sound.LosingConnection,nextLoss==2?2f:.6f);
                if(nextLoss==0&&networkTone&&networkCue==Sound.LosingConnection)networkTone.Stop();
                lossKind=nextLoss;lossSatellite=satellite;
            }
        }
        void SampleSurface()
        {
            var origin=link.robot.position+Vector3.up*link.Drive.Length*.6f;
            int count=Physics.RaycastNonAlloc(origin,Vector3.down,surfaceHits,link.Drive.Length*1.5f+2,~0,QueryTriggerInteraction.Ignore);
            float nearest=float.MaxValue;groundRide=Sound.MoonRide;
            for(int i=0;i<count;i++)
            {
                var hit=surfaceHits[i];
                if(hit.distance>=nearest||hit.normal.y<.5f||hit.transform.IsChildOf(link.robot)||
                    hit.collider.GetComponentInParent<ZombieActor>()||hit.collider.GetComponentInParent<FirstPersonController>()||hit.collider.GetComponentInParent<RepairCarryable>())continue;
                nearest=hit.distance;groundRide=RoverSurfaceAudio.Kind(hit);
            }
        }
        static void Fade(AudioSource source,float target){if(source)source.volume=Mathf.MoveTowards(source.volume,target,Time.unscaledDeltaTime*.35f);}
    }
    sealed class MovementAudio : MonoBehaviour
    {
        public bool pcGame;
        CharacterController motor;FirstPersonController human;CrazyCosmonautsPlayer avatar;
        Vector3 before;float distance,next,airTime,fallSpeed;int step;
        void Start(){motor=GetComponent<CharacterController>();human=GetComponent<FirstPersonController>();avatar=GetComponent<CrazyCosmonautsPlayer>();before=transform.position;}
        void Update()
        {
            var delta=Vector3.ProjectOnPlane(transform.position-before,Vector3.up);before=transform.position;
            float scale=pcGame?1:Mathf.Max(.1f,PlayerBody.Scale);
            if(!motor||!motor.enabled||delta.magnitude>3*scale||GameSettingsMenu.BlockGameplayInput||DeveloperConsole.BlockGameplayInput||
                (human&&(!human.enabled||human.Noclip))||(avatar&&(!avatar.enabled||!avatar.game||!avatar.game.GameplayActive))){distance=airTime=fallSpeed=0;return;}
            if(!motor.isGrounded){airTime+=Time.deltaTime;fallSpeed=Mathf.Max(fallSpeed,-motor.velocity.y);distance=0;return;}
            if(airTime>.15f&&fallSpeed>2*scale)
            {
                if(pcGame)GameAudio.Ui(Sound.GameStep,.7f);else GameAudio.At(Sound.DirtBang,transform.position,Mathf.Clamp(fallSpeed/(10*scale),.2f,.7f));
                next=Time.time+.25f;
            }
            airTime=fallSpeed=0;
            distance+=delta.magnitude;if(distance<.85f*scale||Time.time<next)return;
            float speed=motor.velocity.magnitude/scale;next=Time.time+(speed>5?.34f:.5f);distance=0;
            if(pcGame)GameAudio.Ui(Sound.GameStep,.7f);
            else {GameAudio.At((Sound)((int)Sound.Step1+step),transform.position,.7f);step=(step+Random.Range(1,4))%4;}
        }
    }
    sealed class ZombieAudio : MonoBehaviour
    {
        ZombieActor actor;int hits;bool dead;float roarAt;
        void Start(){actor=GetComponent<ZombieActor>();roarAt=Time.time+Random.Range(5,13);}
        void Update()
        {
            if(!actor||GameSettingsMenu.BlockGameplayInput||Time.timeScale<=0)return;
            if(actor.IsDead){if(!dead)GameAudio.Zombie(actor,Sound.ZombieDeath);dead=true;return;}
            if(actor.ShotsReceived!=hits){hits=actor.ShotsReceived;GameAudio.Zombie(actor,Sound.ZombieRoar);roarAt=Time.time+8;}
            if(actor.enabled&&Time.time>roarAt){GameAudio.Zombie(actor,Sound.ZombieRoar);roarAt=Time.time+Random.Range(9,18);}
        }
    }
}
