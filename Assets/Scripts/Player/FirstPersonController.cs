using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform cameraRoot;
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference lookAction;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float acceleration = 35f;
    [SerializeField] private float deceleration = 30f;
    [SerializeField] private float gravity = -20f;

    [Header("Mouse Look")]
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float pitchLimit = 85f;

    private CharacterController controller;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private float pitch;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    private void OnEnable()
    {
        moveAction.action.Enable();
        lookAction.action.Enable();
        SetCursorLocked(true);
    }

    private void OnDisable()
    {
        moveAction.action.Disable();
        lookAction.action.Disable();
        SetCursorLocked(false);
    }

    private void Update()
    {
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
        UpdateMovement();
    }

    private void UpdateLook()
    {
        Vector2 look = lookAction.action.ReadValue<Vector2>();

        transform.Rotate(Vector3.up, look.x * mouseSensitivity);

        pitch -= look.y * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, -pitchLimit, pitchLimit);

        cameraRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void UpdateMovement()
    {
        Vector2 input = Vector2.ClampMagnitude(
            moveAction.action.ReadValue<Vector2>(), 1f);

        Vector3 targetVelocity =
            (transform.right * input.x + transform.forward * input.y)
            * moveSpeed;

        float rate = input.sqrMagnitude > 0.001f
            ? acceleration
            : deceleration;

        horizontalVelocity = Vector3.MoveTowards(
            horizontalVelocity,
            targetVelocity,
            rate * Time.deltaTime);

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity =
            horizontalVelocity + Vector3.up * verticalVelocity;

        CollisionFlags collisions =
            controller.Move(velocity * Time.deltaTime);

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