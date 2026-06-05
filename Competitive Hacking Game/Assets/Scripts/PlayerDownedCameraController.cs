using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerDownedCameraController : NetworkBehaviour, IPlayerRoundResettable
{
    [Header("References")]
    [SerializeField]
    private PlayerLifeState lifeState;

    [SerializeField]
    private PlayerLook playerLook;

    [SerializeField]
    private Camera playerCamera;

    [Header("Follow")]
    [SerializeField]
    private float positionFollowSpeed = 18f;

    [SerializeField]
    private float rotationFollowSpeed = 18f;

    [Tooltip("Locks camera roll to zero so the downed view does not tilt sideways.")]
    [SerializeField]
    private bool lockRollToHorizon = true;

    private DownedBodyObject currentBody;
    private Transform currentAnchor;

    private bool isDownedCameraActive;

    private Transform savedCameraParent;
    private Vector3 savedLocalPosition;
    private Quaternion savedLocalRotation;
    private Vector3 savedLocalScale;
    private float savedFieldOfView;
    private bool hasSavedCameraState;

    private void Reset()
    {
        CacheReferences();
    }

    private void Awake()
    {
        CacheReferences();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        CacheReferences();

        if (!IsOwner)
            return;

        if (lifeState != null)
        {
            lifeState.State.OnValueChanged += HandleLifeStateChanged;
            lifeState.CurrentBodyNetworkObjectId.OnValueChanged += HandleBodyIdChanged;
        }

        RefreshDownedCameraState();
    }

    public override void OnNetworkDespawn()
    {
        if (lifeState != null)
        {
            lifeState.State.OnValueChanged -= HandleLifeStateChanged;
            lifeState.CurrentBodyNetworkObjectId.OnValueChanged -= HandleBodyIdChanged;
        }

        ForceExitDownedCamera();

        base.OnNetworkDespawn();
    }

    private void OnDisable()
    {
        ForceExitDownedCamera();
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        if (lifeState == null)
            return;

        if (lifeState.IsDowned)
        {
            if (!isDownedCameraActive)
                TryEnterDownedCamera();

            if (isDownedCameraActive)
                FollowCurrentAnchor();

            return;
        }

        if (isDownedCameraActive)
            ExitDownedCamera();
    }

    private void CacheReferences()
    {
        if (lifeState == null)
            lifeState = GetComponent<PlayerLifeState>();

        if (playerLook == null)
            playerLook = GetComponent<PlayerLook>();

        if (playerCamera == null && playerLook != null)
            playerCamera = playerLook.cam;
    }

    private void HandleLifeStateChanged(
        PlayerLifeStateType previousState,
        PlayerLifeStateType newState
    )
    {
        RefreshDownedCameraState();
    }

    private void HandleBodyIdChanged(ulong previousBodyId, ulong newBodyId)
    {
        RefreshDownedCameraState();
    }

    private void RefreshDownedCameraState()
    {
        if (!IsOwner)
            return;

        if (lifeState == null)
            return;

        if (lifeState.IsDowned)
        {
            TryEnterDownedCamera();
            return;
        }

        if (isDownedCameraActive)
            ExitDownedCamera();
    }

    private bool TryEnterDownedCamera()
    {
        CacheReferences();

        if (playerCamera == null)
            return false;

        if (!TryResolveCurrentBody(out DownedBodyObject body))
            return false;

        currentBody = body;
        currentAnchor = body.CameraAnchor;

        if (currentAnchor == null)
            currentAnchor = body.transform;

        if (!isDownedCameraActive)
        {
            SaveCameraStateIfNeeded();

            if (playerLook != null)
            {
                playerLook.SetPhoneAim(false);
                playerLook.SetAimHeld(false);
                playerLook.enabled = false;
            }

            playerCamera.transform.SetParent(null, worldPositionStays: true);

            isDownedCameraActive = true;

            SnapToCurrentAnchor();
        }

        return true;
    }

    private bool TryResolveCurrentBody(out DownedBodyObject body)
    {
        body = null;

        if (lifeState == null)
            return false;

        ulong bodyNetworkObjectId = lifeState.CurrentBodyNetworkObjectId.Value;

        if (bodyNetworkObjectId == PlayerLifeState.NoBodyNetworkObjectId)
            return false;

        if (NetworkManager.Singleton == null)
            return false;

        if (
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                bodyNetworkObjectId,
                out NetworkObject bodyNetworkObject
            )
        )
            return false;

        body = bodyNetworkObject.GetComponent<DownedBodyObject>();
        return body != null;
    }

    private void SaveCameraStateIfNeeded()
    {
        if (hasSavedCameraState)
            return;

        Transform cameraTransform = playerCamera.transform;

        savedCameraParent = cameraTransform.parent;
        savedLocalPosition = cameraTransform.localPosition;
        savedLocalRotation = cameraTransform.localRotation;
        savedLocalScale = cameraTransform.localScale;
        savedFieldOfView = playerCamera.fieldOfView;

        hasSavedCameraState = true;
    }

    private void SnapToCurrentAnchor()
    {
        if (playerCamera == null || currentAnchor == null)
            return;

        playerCamera.transform.position = currentAnchor.position;
        playerCamera.transform.rotation = GetSafeAnchorRotation();
    }

    private void FollowCurrentAnchor()
    {
        if (playerCamera == null)
            return;

        if (currentAnchor == null)
        {
            if (!TryResolveCurrentBody(out DownedBodyObject body))
                return;

            currentBody = body;
            currentAnchor = body.CameraAnchor != null ? body.CameraAnchor : body.transform;
        }

        float dt = Time.deltaTime;

        float positionT = 1f - Mathf.Exp(-Mathf.Max(0.01f, positionFollowSpeed) * dt);
        float rotationT = 1f - Mathf.Exp(-Mathf.Max(0.01f, rotationFollowSpeed) * dt);

        Transform cameraTransform = playerCamera.transform;

        cameraTransform.position = Vector3.Lerp(
            cameraTransform.position,
            currentAnchor.position,
            positionT
        );

        cameraTransform.rotation = Quaternion.Slerp(
            cameraTransform.rotation,
            GetSafeAnchorRotation(),
            rotationT
        );
    }

    private Quaternion GetSafeAnchorRotation()
    {
        if (currentAnchor == null)
            return playerCamera != null ? playerCamera.transform.rotation : Quaternion.identity;

        Quaternion anchorRotation = currentAnchor.rotation;

        if (!lockRollToHorizon)
            return anchorRotation;

        Vector3 euler = anchorRotation.eulerAngles;
        return Quaternion.Euler(euler.x, euler.y, 0f);
    }

    private void ExitDownedCamera()
    {
        if (!isDownedCameraActive)
            return;

        RestoreCameraState();

        currentBody = null;
        currentAnchor = null;
        isDownedCameraActive = false;
    }

    private void ForceExitDownedCamera()
    {
        if (!isDownedCameraActive)
            return;

        RestoreCameraState();

        currentBody = null;
        currentAnchor = null;
        isDownedCameraActive = false;
    }

    private void RestoreCameraState()
    {
        if (playerCamera == null)
            return;

        if (hasSavedCameraState)
        {
            Transform cameraTransform = playerCamera.transform;

            cameraTransform.SetParent(savedCameraParent, worldPositionStays: false);
            cameraTransform.localPosition = savedLocalPosition;
            cameraTransform.localRotation = savedLocalRotation;
            cameraTransform.localScale = savedLocalScale;
            playerCamera.fieldOfView = savedFieldOfView;
        }

        hasSavedCameraState = false;

        if (playerLook != null && (lifeState == null || lifeState.IsAlive))
            playerLook.enabled = true;
    }

    public void ResetForRound()
    {
        ForceExitDownedCamera();
    }
}