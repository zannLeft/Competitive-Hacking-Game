using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class PhoneTargetHandler : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private PlayerLook playerLook;

    private const float offsetDistance   = 0.185f;
    private const float horizontalOffset = 0.2f;
    private const float verticalOffset   = -0.06f;

    [Header("Smoothing & Clamp")]
    [SerializeField] private float rotationSmoothSpeed = 10f;
    [SerializeField] private float maxPitchUp = 45f;
    [SerializeField] private float maxPitchDown = -45f;

    [Header("IK Anchor (child of PhoneTarget)")]
    [SerializeField] private string ikAnchorName = "IKAnchor";
    [SerializeField] private Vector3 ikAnchorLocalEuler = new Vector3(-12f, -160f, 0f); // <-- requested spawn rotation

    private Transform phoneTarget;
    private Transform ikAnchor;
    private bool isPhoneActive = false;
    private Quaternion smoothedRotation;
    private bool _initialized;
    private Transform _camT;

    public Transform Target   => phoneTarget;
    public Transform IKAnchor => ikAnchor;
    public bool IsActive      => isPhoneActive;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsOwner) return;

        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>(true);
        _camT = playerCamera ? playerCamera.transform : null;

        EnsurePhoneTarget();
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        _initialized = false; // reseed smoothing after teleports/loads
    }

    public override void OnDestroy()
    {
        base.OnDestroy(); // keep NetworkBehaviour cleanup
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
        if (!IsOwner || playerCamera == null || playerLook == null) return;

        if (phoneTarget == null) EnsurePhoneTarget();

        // keep it fresh even when IK isn't sampled (owner only)
        UpdateAnchorImmediate(Time.deltaTime);
    }

    /// <summary>
    /// Update the anchor NOW (call from OnAnimatorIK before sampling IK target).
    /// </summary>
    public void UpdateAnchorImmediate(float dt)
    {
        if (_camT == null || playerLook == null || phoneTarget == null) return;

        if (!_initialized)
        {
            float initPitch = Mathf.Clamp(playerLook.Pitch, maxPitchDown, maxPitchUp);
            smoothedRotation = Quaternion.Euler(initPitch, _camT.eulerAngles.y, 0f);
            _initialized = true;
        }

        float pitch = Mathf.Clamp(playerLook.Pitch, maxPitchDown, maxPitchUp);
        Quaternion targetRotation = Quaternion.Euler(pitch, _camT.eulerAngles.y, 0f);
        smoothedRotation = Quaternion.Slerp(smoothedRotation, targetRotation, rotationSmoothSpeed * dt);

        Vector3 forwardOffset = smoothedRotation * Vector3.forward * offsetDistance;
        Vector3 rightOffset   = smoothedRotation * Vector3.right   * horizontalOffset;
        Vector3 upOffset      = smoothedRotation * Vector3.up      * verticalOffset;

        Vector3 targetPosition = _camT.position + forwardOffset + rightOffset + upOffset;

        phoneTarget.position = targetPosition;

        // Restore original facing logic so palm orientation matches the old setup
        phoneTarget.rotation = smoothedRotation;
    }

    public void SetPhoneActive(bool value)
    {
        if (!IsOwner) return;

        if (value && phoneTarget == null)
            EnsurePhoneTarget();

        if (value == isPhoneActive) return;
        isPhoneActive = value;

        if (phoneTarget != null)
            phoneTarget.gameObject.SetActive(isPhoneActive);

        if (isPhoneActive && ikAnchor == null)
            CreateOrFindIKAnchor();
    }

    private void EnsurePhoneTarget()
    {
        if (phoneTarget != null) return;

        var name = $"PhoneTarget_{(NetworkManager ? NetworkManager.LocalClientId : 0)}";
        GameObject targetObj = new GameObject(name);
        phoneTarget = targetObj.transform;

        phoneTarget.parent = null;
        Object.DontDestroyOnLoad(targetObj);
        phoneTarget.gameObject.SetActive(isPhoneActive);

        CreateOrFindIKAnchor();
        _initialized = false;
    }

    private void CreateOrFindIKAnchor()
    {
        if (phoneTarget == null) return;

        var t = phoneTarget.Find(ikAnchorName);
        if (t == null)
        {
            var go = new GameObject(ikAnchorName);
            t = go.transform;
            t.SetParent(phoneTarget, worldPositionStays: false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.Euler(ikAnchorLocalEuler); // <-- apply spawn rotation
        }
        ikAnchor = t;
    }
}
