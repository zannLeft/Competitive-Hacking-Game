using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerPhone : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;                    // Auto-fills if null
    [SerializeField] private PhoneTargetHandler targetHandler;     // Owner-only (optional on remotes)
    [SerializeField] private Transform phoneAttachR;               // "PhoneAttach_R" on right hand
    [SerializeField] private GameObject phonePrefab;               // Optional: if no child exists under PhoneAttach_R

    [Header("Animator Params & Layer")]
    [SerializeField] private string phoneMaskParam = "PhoneMask";  // Animator float (replicated via NetworkAnimator)
    [SerializeField] private string phoneIKParam = "PhoneIK";    // Animator float (replicated via NetworkAnimator)
    [SerializeField] private int phoneLayerIndex = 1;           // Right-arm mask layer index

    [Header("Easing (Mask Layer Weight)")]
    [SerializeField] private float maskEaseInTime = 0.12f;
    [SerializeField] private float maskEaseOutTime = 0.12f;
    [SerializeField] private float maskMaxSpeed = 0f;

    [Header("Easing (IK Weight)")]
    [SerializeField] private float ikEaseInTime = 0.10f;
    [SerializeField] private float ikEaseOutTime = 0.10f;
    [SerializeField] private float ikMaxSpeed = 0f;

    [Header("Remote Approx Target (non-owner)")]
    [SerializeField] private float approxDistance = 0.45f;  // forward from head
    [SerializeField] private float approxHorizontal = 0.06f;  // right from head
    [SerializeField] private float approxVertical = -0.02f; // up from head (negative = down)
    [SerializeField] private Vector3 remoteRotOffsetEuler = new Vector3(0f, 90f, 0f);

    // BAKED values (restored)
    private static readonly Vector3 kPhoneLocalPosition = new Vector3(-0.03f, 0f, 0.01f);
    private static readonly Vector3 kPhoneLocalEuler = new Vector3(20f, 0f, 90f);
    private static readonly Vector3 kIKHandRotOffsetEuler = new Vector3(0f, 90f, -90f); // ← original

    [Header("Optional: Elbow Hint")]
    [SerializeField] private Transform rightElbowHint;
    [SerializeField] private float elbowHintWeight = 0.5f;

    [Header("Flashlight (networked)")]
    [SerializeField] private string flashlightNodeName = "FlashLight"; // child under the phone prefab
    private readonly NetworkVariable<bool> _flashOn = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public bool IsFlashlightOn => _flashOn.Value;

    // --- internals ---
    private int _maskHash, _ikHash;
    private bool _rmbHeld;                 // set by InputManager (owner only)
    private float _targetBlend;            // 0..1 derived from (_rmbHeld || _flashOn)
    private float _maskWeight, _ikWeight;  // smoothed outputs
    private float _maskVel, _ikVel;        // SmoothDamp velocities
    private bool _phoneVisible;
    private GameObject _spawnedPhone;
    private PhoneScreenController _screenController;
    private Transform _headBone;
    private bool _targetActive;
    private float _lastLayerWeight = -1f;
    private Light _flash;

    private const float SnapEps = 1e-4f;

    void Reset()
    {
        animator = GetComponent<Animator>();
        targetHandler = GetComponent<PhoneTargetHandler>();
    }

    public override void OnNetworkSpawn()
    {
        // Animator + handler
        if (animator == null)
            animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
        if (targetHandler == null)
            targetHandler = GetComponent<PhoneTargetHandler>();

        // Bones
        if (animator != null && animator.isHuman && animator.avatar)
            _headBone = animator.GetBoneTransform(HumanBodyBones.Head);

        // Hashes
        _maskHash = Animator.StringToHash(phoneMaskParam);
        _ikHash = Animator.StringToHash(phoneIKParam);

        // Auto-find PhoneAttach_R if missing
        if (phoneAttachR == null && animator != null && animator.isHuman && animator.avatar)
        {
            var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (rightHand != null)
            {
                var found = rightHand.Find("PhoneAttach_R");
                if (found != null) phoneAttachR = found;
            }
        }

        // If no prefab, try first child under attach (kept disabled initially)
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

        // Flashlight replication
        _flashOn.OnValueChanged += OnFlashChanged;
    }

    public override void OnNetworkDespawn()
    {
        _flashOn.OnValueChanged -= OnFlashChanged;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // 1) Owner decides if phone should be up
        if (IsOwner)
        {
            // Phone is up if either RMB is held OR flashlight is on
            _targetBlend = (_rmbHeld || _flashOn.Value) ? 1f : 0f;

            // Easing for mask and IK (both directions)
            float maskSmooth = (_targetBlend > _maskWeight ? Mathf.Max(0.0001f, maskEaseInTime)
                                                           : Mathf.Max(0.0001f, maskEaseOutTime));
            float ikSmooth = (_targetBlend > _ikWeight ? Mathf.Max(0.0001f, ikEaseInTime)
                                                           : Mathf.Max(0.0001f, ikEaseOutTime));

            _maskWeight = Mathf.SmoothDamp(
                _maskWeight, _targetBlend, ref _maskVel, maskSmooth,
                maskMaxSpeed <= 0f ? float.PositiveInfinity : maskMaxSpeed, dt);

            _ikWeight = Mathf.SmoothDamp(
                _ikWeight, _targetBlend, ref _ikVel, ikSmooth,
                ikMaxSpeed <= 0f ? float.PositiveInfinity : ikMaxSpeed, dt);

            // Snap to exact 0/1 near ends
            if (_targetBlend <= 0f)
            {
                if (_maskWeight < SnapEps) { _maskWeight = 0f; _maskVel = 0f; }
                if (_ikWeight < SnapEps) { _ikWeight = 0f; _ikVel = 0f; }
            }
            else if (_targetBlend >= 1f)
            {
                if (1f - _maskWeight < SnapEps) { _maskWeight = 1f; _maskVel = 0f; }
                if (1f - _ikWeight < SnapEps) { _ikWeight = 1f; _ikVel = 0f; }
            }

            _maskWeight = Mathf.Clamp01(_maskWeight);
            _ikWeight = Mathf.Clamp01(_ikWeight);

            animator.SetFloat(_maskHash, _maskWeight);
            animator.SetFloat(_ikHash, _ikWeight);
        }
        else
        {
            // Remotes read what the owner wrote via NetworkAnimator
            _maskWeight = animator.GetFloat(_maskHash);
            _ikWeight = animator.GetFloat(_ikHash);
        }

        // 2) Drive right-arm layer weight
        if (phoneLayerIndex >= 0 && phoneLayerIndex < animator.layerCount)
        {
            if (!Mathf.Approximately(_lastLayerWeight, _maskWeight))
            {
                animator.SetLayerWeight(phoneLayerIndex, _maskWeight);
                _lastLayerWeight = _maskWeight;
            }
        }

        // 3) Activate/deactivate PhoneTarget based on CURRENT weights (not raw input)
        if (IsOwner && targetHandler != null)
        {
            bool shouldBeActive = (_maskWeight > 0f) || (_ikWeight > 0f);
            if (shouldBeActive != _targetActive)
            {
                targetHandler.SetPhoneActive(shouldBeActive);
                _targetActive = shouldBeActive;
            }
        }

        // 4) Phone prop visibility: show when IK > 0, hide when <= 0
        if (!_phoneVisible && _ikWeight > 0f) ShowPhone();
        else if (_phoneVisible && _ikWeight <= 0f) HidePhone();

        // 5) Keep applying local offsets so you can live-tweak in play mode
        ApplyPhoneOffsets();

        // 6) Screen ON only while RMB is held (owner-only visual)
        if (_spawnedPhone != null && IsOwner)
        {
            if (_screenController == null)
                _screenController = _spawnedPhone.GetComponent<PhoneScreenController>();
            _screenController?.SetScreenOn(_rmbHeld);
        }

        // 7) Flashlight component reflect network state (on only while phone is up)
        UpdateFlashlightActive();
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (animator == null) return;

        float w = _ikWeight;

        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, w);
        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, w);

        if (rightElbowHint != null)
            animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, w * elbowHintWeight);

        if (w <= 0f) return;

        // Ensure anchor pose is up-to-date (kills spin jitter)
        if (IsOwner && targetHandler != null)
            targetHandler.UpdateAnchorImmediate(Time.deltaTime);

        Quaternion handRotOffset = Quaternion.Euler(kIKHandRotOffsetEuler);

        // Owner uses IKAnchor under PhoneTarget (inherits smoothing)
        if (IsOwner && targetHandler != null && targetHandler.IKAnchor != null)
        {
            Transform a = targetHandler.IKAnchor;
            animator.SetIKPosition(AvatarIKGoal.RightHand, a.position);
            animator.SetIKRotation(AvatarIKGoal.RightHand, a.rotation * handRotOffset);
        }
        else
        {
            // Remote fallback: approximate in front of head
            ResolveRemoteApproxTarget(out Vector3 pos, out Quaternion baseRot);
            animator.SetIKPosition(AvatarIKGoal.RightHand, pos);
            animator.SetIKRotation(AvatarIKGoal.RightHand, baseRot * handRotOffset);
        }

        if (rightElbowHint != null)
            animator.SetIKHintPosition(AvatarIKHint.RightElbow, rightElbowHint.position);
    }

    /// <summary>Called by InputManager (owner only). Press -> true, Release -> false.</summary>
    public void SetHolding(bool holding)
    {
        if (!IsOwner) return;
        _rmbHeld = holding;
    }

    /// <summary>Toggle flashlight (owner only). Phone raises automatically via Update().</summary>
    public void ToggleFlashlight()
    {
        if (!IsOwner) return;
        _flashOn.Value = !_flashOn.Value;
    }

    // ---- internal helpers ----

    private void OnFlashChanged(bool previous, bool current)
    {
        UpdateFlashlightActive();
    }

    private void EnsureFlashRef()
    {
        if (_flash != null || _spawnedPhone == null) return;

        // Preferred: named child
        var t = _spawnedPhone.transform.Find(flashlightNodeName);
        if (t != null) _flash = t.GetComponentInChildren<Light>(true);

        // Fallback: any Light under the phone
        if (_flash == null) _flash = _spawnedPhone.GetComponentInChildren<Light>(true);
    }

    private void UpdateFlashlightActive()
    {
        if (!_phoneVisible) return; // phone must be up to use the light
        EnsureFlashRef();
        if (_flash != null)
            _flash.enabled = _flashOn.Value;
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

            if (IsOwner)
            {
                if (_screenController == null)
                    _screenController = _spawnedPhone.GetComponent<PhoneScreenController>();
                _screenController?.SetScreenOn(_rmbHeld); // start as HUD if RMB held
            }

            EnsureFlashRef();
            UpdateFlashlightActive();
        }
    }

    private void HidePhone()
    {
        _phoneVisible = false;

        // Safety: ensure light is off when phone goes down
        if (_flash != null) _flash.enabled = false;

        if (_spawnedPhone != null)
            _spawnedPhone.SetActive(false);
    }

    private void ApplyPhoneOffsets()
    {
        if (_spawnedPhone == null || phoneAttachR == null) return;

        var t = _spawnedPhone.transform;
        t.localPosition = kPhoneLocalPosition;
        t.localRotation = Quaternion.Euler(kPhoneLocalEuler);
    }

    private void ResolveRemoteApproxTarget(out Vector3 pos, out Quaternion rot)
    {
        if (_headBone != null)
        {
            rot = _headBone.rotation * Quaternion.Euler(remoteRotOffsetEuler);
            pos = _headBone.position
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
}
