using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class PhoneTargetHandler : NetworkBehaviour
{
    [Header("References")]
    [SerializeField]
    private Camera playerCamera;

    [SerializeField]
    private PlayerLook playerLook;

    [Header("Offsets (camera/yaw space)")]
    [SerializeField]
    private float offsetDistance = 0.185f; // forward

    [SerializeField]
    private float horizontalOffset = 0.20f; // right

    [SerializeField]
    private float verticalOffset = -0.06f; // world up (negative = down)

    [Header("Rotation (what the phone faces)")]
    [SerializeField]
    private float rotationSmoothSpeed = 10f;

    [SerializeField]
    private float maxRotPitchUp = 75f;

    [SerializeField]
    private float maxRotPitchDown = -75f;

    [Header("Position pitch influence (prevents extreme stretch)")]
    [Range(0f, 1f)]
    [SerializeField]
    private float positionPitchInfluence = 0.35f;

    [SerializeField]
    private float maxPosPitchUp = 35f;

    [SerializeField]
    private float maxPosPitchDown = -35f;

    [Header("IK Anchor (child of PhoneTarget)")]
    [SerializeField]
    private string ikAnchorName = "IKAnchor";

    [SerializeField]
    private Vector3 ikAnchorLocalEuler = new Vector3(-12f, -160f, 0f);

    private Transform phoneTarget;
    private Transform ikAnchor;
    private bool isPhoneActive = false;

    private Quaternion smoothedRotation;
    private bool _initialized;
    private Transform _camT;

    public Transform Target => phoneTarget;
    public Transform IKAnchor => ikAnchor;
    public bool IsActive => isPhoneActive;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsOwner)
            return;

        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>(true);
        _camT = playerCamera ? playerCamera.transform : null;

        EnsurePhoneTarget();
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnActiveSceneChanged(Scene oldScene, Scene newScene) => _initialized = false;

    public override void OnDestroy()
    {
        base.OnDestroy();
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;

        if (IsOwner && phoneTarget != null)
        {
            Destroy(phoneTarget.gameObject);
            phoneTarget = null;
            ikAnchor = null;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;

        if (phoneTarget != null)
        {
            Destroy(phoneTarget.gameObject);
            phoneTarget = null;
            ikAnchor = null;
        }
    }

    void Update()
    {
        if (!IsOwner || playerLook == null || _camT == null)
            return;

        if (phoneTarget == null)
            EnsurePhoneTarget();

        UpdateAnchorImmediate(Time.deltaTime);
    }

    public void UpdateAnchorImmediate(float dt)
    {
        if (_camT == null || playerLook == null || phoneTarget == null)
            return;

        // --- Stable yaw, even at pitch = +/-90 ---
        // Prefer camera forward projected onto ground.
        Vector3 flatForward = _camT.forward;
        flatForward.y = 0f;

        // If looking straight up/down, projected forward collapses -> use right vector to rebuild forward.
        if (flatForward.sqrMagnitude < 1e-6f)
        {
            Vector3 flatRight = _camT.right;
            flatRight.y = 0f;
            if (flatRight.sqrMagnitude < 1e-6f)
                flatRight = transform.right;

            flatRight.Normalize();

            // ✅ IMPORTANT: right × up = forward (NOT up × right)
            flatForward = Vector3.Cross(flatRight, Vector3.up);
        }

        flatForward.Normalize();
        Quaternion yawRot = Quaternion.LookRotation(flatForward, Vector3.up);

        // --- Rotation: full pitch (clamped) ---
        float rawPitch = playerLook.Pitch;
        float rotPitch = Mathf.Clamp(rawPitch, maxRotPitchDown, maxRotPitchUp);
        Quaternion rotPitchQ = Quaternion.AngleAxis(rotPitch, yawRot * Vector3.right);
        Quaternion targetRotation = rotPitchQ * yawRot;

        if (!_initialized)
        {
            smoothedRotation = targetRotation;
            _initialized = true;
        }

        smoothedRotation = Quaternion.Slerp(
            smoothedRotation,
            targetRotation,
            rotationSmoothSpeed * dt
        );

        // --- Position: reduced/clamped pitch influence ---
        float posPitch =
            Mathf.Clamp(rawPitch, maxPosPitchDown, maxPosPitchUp) * positionPitchInfluence;
        Quaternion posPitchQ = Quaternion.AngleAxis(posPitch, yawRot * Vector3.right);
        Quaternion posRot = posPitchQ * yawRot;

        Vector3 forwardOffset = (posRot * Vector3.forward) * offsetDistance;
        Vector3 rightOffset = (yawRot * Vector3.right) * horizontalOffset;
        Vector3 upOffset = Vector3.up * verticalOffset;

        phoneTarget.position = _camT.position + forwardOffset + rightOffset + upOffset;
        phoneTarget.rotation = smoothedRotation;
    }

    public void SetPhoneActive(bool value)
    {
        if (!IsOwner)
            return;

        if (value && phoneTarget == null)
            EnsurePhoneTarget();

        if (value == isPhoneActive)
            return;
        isPhoneActive = value;

        if (phoneTarget != null)
            phoneTarget.gameObject.SetActive(isPhoneActive);

        if (isPhoneActive && ikAnchor == null)
            CreateOrFindIKAnchor();
    }

    private void EnsurePhoneTarget()
    {
        if (phoneTarget != null)
            return;

        var name = $"PhoneTarget_{(NetworkManager ? NetworkManager.LocalClientId : 0)}";
        GameObject targetObj = new GameObject(name);
        phoneTarget = targetObj.transform;

        phoneTarget.parent = null;
        DontDestroyOnLoad(targetObj);
        phoneTarget.gameObject.SetActive(isPhoneActive);

        CreateOrFindIKAnchor();
        _initialized = false;
    }

    private void CreateOrFindIKAnchor()
    {
        if (phoneTarget == null)
            return;

        var t = phoneTarget.Find(ikAnchorName);
        if (t == null)
        {
            var go = new GameObject(ikAnchorName);
            t = go.transform;
            t.SetParent(phoneTarget, worldPositionStays: false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.Euler(ikAnchorLocalEuler);
        }
        ikAnchor = t;
    }
}
