using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    public float DebugSpeedMultiplier {get;set;}=1;
    [Header("References")]
    [SerializeField] private Transform cameraRoot;
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference lookAction;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField,Min(0f)] private float sprintSpeed = 6f;
    [SerializeField] private float acceleration = 35f;
    [SerializeField] private float deceleration = 30f;
    [SerializeField] private float gravity = -20f;

    [Header("Jump")]
    [SerializeField,Min(0.05f)] private float jumpHeight=1.1f;

    [Header("Crouch")]
    [SerializeField,Range(0.35f,0.9f)] private float crouchHeightRatio=0.55f;
    [SerializeField,Range(0.1f,1f)] private float crouchSpeedMultiplier=0.45f;
    [SerializeField,Min(0.01f)] private float crouchDuration=0.25f;
    [SerializeField,Min(0.01f)] private float repeatedCrouchDuration=0.65f;
    [SerializeField,Min(0.1f)] private float crouchRecoveryDelay=0.8f;
    [SerializeField,Min(0.1f)] private float crouchRecoveryDuration=1.25f;

    [Header("Mouse Look")]
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float pitchLimit = 85f;

    private CharacterController controller;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private float pitch;
    private float standingHeight;
    private float crouchFatigue;
    private float timeSinceCrouch=float.PositiveInfinity;
    private bool wasCrouchRequested;
    private Vector3 standingCenter,standingCameraPosition;
    private readonly Collider[] stanceOverlaps=new Collider[64];

    // Set while the tool belt is open: the mouse picks a tool instead of turning the head.
    public static bool LookFrozen;
    public Transform CameraRoot => cameraRoot;
    public float Pitch => pitch;
    // Feed recoil into the same view angles as mouse input, so the player can compensate.
    public void AddViewKick(float up, float yaw)
    {
        if (!enabled || !cameraRoot) return;
        pitch=Mathf.Clamp(pitch-up,-pitchLimit,pitchLimit);
        transform.Rotate(Vector3.up,yaw);
        cameraRoot.localRotation=Quaternion.Euler(pitch,0,0);
    }
    public float CrouchAmount { get; private set; }
    public bool IsCrouching=>CrouchAmount>0.01f;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        standingHeight=controller.height;
        standingCenter=controller.center;
        if(cameraRoot)standingCameraPosition=cameraRoot.localPosition;
    }

    private void OnEnable()
    {
        moveAction.action.Enable();
        lookAction.action.Enable();
        SetCursorLocked(true);
    }

    private void OnDisable()
    {
        GetComponentInChildren<FirstPersonCameraEffects>()?.ResetWeaponMotion();
        moveAction.action.Disable();
        lookAction.action.Disable();
        SetCursorLocked(false);
    }

    private void Update()
    {
        if (MoonThemedGame.Interactions.GameSettingsMenu.BlockGameplayInput) return;
        if (Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            SetCursorLocked(false);
        }

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            horizontalVelocity = Vector3.zero;

            if (Mouse.current != null &&
                Mouse.current.leftButton.wasPressedThisFrame)
            {
                SetCursorLocked(true);
            }

            return;
        }

        UpdateLook();
        UpdateCrouch(MoonThemedGame.Interactions.GameControls.Held(MoonThemedGame.Interactions.GameCommand.Crouch),Time.deltaTime);
        UpdateMovement();
    }

    private void UpdateCrouch(bool requested,float dt)
    {
        if(dt<=0)return;
        timeSinceCrouch+=dt;
        // Holding Ctrl is free; rapidly starting another crouch slows both stance transitions.
        if(requested && !wasCrouchRequested)
        {
            if(timeSinceCrouch<=crouchRecoveryDelay)crouchFatigue=Mathf.Clamp01(crouchFatigue+0.3f);
            timeSinceCrouch=0;
        }
        wasCrouchRequested=requested;
        if(timeSinceCrouch>crouchRecoveryDelay)
            crouchFatigue=Mathf.MoveTowards(crouchFatigue,0,dt/Mathf.Max(0.1f,crouchRecoveryDuration));
        float duration=Mathf.Lerp(crouchDuration,Mathf.Max(crouchDuration,repeatedCrouchDuration),crouchFatigue);
        bool blocked=CrouchAmount>0 && !HasStandingRoom();
        float previous=CrouchAmount;
        CrouchAmount=Mathf.MoveTowards(CrouchAmount,requested || blocked?1:0,dt/Mathf.Max(0.01f,duration));
        if(Mathf.Approximately(previous,CrouchAmount))return;
        float crouchedHeight=Mathf.Max(controller.radius*2,standingHeight*crouchHeightRatio);
        float height=Mathf.Lerp(standingHeight,crouchedHeight,Mathf.SmoothStep(0,1,CrouchAmount));
        // Preserve the capsule's bottom; shrinking must never lift the feet or sink them into the floor.
        controller.height=height;
        controller.center=standingCenter+Vector3.up*((height-standingHeight)*0.5f);
        if(cameraRoot)
        {
            var drop=transform.TransformVector(Vector3.up*(standingHeight-height));
            cameraRoot.localPosition=standingCameraPosition-(cameraRoot.parent?cameraRoot.parent.InverseTransformVector(drop):drop);
        }
    }

    private bool HasStandingRoom()
    {
        var scale=transform.lossyScale;
        float radius=controller.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z));
        float height=Mathf.Max(standingHeight*Mathf.Abs(scale.y),radius*2);
        float margin=Mathf.Max(0.001f,radius*0.05f);
        var center=transform.TransformPoint(standingCenter);
        var half=transform.up*(height*0.5f-radius);
        int count=Physics.OverlapCapsuleNonAlloc(center-half,center+half+transform.up*(margin+0.005f),
            Mathf.Max(0.001f,radius-margin),stanceOverlaps,~0,QueryTriggerInteraction.Ignore);
        if(count==stanceOverlaps.Length)return false;
        for(int i=0;i<count;i++)
        {
            var other=stanceOverlaps[i];
            if(other==controller || other.transform.IsChildOf(transform) ||
                Physics.GetIgnoreLayerCollision(gameObject.layer,other.gameObject.layer) || Physics.GetIgnoreCollision(controller,other))continue;
            return false;
        }
        return true;
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        var body = hit.rigidbody;
        if (body && !body.isKinematic && body.GetComponent<MoonThemedGame.Interactions.HeldProp>() && hit.moveDirection.y > -0.3f)
            body.AddForce(new Vector3(hit.moveDirection.x, 0, hit.moveDirection.z) * 5, ForceMode.Force);
    }

    private void UpdateLook()
    {
        if (LookFrozen || MoonThemedGame.Repair.RepairWrench.ConsumesLook) return;
        Vector2 look = lookAction.action.ReadValue<Vector2>();

        transform.Rotate(Vector3.up, look.x * mouseSensitivity);

        pitch -= look.y * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, -pitchLimit, pitchLimit);

        cameraRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    public void SetViewPose(Vector3 position,float yaw,float pitchDegrees=0)
    {
        bool wasEnabled=controller && controller.enabled;
        if(controller)controller.enabled=false;
        transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));
        pitch=Mathf.Clamp(pitchDegrees,-pitchLimit,pitchLimit);
        if(cameraRoot)cameraRoot.localRotation=Quaternion.Euler(pitch,0,0);
        horizontalVelocity=Vector3.zero;verticalVelocity=0;
        if(controller)controller.enabled=wasEnabled;
    }

    private void UpdateMovement()
    {
        Vector2 input = MoonThemedGame.Interactions.GameControls.Movement;
        if (moveAction.action.activeControl != null && !(moveAction.action.activeControl.device is Keyboard))
            input += moveAction.action.ReadValue<Vector2>();
        input = Vector2.ClampMagnitude(input, 1f);

        Move(input,MoonThemedGame.Interactions.GameControls.Pressed(MoonThemedGame.Interactions.GameCommand.Jump),Time.deltaTime);
    }

    private void Move(Vector2 input,bool jumpRequested,float dt)
    {
        if(!controller.enabled || dt<=0)return;

        bool sprinting=!IsCrouching && MoonThemedGame.Interactions.GameControls.Held(MoonThemedGame.Interactions.GameCommand.Sprint);
        Vector3 targetVelocity =
            (transform.right * input.x + transform.forward * input.y)
            * (sprinting?sprintSpeed:moveSpeed) * Mathf.Lerp(1,crouchSpeedMultiplier,CrouchAmount) * DebugSpeedMultiplier;

        float rate = input.sqrMagnitude > 0.001f
            ? acceleration
            : deceleration;

        horizontalVelocity = Vector3.MoveTowards(
            horizontalVelocity,
            targetVelocity,
            rate * dt * DebugSpeedMultiplier);

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        if(controller.isGrounded && verticalVelocity<=0 && jumpRequested)
            verticalVelocity=Mathf.Sqrt(2*Mathf.Max(0.01f,-gravity)*jumpHeight);

        verticalVelocity += gravity * dt;

        Vector3 velocity =
            horizontalVelocity + Vector3.up * verticalVelocity;

        CollisionFlags collisions =
            controller.Move(velocity * dt);

        if ((collisions & CollisionFlags.Above) != 0 &&
            verticalVelocity > 0f)
        {
            verticalVelocity = 0f;
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            horizontalVelocity = Vector3.zero;
            SetCursorLocked(false);
        }
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked
            ? CursorLockMode.Locked
            : CursorLockMode.None;

        Cursor.visible = !locked;
    }
}
