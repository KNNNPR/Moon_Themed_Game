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

        void OnEnable()
        {
            origin=transform.localPosition;
            rotation=transform.localRotation;
            started=Time.unscaledTime;
        }
        void LateUpdate()
        {
            float t=Time.unscaledTime-started;
            transform.localPosition=origin+new Vector3(Mathf.Sin(t*.47f+phase),Mathf.Sin(t*.63f+phase*2),Mathf.Cos(t*.39f+phase)) * amplitude;
            transform.localRotation=rotation*Quaternion.Euler(spin*t+new Vector3(Mathf.Sin(t*.38f+phase),Mathf.Cos(t*.31f+phase),Mathf.Sin(t*.43f+phase))*rotationAmplitude);
        }
    }
}
