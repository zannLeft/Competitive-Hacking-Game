using System;
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
    [Tooltip("Max degrees camera can twist left/right while stationary/crouched.")]
    public float maxShoulderYaw = 90f;
    [Tooltip("When the offset reaches this, the body starts turning to align.")]
    public float catchUpThreshold = 90f;
    [Tooltip("How fast the body turns when catching up while stationary (deg/sec).")]
    public float bodyCatchUpSpeed = 720f;

    [Tooltip("Allow shoulder look when crouching (not sliding/sprinting).")]
    public bool shoulderWhileCrouching = true;

    [Header("Catch-up on Move")]
    [Tooltip("If true, as soon as movement input starts, the body instantly matches the camera.")]
    public bool instantCatchUpOnMove = true;
    [Tooltip("If instantCatchUpOnMove is false, use this smooth speed (deg/sec).")]
    public float catchUpOnMoveSpeed = 1080f;

    // Internal yaw state (camera pivot offset)
    private float yawOffset = 0f;              // local yaw on cameraRoot (degrees)
    private bool catchingUpStationary = false; // true after threshold reached while stationary
    private bool wasMoving = false;


    public float Pitch => xRotation;          // [-90, +90] from your clamp
    public float YawOffset => yawOffset;      // [-maxShoulderYaw, +maxShoulderYaw] 

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

        // Cache camera local positions
        standingPosition = cam.transform.localPosition;
        crouchingPosition = new Vector3(standingPosition.x, standingPosition.y - 1.1f, standingPosition.z);
        slidingPosition = new Vector3(standingPosition.x, standingPosition.y - 1.1f, standingPosition.z - 0.1f);
        sprintingPosition = new Vector3(standingPosition.x, standingPosition.y - 0.15f, standingPosition.z + 0.15f);

        cam.fieldOfView = defaultFOV;

        if (cameraRoot == null && cam != null)
            cameraRoot = cam.transform.parent; // auto-fill
    }

    void Update()
    {
        if (!IsOwner) return;

        // Slide: gently bring pitch to neutral if needed
        if (motor.sliding && xRotation > 0f)
            xRotation = Mathf.Lerp(xRotation, 0f, Time.deltaTime * 3.5f);

        // Camera height by state
        Vector3 targetPos =
            motor.sprinting ? sprintingPosition :
            motor.sliding ? slidingPosition :
            motor.crouching ? crouchingPosition : standingPosition;


        cam.transform.localPosition = Vector3.Lerp(cam.transform.localPosition, targetPos, Time.deltaTime * 7f);

        // FOV by state
        float targetFOV = ((motor.sprinting && motor.inputDirection.y > 0) || motor.sliding) ? sprintFOV : defaultFOV;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, Time.deltaTime * fovTransitionSpeed);

        // Apply the current yaw offset to the camera pivot every frame
        if (cameraRoot != null)
            cameraRoot.localRotation = Quaternion.Euler(0f, yawOffset, 0f);
    }

    public void ProcessLook(Vector2 input)
    {
        if (!IsOwner) return;

        float mouseX = input.x;
        float mouseY = input.y;

        // ---------- PITCH on the camera ----------
        if (motor.sliding && xRotation > 0f)
        {
            if (mouseY > 0f)
                xRotation -= mouseY * Time.deltaTime * ySensitivity;
        }
        else
        {
            xRotation -= mouseY * Time.deltaTime * ySensitivity;
        }
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);
        cam.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // ---------- YAW logic ----------
        bool hasMoveInput = motor.inputDirection.sqrMagnitude > 0.0001f;
        bool shoulderModeActive =
            !hasMoveInput &&
            !motor.sliding &&
            !motor.sprinting &&
            (motor.crouching ? shoulderWhileCrouching : true);

        if (shoulderModeActive)
        {
            // Accumulate yaw on the camera pivot (look-over-shoulder)
            yawOffset += mouseX * xSensitivity * Time.deltaTime;
            yawOffset = Mathf.Clamp(yawOffset, -maxShoulderYaw, maxShoulderYaw);

            // Begin catch-up once threshold is reached
            if (!catchingUpStationary && Mathf.Abs(yawOffset) >= (catchUpThreshold - 0.01f))
                catchingUpStationary = true;

            // Turn in place until aligned
            if (catchingUpStationary)
            {
                float step = bodyCatchUpSpeed * Time.deltaTime;
                CatchUp(step);

                if (Mathf.Abs(yawOffset) <= 0.01f)
                    catchingUpStationary = false;
            }
        }
        else
        {
            // Movement (or sliding/sprinting): ensure body matches camera if there is any offset
            if (hasMoveInput && !wasMoving && Mathf.Abs(yawOffset) > 0.01f)
            {
                if (instantCatchUpOnMove)
                {
                    // Snap the body to the camera direction and clear offset
                    transform.Rotate(0f, yawOffset, 0f);
                    yawOffset = 0f;
                }
                else
                {
                    // Smooth catch-up starting on move
                    float step = catchUpOnMoveSpeed * Time.deltaTime;
                    CatchUp(step);
                }
            }

            // While moving, body yaw follows mouse input normally
            transform.Rotate(Vector3.up * (mouseX * Time.deltaTime) * xSensitivity);

            // If smoothing is enabled, keep catching up during movement until aligned
            if (!instantCatchUpOnMove && hasMoveInput && Mathf.Abs(yawOffset) > 0.01f)
            {
                float step = catchUpOnMoveSpeed * Time.deltaTime;
                CatchUp(step);
            }

            catchingUpStationary = false; // moving cancels stationary catch-up
        }

        // Apply offset immediately so there is no 1-frame visual lag
        if (cameraRoot != null)
            cameraRoot.localRotation = Quaternion.Euler(0f, yawOffset, 0f);

        wasMoving = hasMoveInput;
    }

    /// <summary>
    /// Rotate the body toward the camera's world yaw by up to 'stepDegrees'.
    /// Subtracts the same delta from yawOffset so the camera view doesn't jump.
    /// </summary>
    private void CatchUp(float stepDegrees)
    {
        if (Mathf.Abs(yawOffset) <= 0.0001f) return;

        float delta = Mathf.Clamp(yawOffset, -stepDegrees, stepDegrees); // yawOffset is signed in degrees
        transform.Rotate(0f, delta, 0f);
        yawOffset -= delta;

        if (Mathf.Abs(yawOffset) <= 0.01f)
            yawOffset = 0f;
    }
    
    // Add inside PlayerLook class
    public Quaternion MoveSpaceRotation
    {
        get
        {
            // Body rotation + current camera yaw offset (ignores pitch/roll).
            return transform.rotation * Quaternion.Euler(0f, yawOffset, 0f);
        }
    }

}

