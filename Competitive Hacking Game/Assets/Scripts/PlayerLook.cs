using Unity.Netcode;
using UnityEngine;

public class PlayerLook : NetworkBehaviour
{
    private PlayerMotor motor;

    [Header("References")]
    public Camera cam;
    [Tooltip("Parent pivot of the main camera (your empty 'Camera' object in the middle of the player).")]
    public Transform cameraRoot;

    // Camera local positions by state
    private Vector3 standingPosition;
    private Vector3 crouchingPosition;
    private Vector3 slidingPosition;
    private Vector3 sprintingPosition;

    [Header("Sensitivity")]
    public float xSensitivity = 30f;
    public float ySensitivity = 30f;

    // Pitch
    private float xRotation = 0f;

    [Header("FOV")]
    [SerializeField] private float defaultFOV = 90f;
    [SerializeField] private float sprintFOV = 100f;
    [SerializeField] private float fovTransitionSpeed = 5f;

    [Header("Shoulder Look (Yaw Decoupling)")]
    public float maxShoulderYaw = 90f;
    public float catchUpThreshold = 90f;
    public float bodyCatchUpSpeed = 720f;
    public bool  shoulderWhileCrouching = true;

    [Header("Catch-up on Move")]
    public bool  instantCatchUpOnMove = true;
    public float catchUpOnMoveSpeed   = 1080f;

    // Internal yaw state (camera pivot offset)
    private float yawOffset = 0f;              // local yaw on cameraRoot (degrees)
    private bool  catchingUpStationary = false;
    private bool  wasMoving = false;

    public float Pitch => xRotation;
    public float YawOffset => yawOffset;

    private bool phoneAimActive = false;
    public  bool IsPhoneAiming => phoneAimActive;

    public Quaternion WorldLookRotation =>
        transform.rotation * Quaternion.Euler(0f, yawOffset, 0f) * Quaternion.Euler(Pitch, 0f, 0f);

    public Vector3 WorldLookDirection => WorldLookRotation * Vector3.forward;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;

        if (!IsOwner)
        {
            if (cam != null) cam.gameObject.SetActive(false);
            return;
        }

        motor = GetComponent<PlayerMotor>();

        standingPosition  = cam.transform.localPosition;
        crouchingPosition = new Vector3(standingPosition.x, standingPosition.y - 1.1f, standingPosition.z);
        slidingPosition   = new Vector3(standingPosition.x, standingPosition.y - 1.1f, standingPosition.z - 0.1f);
        sprintingPosition = new Vector3(standingPosition.x, standingPosition.y - 0.15f, standingPosition.z + 0.15f);

        cam.fieldOfView = defaultFOV;

        if (cameraRoot == null && cam != null)
            cameraRoot = cam.transform.parent; // auto-fill
    }

    void Update()
    {
        if (!IsOwner) return;

        float dt = Time.deltaTime;

        if (motor.sliding && xRotation > 0f)
            xRotation = Mathf.Lerp(xRotation, 0f, dt * 3.5f);

        Vector3 targetPos = motor.sprinting ? sprintingPosition :
                            motor.sliding   ? slidingPosition   :
                            motor.crouching ? crouchingPosition : standingPosition;

        cam.transform.localPosition = Vector3.Lerp(cam.transform.localPosition, targetPos, dt * 7f);

        bool fastState = (motor.sprinting && motor.inputDirection.y > 0) || motor.sliding;
        float targetFOV = fastState ? sprintFOV : defaultFOV;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, dt * fovTransitionSpeed);

        if (cameraRoot != null)
            cameraRoot.localRotation = Quaternion.Euler(0f, yawOffset, 0f);
    }

    public void ProcessLook(Vector2 input)
    {
        if (!IsOwner) return;

        float dt = Time.deltaTime;
        float mouseX = input.x;
        float mouseY = input.y;

        // ---------- PITCH ----------
        if (!(motor.sliding && xRotation > 0f && mouseY <= 0f))
        {
            xRotation -= mouseY * dt * ySensitivity;
            xRotation  = Mathf.Clamp(xRotation, -90f, 90f);
        }
        cam.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // ---------- YAW ----------
        bool hasMoveInput = motor.inputDirection.sqrMagnitude > 0.0001f;
        bool shoulderMode =
            !hasMoveInput && !motor.sliding && !motor.sprinting &&
            (motor.crouching ? shoulderWhileCrouching : true);

        float yawDelta = mouseX * dt * xSensitivity;

        if (phoneAimActive)
        {
            transform.rotation *= Quaternion.Euler(0f, yawDelta, 0f);

            if (Mathf.Abs(yawOffset) > 0.01f)
            {
                if (instantCatchUpOnMove)
                {
                    transform.rotation *= Quaternion.Euler(0f, yawOffset, 0f);
                    yawOffset = 0f;
                }
                else
                {
                    CatchUp(catchUpOnMoveSpeed * dt);
                }
            }
            catchingUpStationary = false;
        }
        else if (shoulderMode)
        {
            yawOffset = Mathf.Clamp(yawOffset + yawDelta, -maxShoulderYaw, maxShoulderYaw);

            if (!catchingUpStationary && Mathf.Abs(yawOffset) >= (catchUpThreshold - 0.01f))
                catchingUpStationary = true;

            if (catchingUpStationary)
            {
                CatchUp(bodyCatchUpSpeed * dt);
                if (Mathf.Abs(yawOffset) <= 0.01f) catchingUpStationary = false;
            }
        }
        else
        {
            if (hasMoveInput && !wasMoving && Mathf.Abs(yawOffset) > 0.01f)
            {
                if (instantCatchUpOnMove)
                {
                    transform.rotation *= Quaternion.Euler(0f, yawOffset, 0f);
                    yawOffset = 0f;
                }
                else
                {
                    CatchUp(catchUpOnMoveSpeed * dt);
                }
            }

            transform.rotation *= Quaternion.Euler(0f, yawDelta, 0f);

            if (!instantCatchUpOnMove && hasMoveInput && Mathf.Abs(yawOffset) > 0.01f)
                CatchUp(catchUpOnMoveSpeed * dt);

            catchingUpStationary = false;
        }

        if (cameraRoot != null)
            cameraRoot.localRotation = Quaternion.Euler(0f, yawOffset, 0f);

        wasMoving = hasMoveInput;
    }

    private void CatchUp(float stepDegrees)
    {
        if (Mathf.Abs(yawOffset) <= 0.0001f) return;

        float delta = Mathf.Clamp(yawOffset, -stepDegrees, stepDegrees);
        transform.rotation *= Quaternion.Euler(0f, delta, 0f);
        yawOffset -= delta;

        if (Mathf.Abs(yawOffset) <= 0.01f)
            yawOffset = 0f;
    }

    public Quaternion MoveSpaceRotation => transform.rotation * Quaternion.Euler(0f, yawOffset, 0f);

    public void SetPhoneAim(bool aiming) => phoneAimActive = aiming;
}
