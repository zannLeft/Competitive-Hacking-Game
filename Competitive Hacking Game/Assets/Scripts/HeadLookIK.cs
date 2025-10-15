using UnityEngine;
using Unity.Netcode;

[DisallowMultipleComponent]
public class HeadLookIK : NetworkBehaviour
{
    const float LOOK_DISTANCE   = 8f;
    const float HEAD_WEIGHT     = 0.85f;
    const float EYES_WEIGHT     = 0.30f;
    const float CLAMP_WEIGHT    = 0.60f;

    const float MAX_HEAD_YAW    = 80f;  // left/right
    const float MAX_PITCH_UP    = 60f;  // look up (deg; negative)
    const float MAX_PITCH_DOWN  = 45f;  // look down

    const float BODY_W_DEFAULT  = 0.15f; // spine/chest influence when NOT crouching/coiling
    const float SEND_RATE_HZ    = 15f;   // owner → others (pitch,yaw) updates
    const float REMOTE_SMOOTH   = 12f;   // smoothing on receivers

    Animator   animator;
    PlayerLook look;          // owner only
    Transform  headBone;

    // (pitch, yawOffset) in degrees: x = pitch, y = yaw
    NetworkVariable<Vector2> netLook =
        new NetworkVariable<Vector2>(
            Vector2.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

    float lerpedPitch, lerpedYaw, sendTimer;

    public override void OnNetworkSpawn()
    {
        animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
        look     = GetComponent<PlayerLook>() ?? GetComponentInParent<PlayerLook>();

        if (animator)
        {
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            if (animator.isHuman && animator.avatar)
                headBone = animator.GetBoneTransform(HumanBodyBones.Head);
        }
    }

    void Update()
    {
        if (!IsSpawned || animator == null || headBone == null) return;

        float dt = Time.deltaTime;

        if (IsOwner && look != null)
        {
            float yaw   = Mathf.Clamp(look.YawOffset, -MAX_HEAD_YAW, MAX_HEAD_YAW);
            float pitch = Mathf.Clamp(look.Pitch,     -MAX_PITCH_UP, MAX_PITCH_DOWN);

            sendTimer += dt;
            if (sendTimer >= 1f / SEND_RATE_HZ)
            {
                sendTimer = 0f;
                Vector2 v = new Vector2(pitch, yaw);
                if ((netLook.Value - v).sqrMagnitude > 0.25f) // ~0.5°
                    netLook.Value = v;
            }

            lerpedPitch = pitch;
            lerpedYaw   = yaw;
        }
        else
        {
            Vector2 t = netLook.Value;
            lerpedPitch = Mathf.Lerp(lerpedPitch, t.x, dt * REMOTE_SMOOTH);
            lerpedYaw   = Mathf.Lerp(lerpedYaw,   t.y, dt * REMOTE_SMOOTH);
        }

        lerpedPitch = Mathf.Clamp(lerpedPitch, -MAX_PITCH_UP, MAX_PITCH_DOWN);
        lerpedYaw   = Mathf.Clamp(lerpedYaw,   -MAX_HEAD_YAW, MAX_HEAD_YAW);
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (!IsSpawned || animator == null || headBone == null) return;

        // Read animator states
        bool isCrouching = animator.GetBool("Crouching");
        bool isCoiling   = animator.GetBool("Coiling"); // NEW

        // Ignore up/down (pitch) while crouching OR coiling
        bool ignorePitch = isCrouching || isCoiling;

        // Spine/chest influence off while crouching or coiling (same behavior)
        float bodyW = ignorePitch ? 0f : BODY_W_DEFAULT;

        // Build a yaw-only or full look rotation
        Quaternion yawOnly = transform.rotation * Quaternion.Euler(0f, lerpedYaw, 0f);
        Quaternion lookRot = ignorePitch ? yawOnly
                                         : yawOnly * Quaternion.Euler(lerpedPitch, 0f, 0f);

        Vector3 headPos    = headBone.position;
        Vector3 lookTarget = headPos + (lookRot * Vector3.forward) * LOOK_DISTANCE;

        animator.SetLookAtWeight(1f, bodyW, HEAD_WEIGHT, EYES_WEIGHT, CLAMP_WEIGHT);
        animator.SetLookAtPosition(lookTarget);
    }
}
