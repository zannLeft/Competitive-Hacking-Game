// PlayerPhone.cs (FULL) — RMB-only, and DISABLED while sliding or coiling
// UPDATED: Stabilize phone while moving by allowing full IK lock while walking/sprinting
//          + IK rotation weight now matches IK position weight (prevents jitter from mixed weights)

using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerPhone : NetworkBehaviour
{
    [Header("References")]
    [SerializeField]
    private Animator animator;

    [SerializeField]
    private PhoneTargetHandler targetHandler;

    [SerializeField]
    private Transform phoneAttachR;

    [SerializeField]
    private GameObject phonePrefab;

    [SerializeField]
    private PlayerMotor motor;

    [Header("Animator Params & Layer")]
    [SerializeField]
    private string phoneMaskParam = "PhoneMask";

    [SerializeField]
    private string phoneIKParam = "PhoneIK";

    [SerializeField]
    private int phoneLayerIndex = 1;

    [Header("Easing (Mask Layer Weight)")]
    [SerializeField]
    private float maskEaseInTime = 0.12f;

    [SerializeField]
    private float maskEaseOutTime = 0.12f;

    [SerializeField]
    private float maskMaxSpeed = 0f;

    [Header("Easing (IK Weight)")]
    [SerializeField]
    private float ikEaseInTime = 0.10f;

    [SerializeField]
    private float ikEaseOutTime = 0.10f;

    [SerializeField]
    private float ikMaxSpeed = 0f;

    [Header("IK weight caps")]
    [Range(0f, 1f)]
    [SerializeField]
    private float idleIKMax = 1.00f;

    [Range(0f, 1f)]
    [SerializeField]
    private float movingIKMax = 1.00f; // NEW: keep phone stable while walking

    [Range(0f, 1f)]
    [SerializeField]
    private float sprintIKMax = 1.00f; // NEW: keep phone stable while sprinting

    [Range(0f, 1f)]
    [SerializeField]
    private float slideIKMax = 0.01f; // (still allowed if you want “less hand lock” while sliding)

    [Header("Remote Approx Target (non-owner)")]
    [SerializeField]
    private float approxDistance = 0.45f;

    [SerializeField]
    private float approxHorizontal = 0.06f;

    [SerializeField]
    private float approxVertical = -0.02f;

    [SerializeField]
    private Vector3 remoteRotOffsetEuler = new Vector3(0f, 90f, 0f);

    private static readonly Vector3 kPhoneLocalPosition = new Vector3(-0.03f, 0f, 0.01f);
    private static readonly Vector3 kPhoneLocalEuler = new Vector3(20f, 0f, 90f);
    private static readonly Vector3 kIKHandRotOffsetEuler = new Vector3(0f, 90f, -90f);

    [Header("Optional: Elbow Hint")]
    [SerializeField]
    private Transform rightElbowHint;

    [SerializeField]
    private float elbowHintWeight = 0.5f;

    private int _maskHash,
        _ikHash;
    private bool _rmbHeld;

    private float _targetBlend;
    private float _maskWeight,
        _ikWeight;
    private float _maskVel,
        _ikVel;

    private bool _phoneVisible;
    private GameObject _spawnedPhone;
    private PhoneScreenController _screenController;
    private Transform _headBone;

    private bool _targetActive;
    private float _lastLayerWeight = -1f;

    private const float SnapEps = 1e-4f;

    void Reset()
    {
        animator = GetComponent<Animator>();
        targetHandler = GetComponent<PhoneTargetHandler>();
        motor = GetComponent<PlayerMotor>();
    }

    public override void OnNetworkSpawn()
    {
        if (animator == null)
            animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
        if (targetHandler == null)
            targetHandler = GetComponent<PhoneTargetHandler>();
        if (motor == null)
            motor = GetComponent<PlayerMotor>();

        if (animator != null && animator.isHuman && animator.avatar)
            _headBone = animator.GetBoneTransform(HumanBodyBones.Head);

        _maskHash = Animator.StringToHash(phoneMaskParam);
        _ikHash = Animator.StringToHash(phoneIKParam);

        if (phoneAttachR == null && animator != null && animator.isHuman && animator.avatar)
        {
            var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (rightHand != null)
            {
                var found = rightHand.Find("PhoneAttach_R");
                if (found != null)
                    phoneAttachR = found;
            }
        }

        if (phoneAttachR != null && phonePrefab == null)
        {
            foreach (Transform child in phoneAttachR)
            {
                _spawnedPhone = child.gameObject;
                _spawnedPhone.SetActive(false);
                break;
            }
        }

        ApplyPhoneOffsets();
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // Phone is allowed only if RMB held AND we are NOT sliding/coiling
        bool phoneAllowed = _rmbHeld && !(motor != null && (motor.sliding || motor.Coiling));

        if (IsOwner)
        {
            _targetBlend = phoneAllowed ? 1f : 0f;

            float maskSmooth =
                (_targetBlend > _maskWeight)
                    ? Mathf.Max(0.0001f, maskEaseInTime)
                    : Mathf.Max(0.0001f, maskEaseOutTime);

            _maskWeight = Mathf.SmoothDamp(
                _maskWeight,
                _targetBlend,
                ref _maskVel,
                maskSmooth,
                maskMaxSpeed <= 0f ? float.PositiveInfinity : maskMaxSpeed,
                dt
            );

            _maskWeight = Mathf.Clamp01(_maskWeight);
            if (_targetBlend <= 0f && _maskWeight < SnapEps)
            {
                _maskWeight = 0f;
                _maskVel = 0f;
            }
            else if (_targetBlend >= 1f && (1f - _maskWeight) < SnapEps)
            {
                _maskWeight = 1f;
                _maskVel = 0f;
            }

            animator.SetFloat(_maskHash, _maskWeight);

            float ikTarget = phoneAllowed ? ResolveCurrentIkCap() : 0f;

            float ikSmooth =
                (ikTarget > _ikWeight)
                    ? Mathf.Max(0.0001f, ikEaseInTime)
                    : Mathf.Max(0.0001f, ikEaseOutTime);

            _ikWeight = Mathf.SmoothDamp(
                _ikWeight,
                ikTarget,
                ref _ikVel,
                ikSmooth,
                ikMaxSpeed <= 0f ? float.PositiveInfinity : ikMaxSpeed,
                dt
            );

            _ikWeight = Mathf.Clamp01(_ikWeight);
            if (Mathf.Abs(_ikWeight - ikTarget) < SnapEps)
            {
                _ikWeight = ikTarget;
                _ikVel = 0f;
            }

            animator.SetFloat(_ikHash, _ikWeight);
        }
        else
        {
            _maskWeight = animator.GetFloat(_maskHash);
            _ikWeight = animator.GetFloat(_ikHash);
        }

        if (phoneLayerIndex >= 0 && phoneLayerIndex < animator.layerCount)
        {
            if (!Mathf.Approximately(_lastLayerWeight, _maskWeight))
            {
                animator.SetLayerWeight(phoneLayerIndex, _maskWeight);
                _lastLayerWeight = _maskWeight;
            }
        }

        if (IsOwner && targetHandler != null)
        {
            bool shouldBeActive = (_maskWeight > 0f) || (_ikWeight > 0f);
            if (shouldBeActive != _targetActive)
            {
                targetHandler.SetPhoneActive(shouldBeActive);
                _targetActive = shouldBeActive;
            }
        }

        if (!_phoneVisible && _ikWeight > 0f)
            ShowPhone();
        else if (_phoneVisible && _ikWeight <= 0f)
            HidePhone();

        ApplyPhoneOffsets();

        // Screen ON only when RMB held AND phone is allowed (owner only)
        if (_spawnedPhone != null && IsOwner)
        {
            if (_screenController == null)
                _screenController = _spawnedPhone.GetComponent<PhoneScreenController>();
            _screenController?.SetScreenOn(phoneAllowed);
        }
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (animator == null)
            return;

        // UPDATED: Make rotation lock follow IK weight too (prevents jitter from mixed weights)
        float posW = _ikWeight;
        float rotW = _ikWeight;

        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, posW);
        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, rotW);

        if (rightElbowHint != null)
            animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, posW * elbowHintWeight);

        if (posW <= 0f)
            return;

        if (IsOwner && targetHandler != null)
            targetHandler.UpdateAnchorImmediate(Time.deltaTime);

        Quaternion handRotOffset = Quaternion.Euler(kIKHandRotOffsetEuler);

        if (IsOwner && targetHandler != null && targetHandler.IKAnchor != null)
        {
            Transform a = targetHandler.IKAnchor;
            animator.SetIKPosition(AvatarIKGoal.RightHand, a.position);
            animator.SetIKRotation(AvatarIKGoal.RightHand, a.rotation * handRotOffset);
        }
        else
        {
            ResolveRemoteApproxTarget(out Vector3 pos, out Quaternion baseRot);
            animator.SetIKPosition(AvatarIKGoal.RightHand, pos);
            animator.SetIKRotation(AvatarIKGoal.RightHand, baseRot * handRotOffset);
        }

        if (rightElbowHint != null)
            animator.SetIKHintPosition(AvatarIKHint.RightElbow, rightElbowHint.position);
    }

    public void SetHolding(bool holding)
    {
        if (!IsOwner)
            return;
        _rmbHeld = holding;
    }

    private void ShowPhone()
    {
        _phoneVisible = true;

        if (_spawnedPhone == null)
        {
            if (phonePrefab != null && phoneAttachR != null)
                _spawnedPhone = Instantiate(phonePrefab, phoneAttachR, false);
            else if (phoneAttachR != null && phoneAttachR.childCount > 0)
                _spawnedPhone = phoneAttachR.GetChild(0).gameObject;
        }

        if (_spawnedPhone != null)
        {
            _spawnedPhone.SetActive(true);
            ApplyPhoneOffsets();
        }
    }

    private void HidePhone()
    {
        _phoneVisible = false;

        if (_spawnedPhone != null)
            _spawnedPhone.SetActive(false);
    }

    private void ApplyPhoneOffsets()
    {
        if (_spawnedPhone == null || phoneAttachR == null)
            return;

        var t = _spawnedPhone.transform;
        t.localPosition = kPhoneLocalPosition;
        t.localRotation = Quaternion.Euler(kPhoneLocalEuler);
    }

    private void ResolveRemoteApproxTarget(out Vector3 pos, out Quaternion rot)
    {
        if (_headBone != null)
        {
            rot = _headBone.rotation * Quaternion.Euler(remoteRotOffsetEuler);
            pos =
                _headBone.position
                + _headBone.forward * approxDistance
                + _headBone.right * approxHorizontal
                + _headBone.up * approxVertical;
        }
        else
        {
            rot = transform.rotation * Quaternion.Euler(remoteRotOffsetEuler);
            pos = transform.position + transform.forward * 0.4f + Vector3.up * 1.2f;
        }
    }

    private float ResolveCurrentIkCap()
    {
        if (motor == null)
            return idleIKMax;

        // (Phone is already blocked during sliding, but keep this as a safety cap)
        if (motor.sliding)
            return slideIKMax;

        bool isMoving = motor.inputDirection.sqrMagnitude > 0.0001f;
        bool isSprinting = motor.IsActuallySprinting;

        if (isSprinting)
            return sprintIKMax;

        if (isMoving)
            return movingIKMax;

        return idleIKMax;
    }
}
