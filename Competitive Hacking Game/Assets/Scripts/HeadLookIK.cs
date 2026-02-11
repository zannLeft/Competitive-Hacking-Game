using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class HeadLookIK : NetworkBehaviour
{
    const float LOOK_DISTANCE = 8f;
    const float HEAD_WEIGHT = 0.85f;
    const float EYES_WEIGHT = 0.30f;
    const float CLAMP_WEIGHT = 0.60f;

    const float MAX_HEAD_YAW = 80f; // left/right
    const float MAX_PITCH_UP = 60f; // look up (deg; negative)
    const float MAX_PITCH_DOWN = 45f; // look down

    const float BODY_W_DEFAULT = 0.15f; // spine/chest influence normally
    const float SEND_RATE_HZ = 15f;
    const float REMOTE_SMOOTH = 12f;

    [Header("Phone-up influence (yaw boost, pitch stays subtle)")]
    [SerializeField]
    private string phoneMaskParam = "PhoneMask"; // replicated float 0..1 from PlayerPhone

    [Tooltip("How much spine/chest influence we want when phone is up (helps yaw).")]
    [Range(0f, 1f)]
    [SerializeField]
    private float phoneBodyWeight = 1.0f;

    [Tooltip("Scale applied to pitch when phone is up (0.15 keeps vertical like default).")]
    [Range(0f, 1f)]
    [SerializeField]
    private float phonePitchScale = 0.15f;

    [Tooltip("Blend threshold to consider phone 'up'. Still uses smooth lerp from PhoneMask.")]
    [SerializeField]
    private float phoneBlendThreshold = 0.02f;

    [Header("Smoothing (prevents crouch->stand pop)")]
    [Tooltip(
        "How quickly body weight blends when states change (e.g. crouch->stand while phone up)."
    )]
    [SerializeField]
    private float bodyWeightSmoothTime = 0.10f;

    [Tooltip("How quickly pitch scaling blends when states change.")]
    [SerializeField]
    private float pitchScaleSmoothTime = 0.10f;

    [Header("Crouch -> Stand Pop Fix")]
    [Tooltip(
        "When Crouching turns false, keep 'crouch rules' for this long, so LookAt doesn't hit the spine during the first frames of the stand-up transition."
    )]
    [SerializeField]
    private float crouchExitHoldTime = 0.08f; // try 0.06–0.12

    Animator animator;
    PlayerLook look; // owner only
    Transform headBone;

    // (pitch, yawOffset) in degrees: x = pitch, y = yaw
    NetworkVariable<Vector2> netLook = new NetworkVariable<Vector2>(
        Vector2.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    float lerpedPitch,
        lerpedYaw,
        sendTimer;

    int _phoneMaskHash;
    float _phoneBlend; // 0..1

    // Smoothed outputs (avoid 0->1 jumps)
    float _smBodyW = BODY_W_DEFAULT;
    float _smBodyWVel;

    float _smPitchScale = 1f;
    float _smPitchScaleVel;

    // crouch-exit hold
    bool _wasCrouching;
    float _crouchExitHoldTimer;

    public override void OnNetworkSpawn()
    {
        animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
        look = GetComponent<PlayerLook>() ?? GetComponentInParent<PlayerLook>();

        if (animator)
        {
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            if (animator.isHuman && animator.avatar)
                headBone = animator.GetBoneTransform(HumanBodyBones.Head);

            _phoneMaskHash = Animator.StringToHash(phoneMaskParam);

            _wasCrouching = animator.GetBool("Crouching");
        }

        _smBodyW = BODY_W_DEFAULT;
        _smPitchScale = 1f;
        _smBodyWVel = 0f;
        _smPitchScaleVel = 0f;

        _crouchExitHoldTimer = 0f;
    }

    void Update()
    {
        if (!IsSpawned || animator == null || headBone == null)
            return;

        float dt = Time.deltaTime;

        // Read phone blend (replicated) if param exists; otherwise stays 0.
        _phoneBlend = 0f;
        if (!string.IsNullOrEmpty(phoneMaskParam))
        {
            _phoneBlend = Mathf.Clamp01(animator.GetFloat(_phoneMaskHash));
            if (_phoneBlend < phoneBlendThreshold)
                _phoneBlend = 0f;
        }

        if (IsOwner && look != null)
        {
            float yaw = Mathf.Clamp(look.YawOffset, -MAX_HEAD_YAW, MAX_HEAD_YAW);
            float pitch = Mathf.Clamp(look.Pitch, -MAX_PITCH_UP, MAX_PITCH_DOWN);

            sendTimer += dt;
            if (sendTimer >= 1f / SEND_RATE_HZ)
            {
                sendTimer = 0f;
                Vector2 v = new Vector2(pitch, yaw);
                if ((netLook.Value - v).sqrMagnitude > 0.25f) // ~0.5°
                    netLook.Value = v;
            }

            lerpedPitch = pitch;
            lerpedYaw = yaw;
        }
        else
        {
            Vector2 t = netLook.Value;
            lerpedPitch = Mathf.Lerp(lerpedPitch, t.x, dt * REMOTE_SMOOTH);
            lerpedYaw = Mathf.Lerp(lerpedYaw, t.y, dt * REMOTE_SMOOTH);
        }

        lerpedPitch = Mathf.Clamp(lerpedPitch, -MAX_PITCH_UP, MAX_PITCH_DOWN);
        lerpedYaw = Mathf.Clamp(lerpedYaw, -MAX_HEAD_YAW, MAX_HEAD_YAW);
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (!IsSpawned || animator == null || headBone == null)
            return;

        float dt = Time.deltaTime;

        // Read animator states
        bool isCrouching = animator.GetBool("Crouching");
        bool isCoiling = animator.GetBool("Coiling");

        // Start the exit-hold when crouch turns off
        if (_wasCrouching && !isCrouching)
            _crouchExitHoldTimer = Mathf.Max(0f, crouchExitHoldTime);

        _wasCrouching = isCrouching;

        if (_crouchExitHoldTimer > 0f)
            _crouchExitHoldTimer = Mathf.Max(0f, _crouchExitHoldTimer - dt);

        bool treatAsCrouching = isCrouching || (_crouchExitHoldTimer > 0f);

        // Keep head pitch while crouching; suppress pitch only when coiling
        bool ignorePitch = isCoiling;

        // ---- Compute TARGET values (then smooth) ----
        bool spineLocked = treatAsCrouching || isCoiling;

        float targetBodyW = spineLocked ? 0f : BODY_W_DEFAULT;
        float targetPitchScale = 1f;

        if (!spineLocked && _phoneBlend > 0f)
        {
            targetBodyW = Mathf.Lerp(BODY_W_DEFAULT, phoneBodyWeight, _phoneBlend);
            targetPitchScale = Mathf.Lerp(1f, phonePitchScale, _phoneBlend);
        }

        // Smooth them so crouch->stand doesn't pop through a bad intermediate pose
        float bwSmooth = Mathf.Max(0.0001f, bodyWeightSmoothTime);
        float psSmooth = Mathf.Max(0.0001f, pitchScaleSmoothTime);

        _smBodyW = Mathf.SmoothDamp(
            _smBodyW,
            targetBodyW,
            ref _smBodyWVel,
            bwSmooth,
            Mathf.Infinity,
            dt
        );
        _smPitchScale = Mathf.SmoothDamp(
            _smPitchScale,
            targetPitchScale,
            ref _smPitchScaleVel,
            psSmooth,
            Mathf.Infinity,
            dt
        );

        _smBodyW = Mathf.Clamp01(_smBodyW);
        _smPitchScale = Mathf.Clamp01(_smPitchScale);

        // ---- Build look target ----
        Quaternion yawOnly = transform.rotation * Quaternion.Euler(0f, lerpedYaw, 0f);

        float appliedPitch = ignorePitch ? 0f : (lerpedPitch * _smPitchScale);
        Quaternion lookRot = yawOnly * Quaternion.Euler(appliedPitch, 0f, 0f);

        Vector3 headPos = headBone.position;
        Vector3 lookTarget = headPos + (lookRot * Vector3.forward) * LOOK_DISTANCE;

        animator.SetLookAtWeight(1f, _smBodyW, HEAD_WEIGHT, EYES_WEIGHT, CLAMP_WEIGHT);
        animator.SetLookAtPosition(lookTarget);
    }
}
