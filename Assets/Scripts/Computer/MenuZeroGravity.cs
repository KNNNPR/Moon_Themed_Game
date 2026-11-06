using UnityEngine;

namespace MoonThemedGame.Computer
{
    [DefaultExecutionOrder(-10000)]
    public sealed class MenuZeroGravity : MonoBehaviour
    {
        public float amplitude=.035f;
        public float rotationAmplitude=1;
        public Vector3 spin;
        public float phase;
        Vector3 origin;
        Quaternion rotation;
        float started;
        Vector3 velocity,restScale,pendingPosition,pendingVelocity;
        Quaternion pendingRotation;
        bool replacing;
        float replacementStarted;

        void OnEnable()
        {
            origin=transform.localPosition;
            rotation=transform.localRotation;
            restScale=transform.localScale;
            started=Time.unscaledTime;
        }
        public void Toss(Vector3 position,Quaternion orientation,Vector3 worldVelocity,bool replace)
        {
            pendingPosition=position;pendingRotation=orientation;pendingVelocity=worldVelocity;
            if(replace){replacing=true;replacementStarted=Time.unscaledTime;}
            else BeginToss();
        }
        void BeginToss()
        {
            var parent=transform.parent;
            origin=parent?parent.InverseTransformPoint(pendingPosition):pendingPosition;
            rotation=parent?Quaternion.Inverse(parent.rotation)*pendingRotation:pendingRotation;
            velocity=parent?parent.InverseTransformVector(pendingVelocity):pendingVelocity;
            started=Time.unscaledTime;replacing=false;
            transform.localPosition=origin;transform.localRotation=rotation;transform.localScale=restScale;
        }
        void LateUpdate()
        {
            if(replacing)
            {
                float progress=(Time.unscaledTime-replacementStarted)/.16f;
                transform.localScale=restScale*(1-Mathf.SmoothStep(0,1,progress));
                if(progress<1)return;
                BeginToss();
            }
            float t=Time.unscaledTime-started;
            float dt=Mathf.Min(Time.unscaledDeltaTime,.05f);
            float damping=Mathf.Exp(-1.05f*dt);
            origin+=velocity*((1-damping)/1.05f);velocity*=damping;
            transform.localPosition=origin+new Vector3(Mathf.Sin(t*.47f+phase),Mathf.Sin(t*.63f+phase*2),Mathf.Cos(t*.39f+phase)) * amplitude;
            transform.localRotation=rotation*Quaternion.Euler(spin*t+new Vector3(Mathf.Sin(t*.38f+phase),Mathf.Cos(t*.31f+phase),Mathf.Sin(t*.43f+phase))*rotationAmplitude);
        }
    }
}
