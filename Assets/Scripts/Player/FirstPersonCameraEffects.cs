using UnityEngine;

// Attach to CameraEffects, below CameraRoot. Does not change mouse look.
[DisallowMultipleComponent]
public sealed class FirstPersonCameraEffects : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private FirstPersonController player;

    [Header("Walking Bob")]
    [SerializeField] private bool enableBob = true;
    [SerializeField, Min(0f)] private float horizontalAmplitude = 0.005f;
    [SerializeField, Min(0f)] private float verticalAmplitude = 0.008f;
    [Tooltip("Distance between consecutive footsteps in metres.")]
    [SerializeField, Min(0.1f)] private float stepLength = 0.9f;

    [Header("Strafe Tilt")]
    [SerializeField] private bool enableTilt = true;
    [SerializeField, Range(0f, 3f)] private float maxTilt = 0.7f;
    [Header("Weapon lean")]
    [SerializeField,Range(0,25)] private float leanAngle=14;
    [SerializeField,Min(0)] private float leanDistance=.19f;
    [SerializeField,Min(.05f)] private float leanResponse=.18f;
    public float WeaponLean=>lean;

    [Header("Response")]
    [SerializeField, Min(0.1f)] private float referenceSpeed = 3.5f;
    [SerializeField, Min(0f)] private float movementThreshold = 0.05f;
    [SerializeField, Min(0.1f)] private float positionResponse = 14f;
    [SerializeField, Min(0.1f)] private float tiltResponse = 10f;
    [Tooltip("Ignore movement faster than this (for example, teleporting).")]
    [SerializeField, Min(1f)] private float teleportSpeedThreshold = 20f;

    private CharacterController controller;
    private Vector3 restPosition;
    private Quaternion restRotation;
    private Vector3 previousPlayerPosition;
    private Vector3 bobOffset;
    private float phase;
    private float tilt;
    private float lean,leanVelocity,weaponRoll,rollVelocity,weaponPush,pushVelocity;

    private void Reset()
    {
        player = GetComponentInParent<FirstPersonController>();
    }

    private void Awake()
    {
        restPosition = transform.localPosition;
        restRotation = transform.localRotation;

        if (player == null)
            player = GetComponentInParent<FirstPersonController>();

        if (player == null || player.transform == transform ||
            !transform.IsChildOf(player.transform))
        {
            Debug.LogError("Camera effects must be on a child of the player.", this);
            enabled = false;
            return;
        }

        controller = player.GetComponent<CharacterController>();
    }

    private void OnEnable()
    {
        if (player != null)
            previousPlayerPosition = player.transform.position;
    }

    private void LateUpdate()
    {
        if (player == null || controller == null)
            return;

        // Sample position after the player's Update, including collision response.
        Vector3 displacement = player.transform.position - previousPlayerPosition;
        previousPlayerPosition = player.transform.position;
        displacement.y = 0f;

        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        float speed = displacement.magnitude / dt;
        bool walking = player.isActiveAndEnabled && controller.enabled &&
            controller.isGrounded && Cursor.lockState == CursorLockMode.Locked &&
            speed > movementThreshold && speed < teleportSpeedThreshold;

        Vector3 targetOffset = Vector3.zero;
        float targetTilt = 0f;

        if (walking && MoonThemedGame.Interactions.ComfortSettings.CameraShake)
        {
            float intensity = Mathf.Clamp01(speed / Mathf.Max(0.1f, referenceSpeed));

            if (enableBob)
            {
                // One horizontal cycle spans two footsteps.
                phase = Mathf.Repeat(phase + displacement.magnitude * Mathf.PI /
                    Mathf.Max(0.1f, stepLength), Mathf.PI * 2f);

                targetOffset = new Vector3(
                    Mathf.Sin(phase) * horizontalAmplitude,
                    Mathf.Sin(phase * 2f) * verticalAmplitude,
                    0f) * intensity;
            }

            if (enableTilt)
            {
                Vector3 localVelocity = player.transform.InverseTransformDirection(
                    displacement / dt);
                targetTilt = -Mathf.Clamp(localVelocity.x /
                    Mathf.Max(0.1f, referenceSpeed), -1f, 1f) * maxTilt;
            }
        }

        // Exponential response keeps smoothing consistent across frame rates.
        float positionBlend = 1f - Mathf.Exp(-positionResponse * dt);
        float tiltBlend = 1f - Mathf.Exp(-tiltResponse * dt);
        bobOffset = Vector3.Lerp(bobOffset, targetOffset, positionBlend);
        tilt = Mathf.Lerp(tilt, targetTilt, tiltBlend);
        if(!MoonThemedGame.Interactions.ComfortSettings.CameraShake){bobOffset=Vector3.zero;tilt=0;weaponRoll=weaponPush=rollVelocity=pushVelocity=0;}

        transform.localPosition = restPosition + bobOffset;
        transform.localRotation = restRotation * Quaternion.Euler(0f, 0f, tilt);
        bool active=player.isActiveAndEnabled && MoonThemedGame.Repair.PlayerPistol.LeanControlsActive &&
            !MoonThemedGame.Repair.PlayerPistol.InteractionModifierHeld;
        float desired=active?(MoonThemedGame.Interactions.GameControls.Held(MoonThemedGame.Interactions.GameCommand.LeanRight)?1:0)-
            (MoonThemedGame.Interactions.GameControls.Held(MoonThemedGame.Interactions.GameCommand.LeanLeft)?1:0):0;
        lean=Mathf.SmoothDamp(lean,desired,ref leanVelocity,leanResponse,Mathf.Infinity,dt);
        weaponRoll=Mathf.SmoothDamp(weaponRoll,0,ref rollVelocity,.13f,Mathf.Infinity,dt);
        weaponPush=Mathf.SmoothDamp(weaponPush,0,ref pushVelocity,.12f,Mathf.Infinity,dt);
        float unit=MoonThemedGame.Repair.PlayerBody.Scale;
        var shift=player.transform.right*(lean*leanDistance*unit)-transform.forward*weaponPush;
        float distance=shift.magnitude,allowed=distance;
        if(distance>.0001f)
            foreach(var hit in Physics.SphereCastAll(transform.position,.10f*unit,shift/distance,distance,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                if(!hit.transform.IsChildOf(player.transform))allowed=Mathf.Min(allowed,Mathf.Max(0,hit.distance-.02f*unit));
        float clearance=distance>.0001f?allowed/distance:1;
        transform.position+=shift*clearance;
        transform.localRotation=restRotation*Quaternion.Euler(0,0,tilt-lean*leanAngle*clearance+weaponRoll);
    }

    public void AddWeaponImpulse(float roll,float push)
    {if(!MoonThemedGame.Interactions.ComfortSettings.CameraShake)return;weaponRoll=Mathf.Clamp(weaponRoll+roll,-3,3);weaponPush=Mathf.Min(weaponPush+push,.04f*MoonThemedGame.Repair.PlayerBody.Scale);rollVelocity=pushVelocity=0;}
    public void ResetWeaponMotion()
    {
        lean=leanVelocity=weaponRoll=rollVelocity=weaponPush=pushVelocity=0;
        transform.localPosition=restPosition+bobOffset;
        transform.localRotation=restRotation*Quaternion.Euler(0,0,tilt);
    }

    private void OnDisable()
    {
        transform.localPosition = restPosition;
        transform.localRotation = restRotation;
        bobOffset = Vector3.zero;
        tilt = 0f;
        phase = 0f;
        lean=leanVelocity=weaponRoll=rollVelocity=weaponPush=pushVelocity=0;
    }
}
