using UnityEngine;
using UnityEngine.InputSystem;

namespace MoonThemedGame.Interactions
{
    [DefaultExecutionOrder(100),DisallowMultipleComponent,RequireComponent(typeof(HeldProp))]
    public sealed class EdibleProp : MonoBehaviour
    {
        [Min(0.1f)] public float eatDuration=0.45f;
        [Min(0)] public float intoxicationDose;
        HeldProp prop;
        Vector3 originalScale;
        float elapsed;
        bool lookWasLocked;
        public bool IsEating { get; private set; }
        public bool IsConsumed { get; private set; }
        public bool CanEat=>isActiveAndEnabled && !IsEating && !IsConsumed && prop && prop.IsHeld &&
            !GameSettingsMenu.BlockGameplayInput && !MoonThemedGame.Repair.PlayerBody.BeltOpen &&
            prop.HoldingCamera && prop.HoldingCamera.isActiveAndEnabled;

        void Awake(){prop=GetComponent<HeldProp>();originalScale=transform.localScale;}
        public bool TryEat()
        {
            if(!CanEat)return false;
            elapsed=0;IsEating=true;SetHighlightOff();return true;
        }
        void SetHighlightOff(){if(prop)prop.SetHighlighted(false);}
        void Update()
        {
            bool locked=Cursor.lockState==CursorLockMode.Locked;
            if(lookWasLocked && locked && Mouse.current!=null && Mouse.current.leftButton.wasPressedThisFrame)TryEat();
            lookWasLocked=locked;
        }
        void LateUpdate()
        {
            if(!IsEating)return;
            if(!prop || !prop.IsHeld){Cancel();return;}
            if(GameSettingsMenu.BlockGameplayInput || MoonThemedGame.Repair.PlayerBody.BeltOpen)return;
            elapsed+=Time.deltaTime;
            float t=Mathf.SmoothStep(0,1,Mathf.Clamp01(elapsed/Mathf.Max(0.1f,eatDuration)));
            var camera=prop.HoldingCamera;
            var mouth=camera.transform.position+camera.transform.forward*0.12f-camera.transform.up*0.18f;
            transform.position=Vector3.Lerp(transform.position,mouth,t);
            transform.rotation=Quaternion.AngleAxis(t*35,camera.transform.right)*transform.rotation;
            transform.localScale=originalScale*Mathf.Lerp(1,0.3f,t);
            if(elapsed<eatDuration)return;
            IsEating=false;IsConsumed=true;
            OperatorIntoxication.Consume(prop.HoldingCamera,intoxicationDose);
            prop.Drop(false);
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
        void Cancel(){IsEating=false;elapsed=0;if(!IsConsumed)transform.localScale=originalScale;}
        void OnDisable(){Cancel();}
    }
}
