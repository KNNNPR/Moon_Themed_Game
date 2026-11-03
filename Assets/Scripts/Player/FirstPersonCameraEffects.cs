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

        if (walking)
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

        transform.localPosition = restPosition + bobOffset;
        transform.localRotation = restRotation * Quaternion.Euler(0f, 0f, tilt);
    }

    private void OnDisable()
    {
        transform.localPosition = restPosition;
        transform.localRotation = restRotation;
        bobOffset = Vector3.zero;
        tilt = 0f;
        phase = 0f;
    }
}
