using UnityEngine;

namespace MoonThemedGame.Interactions
{
    // Operator-only state. The image effect never writes to look angles or camera transforms.
    [DisallowMultipleComponent,RequireComponent(typeof(FirstPersonController))]
    public sealed class OperatorIntoxication : MonoBehaviour
    {
        [Min(1)] public float maximumDose=5;
        [Min(0)] public float recoveryDelay=8;
        [Min(1)] public float secondsPerDose=24;
        [Min(0.1f)] public float onsetSeconds=1;
        [Min(0.1f)] public float recoverySeconds=1.5f;
        public float Dose { get; private set; }
        public float Strength { get; private set; }
        public float Phase { get; private set; }
        public Camera ViewCamera { get; private set; }
        float hold;

        public static void Consume(Camera view,float dose)
        {
            if(!view || dose<=0)return;
            var player=view.GetComponentInParent<FirstPersonController>();
            if(!player)return;
            var state=player.GetComponent<OperatorIntoxication>();
            if(!state)state=player.gameObject.AddComponent<OperatorIntoxication>();
            state.ViewCamera=view;state.AddDose(dose);
        }
        public void AddDose(float value)
        {
            if(value<=0)return;
            if(!ViewCamera)ViewCamera=GetComponentInChildren<Camera>();
            Dose=Mathf.Min(Mathf.Max(1,maximumDose),Dose+value);
            hold=recoveryDelay;
        }
        void Update()=>Advance(Time.deltaTime);
        public void Advance(float seconds)
        {
            if(seconds<=0)return;
            Phase+=seconds;
            float recovery=Mathf.Max(0,seconds-hold);hold=Mathf.Max(0,hold-seconds);
            Dose=Mathf.Max(0,Dose-recovery/Mathf.Max(1,secondsPerDose));
            float target=Mathf.Sqrt(Mathf.Clamp01(Dose/Mathf.Max(1,maximumDose)));
            Strength=Mathf.MoveTowards(Strength,target,seconds/Mathf.Max(0.1f,target>Strength?onsetSeconds:recoverySeconds));
            if(Dose==0 && Strength<0.0001f)Strength=0;
        }
        public bool Affects(Camera camera)=>isActiveAndEnabled && camera && camera==ViewCamera && Strength>0.0001f;
    }
}
