using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement; // <— add

public class PhoneTargetHandler : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private PlayerLook playerLook;

    private const float offsetDistance   = 0.2f;
    private const float horizontalOffset = 0.2f;
    private const float verticalOffset   = -0.06f;

    [Header("Smoothing & Clamp")]
    [SerializeField] private float rotationSmoothSpeed = 10f;
    [SerializeField] private float maxPitchUp = 45f;
    [SerializeField] private float maxPitchDown = -45f;

    [Header("IK Anchor (child of PhoneTarget)")]
    [SerializeField] private string ikAnchorName = "IKAnchor";

    private Transform phoneTarget;
    private Transform ikAnchor;
    private bool isPhoneActive = false;
    private Quaternion smoothedRotation;
    private bool _initialized;

    public Transform Target   => phoneTarget;
    public Transform IKAnchor => ikAnchor;
    public bool IsActive      => isPhoneActive;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsOwner) return;

        EnsurePhoneTarget();
        // listen so we can re-seed smoothing on scene changes (optional but nice)
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        // Re-seed smoothing next Update so we don't get a pop if the camera teleported.
        _initialized = false;
    }

    private void OnDestroy()
    {
        // Safety net in case OnNetworkDespawn didn't run (e.g., shutdown order).
        if (IsOwner && phoneTarget != null)
        {
            Destroy(phoneTarget.gameObject);
            phoneTarget = null;
            ikAnchor = null;
        }
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (phoneTarget != null)
        {
            Destroy(phoneTarget.gameObject); // clean up DDOL object when player despawns
            phoneTarget = null;
            ikAnchor = null;
        }
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    void Update()
    {
        if (!IsOwner) return;
        if (playerCamera == null || playerLook == null) return;

        // If a scene load destroyed it somehow, recreate.
        if (phoneTarget == null) EnsurePhoneTarget();

        if (!_initialized)
        {
            float initPitch = Mathf.Clamp(playerLook.Pitch, maxPitchDown, maxPitchUp);
            smoothedRotation = Quaternion.Euler(initPitch, playerCamera.transform.eulerAngles.y, 0f);
            _initialized = true;
        }

        float pitch = Mathf.Clamp(playerLook.Pitch, maxPitchDown, maxPitchUp);
        Quaternion targetRotation = Quaternion.Euler(pitch, playerCamera.transform.eulerAngles.y, 0f);
        smoothedRotation = Quaternion.Slerp(smoothedRotation, targetRotation, rotationSmoothSpeed * Time.deltaTime);

        Vector3 forwardOffset = smoothedRotation * Vector3.forward * offsetDistance;
        Vector3 rightOffset   = smoothedRotation * Vector3.right   * horizontalOffset;
        Vector3 upOffset      = smoothedRotation * Vector3.up      * verticalOffset;

        Vector3 targetPosition = playerCamera.transform.position + forwardOffset + rightOffset + upOffset;

        phoneTarget.position = targetPosition;
        phoneTarget.rotation = Quaternion.LookRotation(playerCamera.transform.position - phoneTarget.position);
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

        // keep at root (no body transform inheritance)
        phoneTarget.parent = null;

        // ✨ make it survive scene loads
        Object.DontDestroyOnLoad(targetObj);

        // match current visual state
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
            t.localRotation = Quaternion.identity;
        }
        ikAnchor = t;
    }
}
