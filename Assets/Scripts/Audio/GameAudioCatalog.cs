using System;
using UnityEngine;

namespace MoonThemedGame.Audio
{
    public enum Sound
    {
        MenuMusic,CreditsMusic,Radio11,Radio2,Radio3,BaseAmbient,AppleDestroyed,AppleEating,DoorSlide,
        GunDrop,GunPickup,GunReload,Gunshot,Hammer,HammerDrop,Button,Keyboard,LampDestroyed,Confirm,Icon,
        RobotCheck,Screwdriver,ScrewdriverDrop,Step1,Step2,Step3,Step4,SuspiciousApple,ToolPickup,ToolboxOpen,
        ScreenOn,Ventilation,Wrench,WrenchDrop,Crane,Pipe,Error,Escape,BigHover,PartDrop,PartPickup,
        GameDamage,GameReload,GameShot,GameStep,ZombieDeath,ZombieRoar,AppleThrow,MenuHover,
        Acceleration,BatteryLow,ConnectionLost,Engine,HackerLaugh,ItemGet,LosingConnection,Meteor,
        MoonAmbient,MoonRide,Radiation,Slowdown,RadioVoice,Bridge,DirtBang,GateOpen,MetalBang,ZombieBang,FenceBang,ConcreteRide,MetalRide,Dispenser,Flashlight
    }
    [CreateAssetMenu(menuName="Moon Game/Audio Catalogue")]
    public sealed class GameAudioCatalog : ScriptableObject
    {
        [Serializable] public sealed class Entry {public Sound sound;public AudioClip clip;[Range(0,1)]public float volume=.65f;}
        public Entry[] entries=Array.Empty<Entry>();
        [Header("Radio only (independent of menu/credits music)")]
        [Range(0,1)] public float radioVolume=.3f;
        [Tooltip("Loudness matching: radio11, radio2, radio3, menu theme 2, HappySong.")]
        public float[] radioTrackGains={1,1,1,1,1};
        public AudioClip Clip(Sound sound)=>Array.Find(entries,e=>e.sound==sound)?.clip;
        public float Volume(Sound sound)=>Array.Find(entries,e=>e.sound==sound)?.volume??.65f;
        public AudioClip[] RadioTracks=>new[]{Clip(Sound.Radio11),Clip(Sound.Radio2),Clip(Sound.Radio3),Clip(Sound.MenuMusic),Clip(Sound.CreditsMusic)};
    }
}
