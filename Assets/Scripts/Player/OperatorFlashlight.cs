using MoonThemedGame.Audio;
using UnityEngine;

namespace MoonThemedGame.Interactions
{
    [DisallowMultipleComponent,RequireComponent(typeof(FirstPersonController))]
    public sealed class OperatorFlashlight : MonoBehaviour
    {
        [Min(1)] public float range=18;
        [Min(0)] public float intensity=450;
        [Range(20,100)] public float coneAngle=58;
        public Color color=new Color(1,.96f,.86f);
        public bool IsOn {get;private set;}
        public Light Beam {get;private set;}
        FirstPersonController player;
        Camera view;

        void Awake(){player=GetComponent<FirstPersonController>();view=GetComponentInChildren<Camera>(true);}
        public void Toggle()
        {
            if(!CanUse || GameSettingsMenu.BlockGameplayInput)return;
            IsOn=!IsOn;RefreshBeam();GameAudio.Ui(Sound.Flashlight);
        }
        bool CanUse=>player&&player.isActiveAndEnabled&&view&&view.isActiveAndEnabled;
        void LateUpdate()
        {
            if(CanUse&&!GameSettingsMenu.BlockGameplayInput&&GameControls.Pressed(GameCommand.Flashlight))Toggle();
            RefreshBeam();
        }
        void RefreshBeam()
        {
            if(!Beam&&IsOn&&view)
            {
                var host=new GameObject("Operator / Flashlight");host.transform.SetParent(view.transform,false);
                Beam=host.AddComponent<Light>();Beam.type=LightType.Spot;Beam.shadows=LightShadows.Soft;
                Beam.shadowBias=.02f;Beam.shadowNormalBias=.3f;Beam.renderMode=LightRenderMode.ForcePixel;
            }
            if(!Beam)return;
            float scale=Mathf.Max(.1f,MoonThemedGame.Repair.PlayerBody.Scale);
            Beam.transform.SetPositionAndRotation(view.transform.position+view.transform.forward*(.12f*scale),view.transform.rotation);
            Beam.range=range*scale;Beam.intensity=intensity*scale*scale;Beam.color=color;
            Beam.spotAngle=coneAngle;Beam.innerSpotAngle=coneAngle*.55f;
            Beam.enabled=IsOn&&CanUse;
        }
        void OnDisable(){if(Beam)Beam.enabled=false;}
        void OnDestroy(){if(Beam)Destroy(Beam.gameObject);}
    }
}
