using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class HeadLookIK : NetworkBehaviour
{
    const float LOOK_DISTANCE = 8f;
    const float HEAD_WEIGHT = 0.85f;
    const float EYES_WEIGHT = 0.30f;
    const float CLAMP_WEIGHT = 0.60f;

    const float MAX_HEAD_YAW = 80f;
    const float MAX_PITCH_UP = 60f;
    const float MAX_PITCH_DOWN = 45f;

    const float BODY_W_DEFAULT = 0.15f;
    const float SEND_RATE_HZ = 15f;
    const float REMOTE_SMOOTH = 12f;

    [Header("Dead Look")]
    [SerializeField]
    private float deadMaxHeadYaw = 90f;

    [SerializeField]
    private float deadMaxPitchUp = 75f;

    [Tooltip("0 = dead player cannot look below horizon/down into chest.")]
    [SerializeField]
    private float deadMaxPitchDown = 0f;

    [Header("Phone-up influence (yaw boost, pitch stays subtle)")]
    [SerializeField]
    private string phoneMaskParam = "PhoneMask";

    [Range(0f, 1f)]
    [SerializeField]
    private float phoneBodyWeight = 1.0f;

    [Range(0f, 1f)]
    [SerializeField]
    private float phonePitchScale = 0.15f;

    [SerializeField]
    private float phoneBlendThreshold = 0.02f;

    [Header("Smoothing (prevents crouch->stand pop)")]
    [SerializeField]
    private float bodyWeightSmoothTime = 0.10f;

    [SerializeField]
    private float pitchScaleSmoothTime = 0.10f;

    [Header("Crouch -> Stand Pop Fix")]
    [SerializeField]
    private float crouchExitHoldTime = 0.08f;

    Animator animator;
    PlayerLook look;
    PlayerSitAction sitAction;
    PlayerDeathState deathState;
    Transform headBone;

    NetworkVariable<Vector2> netLook = new NetworkVariable<Vector2>(
        Vector2.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    float lerpedPitch;
    float lerpedYaw;
    float sendTimer;

    int _phoneMaskHash;
    float _phoneBlend;

    float _smBodyW = BODY_W_DEFAULT;
    float _smBodyWVel;

    float _smPitchScale = 1f;
    float _smPitchScaleVel;

    bool _wasCrouching;
    float _crouchExitHoldTimer;

    public override void OnNetworkSpawn()
    {
        animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
        look = GetComponent<PlayerLook>() ?? GetComponentInParent<PlayerLook>();
        sitAction = GetComponent<PlayerSitAction>() ?? GetComponentInParent<PlayerSitAction>();
        deathState = GetComponent<PlayerDeathState>() ?? GetComponentInParent<PlayerDeathState>();

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
        bool isDead = deathState != null && deathState.IsDead;

        _phoneBlend = 0f;

        if (!isDead && !string.IsNullOrEmpty(phoneMaskParam))
        {
            _phoneBlend = Mathf.Clamp01(animator.GetFloat(_phoneMaskHash));

            if (_phoneBlend < phoneBlendThreshold)
                _phoneBlend = 0f;
        }

        float yawLimit = isDead ? deadMaxHeadYaw : MAX_HEAD_YAW;
        float pitchUpLimit = isDead ? deadMaxPitchUp : MAX_PITCH_UP;
        float pitchDownLimit = isDead ? deadMaxPitchDown : MAX_PITCH_DOWN;

        if (IsOwner && look != null)
        {
            float yaw = Mathf.Clamp(look.YawOffset, -yawLimit, yawLimit);
            float pitch = Mathf.Clamp(look.Pitch, -pitchUpLimit, pitchDownLimit);

            sendTimer += dt;

            if (sendTimer >= 1f / SEND_RATE_HZ)
            {
                sendTimer = 0f;
                Vector2 v = new Vector2(pitch, yaw);

                if ((netLook.Value - v).sqrMagnitude > 0.25f)
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

        lerpedPitch = Mathf.Clamp(lerpedPitch, -pitchUpLimit, pitchDownLimit);
        lerpedYaw = Mathf.Clamp(lerpedYaw, -yawLimit, yawLimit);
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (!IsSpawned || animator == null || headBone == null)
            return;

        float dt = Time.deltaTime;

        bool isDead = deathState != null && deathState.IsDead;

        bool isCrouching = animator.GetBool("Crouching");
        bool isCoiling = animator.GetBool("Coiling");

        bool isLaptopSitting = sitAction != null && sitAction.IsSittingOrTransitioning;

        if (_wasCrouching && !isCrouching)
            _crouchExitHoldTimer = Mathf.Max(0f, crouchExitHoldTime);

        _wasCrouching = isCrouching;

        if (_crouchExitHoldTimer > 0f)
            _crouchExitHoldTimer = Mathf.Max(0f, _crouchExitHoldTimer - dt);

        bool treatAsCrouching = isCrouching || (_crouchExitHoldTimer > 0f);

        bool ignorePitch = isCoiling;

        // Important:
        // Dead = head only. No torso/spine rotation, otherwise the body clips into the floor.
        bool spineLocked = treatAsCrouching || isCoiling || isLaptopSitting || isDead;

        float targetBodyW = spineLocked ? 0f : BODY_W_DEFAULT;
        float targetPitchScale = 1f;

        if (!spineLocked && _phoneBlend > 0f)
        {
            targetBodyW = Mathf.Lerp(BODY_W_DEFAULT, phoneBodyWeight, _phoneBlend);
            targetPitchScale = Mathf.Lerp(1f, phonePitchScale, _phoneBlend);
        }

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

        Quaternion yawOnly = transform.rotation * Quaternion.Euler(0f, lerpedYaw, 0f);

        float appliedPitch = ignorePitch ? 0f : (lerpedPitch * _smPitchScale);
        Quaternion lookRot = yawOnly * Quaternion.Euler(appliedPitch, 0f, 0f);

        Vector3 headPos = headBone.position;
        Vector3 lookTarget = headPos + (lookRot * Vector3.forward) * LOOK_DISTANCE;

        animator.SetLookAtWeight(1f, _smBodyW, HEAD_WEIGHT, EYES_WEIGHT, CLAMP_WEIGHT);
        animator.SetLookAtPosition(lookTarget);
    }
}