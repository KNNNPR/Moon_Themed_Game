using System;
using UnityEngine;

namespace MoonThemedGame.Interactions
{
    public static class ComfortSettings
    {
        const string Prefix="comfort.";
        static bool loaded,shake,toggleCrouch;
        static float human,robot,tools,fov;
        public static event Action Changed;
        static void Load()
        {
            if(loaded)return;loaded=true;
            human=Read("human",1,.2f,3);robot=Read("robot",1,.2f,3);tools=Read("tools",1,.2f,3);fov=Read("fov",1,0,1.5f);
            shake=PlayerPrefs.GetInt(Prefix+"shake",1)!=0;toggleCrouch=PlayerPrefs.GetInt(Prefix+"toggle-crouch",0)!=0;
        }
        static float Read(string key,float fallback,float min,float max){float value=PlayerPrefs.GetFloat(Prefix+key,fallback);return float.IsNaN(value)||float.IsInfinity(value)?fallback:Mathf.Clamp(value,min,max);}
        static void Save(){PlayerPrefs.Save();Changed?.Invoke();}
        static void Set(ref float field,string key,float value,float min,float max)
        {if(float.IsNaN(value)||float.IsInfinity(value))return;value=Mathf.Clamp(value,min,max);if(Mathf.Approximately(field,value))return;field=value;PlayerPrefs.SetFloat(Prefix+key,value);Save();}
        public static float HumanSensitivity {get{Load();return human;}set{Load();Set(ref human,"human",value,.2f,3);}}
        public static float RobotSensitivity {get{Load();return robot;}set{Load();Set(ref robot,"robot",value,.2f,3);}}
        public static float ToolSensitivity {get{Load();return tools;}set{Load();Set(ref tools,"tools",value,.2f,3);}}
        public static float DynamicFov {get{Load();return fov;}set{Load();Set(ref fov,"fov",value,0,1.5f);}}
        public static bool CameraShake {get{Load();return shake;}set{Load();if(shake==value)return;shake=value;PlayerPrefs.SetInt(Prefix+"shake",value?1:0);Save();}}
        public static bool ToggleCrouch {get{Load();return toggleCrouch;}set{Load();if(toggleCrouch==value)return;toggleCrouch=value;PlayerPrefs.SetInt(Prefix+"toggle-crouch",value?1:0);Save();}}
        public static void RestoreDefaults(){HumanSensitivity=RobotSensitivity=ToolSensitivity=DynamicFov=1;CameraShake=true;ToggleCrouch=false;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset(){loaded=false;Changed=null;}
    }
}
