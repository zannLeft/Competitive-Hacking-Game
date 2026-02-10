// HeadLookIK.cs (FULL) — restored crouch/coiling suppression (no phone-specific crouch differences beyond that)
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

    const float SEND_RATE_HZ = 15f; // owner → others (pitch,yaw) updates
    const float REMOTE_SMOOTH = 12f; // smoothing on receivers

    [Header("Upper-Body Weight (smooth boost while phone up)")]
    [SerializeField]
    private string phoneMaskParam = "PhoneMask"; // must match PlayerPhone

    [SerializeField, Range(0f, 1f)]
    private float bodyWeightDefault = 0.15f; // normal upper-body follow

    [SerializeField, Range(0f, 1f)]
    private float bodyWeightPhoneUp = 1.00f; // while phone is up

    [SerializeField]
    private float bodyWeightSmoothTime = 0.14f; // seconds

    [SerializeField]
    private float bodyWeightMaxSpeed = 0f; // 0 = unlimited

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

    // Smoothed body weight
    int _phoneMaskHash;
    float _bodyW;
    float _bodyWVel;

    const float SnapEps = 1e-4f;

    public override void OnNetworkSpawn()
    {
        animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
        look = GetComponent<PlayerLook>() ?? GetComponentInParent<PlayerLook>();

        if (animator)
        {
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            if (animator.isHuman && animator.avatar)
                headBone = animator.GetBoneTransform(HumanBodyBones.Head);
        }

        _phoneMaskHash = Animator.StringToHash(phoneMaskParam);
        _bodyW = Mathf.Clamp01(bodyWeightDefault);
        _bodyWVel = 0f;
    }

    void Update()
    {
        if (!IsSpawned || animator == null || headBone == null)
            return;

        float dt = Time.deltaTime;

        // --- look replication ---
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

        // --- Restore original: no spine/chest motion while crouching or coiling ---
        bool isCrouching = animator.GetBool("Crouching");
        bool isCoiling = animator.GetBool("Coiling");

        float targetBodyW;
        if (isCrouching || isCoiling)
        {
            targetBodyW = 0f;
        }
        else
        {
            float phoneBlend = Mathf.Clamp01(animator.GetFloat(_phoneMaskHash));
            targetBodyW = Mathf.Lerp(bodyWeightDefault, bodyWeightPhoneUp, phoneBlend);
        }

        float smooth = Mathf.Max(0.0001f, bodyWeightSmoothTime);
        _bodyW = Mathf.SmoothDamp(
            _bodyW,
            targetBodyW,
            ref _bodyWVel,
            smooth,
            bodyWeightMaxSpeed <= 0f ? float.PositiveInfinity : bodyWeightMaxSpeed,
            dt
        );

        _bodyW = Mathf.Clamp01(_bodyW);

        if (Mathf.Abs(_bodyW - targetBodyW) < SnapEps)
        {
            _bodyW = targetBodyW;
            _bodyWVel = 0f;
        }
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (!IsSpawned || animator == null || headBone == null)
            return;

        // Keep: suppress pitch only when coiling
        bool isCoiling = animator.GetBool("Coiling");
        bool ignorePitch = isCoiling;

        Quaternion yawOnly = transform.rotation * Quaternion.Euler(0f, lerpedYaw, 0f);
        Quaternion lookRot = ignorePitch
            ? yawOnly
            : yawOnly * Quaternion.Euler(lerpedPitch, 0f, 0f);

        Vector3 headPos = headBone.position;
        Vector3 lookTarget = headPos + (lookRot * Vector3.forward) * LOOK_DISTANCE;

        animator.SetLookAtWeight(1f, _bodyW, HEAD_WEIGHT, EYES_WEIGHT, CLAMP_WEIGHT);
        animator.SetLookAtPosition(lookTarget);
    }
}
