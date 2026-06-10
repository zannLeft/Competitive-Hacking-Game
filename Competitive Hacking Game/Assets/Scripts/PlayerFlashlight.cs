using Unity.Netcode;
using UnityEngine;

[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public class PlayerFlashlight
    : NetworkBehaviour,
        IPlayerRoundResettable,
        IPlayerRoundServerResettable
{
    [Header("References")]
    [SerializeField]
    private PlayerLifeState lifeState;

    [SerializeField]
    private PlayerLook playerLook;

    [Tooltip("Stable rig under the player root. FlashlightLight should be a child of this.")]
    [SerializeField]
    private Transform flashlightStableRig;

    [Tooltip("Use the pod/lens point on the chest to cache the intended local X/Z offset, and to live-follow Y for crouching.")]
    [SerializeField]
    private Transform flashlightOrigin;

    [Tooltip("Assign PlayerCamera here.")]
    [SerializeField]
    private Transform aimSource;

    [SerializeField]
    private Light flashlightLight;

    [Tooltip("Optional. Assign Lens Flare (SRP) from FlashlightLight or FlashlightStableRig.")]
    [SerializeField]
    private Behaviour podLensFlare;

    [Header("Pod Emission")]
    [Tooltip("Assign the TacticalPodMesh renderer here.")]
    [SerializeField]
    private Renderer lightBulbsRenderer;

    [Tooltip("Material slot index for the LightBulbs material. In your screenshot this is Element 1.")]
    [SerializeField]
    private int lightBulbsMaterialIndex = 1;

    [Tooltip("Material used when the flashlight is off.")]
    [SerializeField]
    private Material lightBulbsOffMaterial;

    [Tooltip("Material used when the flashlight is on. This should have emission enabled.")]
    [SerializeField]
    private Material lightBulbsOnMaterial;

    [Header("Tuning")]
    [Tooltip("Rotation smoothing only. Higher = smoother/slower. Lower = snappier. Try 0.08 to 0.12.")]
    [SerializeField]
    private float smoothing = 0.10f;

    private const float MaxUpAngleDegrees = 89f;
    private const float AimSendRate = 20f;
    private const float AimSendAngleThreshold = 0.5f;

    private readonly NetworkVariable<bool> isFlashlightOn = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<Vector3> syncedAimForward = new NetworkVariable<Vector3>(
        Vector3.forward,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Vector3 virtualOriginLocalOffset;
    private bool hasCachedVirtualOriginOffset;

    private Quaternion smoothedWorldRotation = Quaternion.identity;
    private bool hasSmoothedWorldRotation;

    private Vector3 lastStableHorizontalForward = Vector3.forward;
    private Vector3 lastSentAimForward;
    private float nextAimSendTime;

    public bool IsFlashlightOn => isFlashlightOn.Value;

    public bool CanUseFlashlight => lifeState == null || lifeState.IsAlive;

    private void Reset()
    {
        CacheReferences();
    }

    private void Awake()
    {
        CacheReferences();
        CacheDefaultLightBulbMaterialIfNeeded();
        InitializeStableHorizontalForward();
        ApplyFlashlightVisuals(false);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        CacheReferences();
        CacheDefaultLightBulbMaterialIfNeeded();
        InitializeStableHorizontalForward();

        isFlashlightOn.OnValueChanged += HandleFlashlightStateChanged;

        if (lifeState != null)
            lifeState.OnLifeStateChanged += HandleLifeStateChanged;

        hasCachedVirtualOriginOffset = false;
        hasSmoothedWorldRotation = false;

        ApplyFlashlightVisuals(isFlashlightOn.Value);
        UpdateStableRig(true);

        if (IsOwner)
            TrySendAimForward(true);

        if (IsServer && !CanUseFlashlight && isFlashlightOn.Value)
            SetFlashlightOnServer(false);
    }

    public override void OnNetworkDespawn()
    {
        isFlashlightOn.OnValueChanged -= HandleFlashlightStateChanged;

        if (lifeState != null)
            lifeState.OnLifeStateChanged -= HandleLifeStateChanged;

        base.OnNetworkDespawn();
    }

    private void LateUpdate()
    {
        UpdateStableRig(false);

        if (IsOwner && isFlashlightOn.Value)
            TrySendAimForward(false);
    }

    private void CacheReferences()
    {
        if (lifeState == null)
            lifeState = GetComponent<PlayerLifeState>();

        if (playerLook == null)
            playerLook = GetComponent<PlayerLook>();

        if (flashlightLight == null)
            flashlightLight = GetComponentInChildren<Light>(true);

        if (flashlightStableRig == null && flashlightLight != null)
            flashlightStableRig = flashlightLight.transform.parent;
    }

    private void CacheDefaultLightBulbMaterialIfNeeded()
    {
        if (lightBulbsOffMaterial != null)
            return;

        if (lightBulbsRenderer == null)
            return;

        Material[] materials = lightBulbsRenderer.sharedMaterials;

        if (lightBulbsMaterialIndex < 0 || lightBulbsMaterialIndex >= materials.Length)
            return;

        lightBulbsOffMaterial = materials[lightBulbsMaterialIndex];
    }

    private void InitializeStableHorizontalForward()
    {
        Vector3 forward = aimSource != null ? aimSource.forward : transform.forward;
        Vector3 horizontalForward = Vector3.ProjectOnPlane(forward, Vector3.up);

        if (IsValidDirection(horizontalForward))
        {
            lastStableHorizontalForward = horizontalForward.normalized;
            return;
        }

        horizontalForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);

        if (IsValidDirection(horizontalForward))
            lastStableHorizontalForward = horizontalForward.normalized;
        else
            lastStableHorizontalForward = Vector3.forward;
    }

    private void EnsureVirtualOriginOffsetCached()
    {
        if (hasCachedVirtualOriginOffset)
            return;

        if (flashlightOrigin != null)
        {
            virtualOriginLocalOffset = transform.InverseTransformPoint(flashlightOrigin.position);
        }
        else if (flashlightStableRig != null)
        {
            virtualOriginLocalOffset = transform.InverseTransformPoint(flashlightStableRig.position);
        }
        else
        {
            virtualOriginLocalOffset = Vector3.zero;
        }

        hasCachedVirtualOriginOffset = true;
    }

    private void UpdateStableRig(bool snap)
    {
        if (flashlightStableRig == null)
            return;

        EnsureVirtualOriginOffsetCached();

        Vector3 targetPosition = GetVirtualFlashlightOriginPosition();
        Quaternion targetRotation = GetTargetAimRotation();

        if (snap || !hasSmoothedWorldRotation || smoothing <= 0f)
        {
            smoothedWorldRotation = targetRotation;
            hasSmoothedWorldRotation = true;

            ApplyStableRigWorldTransform(targetPosition);
            return;
        }

        float rotationSpeed = 1f / Mathf.Max(0.001f, smoothing);
        float t = 1f - Mathf.Exp(-rotationSpeed * Time.deltaTime);

        smoothedWorldRotation = Quaternion.Slerp(
            smoothedWorldRotation,
            targetRotation,
            t
        );

        ApplyStableRigWorldTransform(targetPosition);
    }

    private void ApplyStableRigWorldTransform(Vector3 worldPosition)
    {
        flashlightStableRig.SetPositionAndRotation(
            worldPosition,
            smoothedWorldRotation
        );
    }

    private Vector3 GetVirtualFlashlightOriginPosition()
    {
        Quaternion stableYawRotation = GetStableYawRotationForVirtualOrigin();

        Vector3 localOffset = virtualOriginLocalOffset;

        // Keep cached X/Z to avoid body catch-up jitter,
        // but use live Y so crouching lowers/raises the flashlight.
        if (flashlightOrigin != null)
        {
            Vector3 liveLocalOffset = transform.InverseTransformPoint(flashlightOrigin.position);
            localOffset.y = liveLocalOffset.y;
        }

        return transform.position + stableYawRotation * localOffset;
    }

    private Quaternion GetStableYawRotationForVirtualOrigin()
    {
        if (IsOwner && playerLook != null)
            return ExtractYawRotation(playerLook.MoveSpaceRotation);

        return ExtractYawRotation(transform.rotation);
    }

    private Quaternion GetTargetAimRotation()
    {
        Vector3 targetForward = GetTargetAimForward();

        if (!IsValidDirection(targetForward))
            targetForward = GetFallbackHorizontalForward();

        return Quaternion.LookRotation(targetForward.normalized, Vector3.up);
    }

    private Vector3 GetTargetAimForward()
    {
        if (IsOwner)
            return GetOwnerClampedAimForward();

        if (IsValidDirection(syncedAimForward.Value))
            return ClampForwardAboveHorizon(syncedAimForward.Value.normalized);

        return ClampForwardAboveHorizon(transform.forward);
    }

    private Vector3 GetOwnerClampedAimForward()
    {
        Transform source = aimSource != null ? aimSource : transform;

        Vector3 forward = source.forward;

        if (!IsValidDirection(forward))
            forward = transform.forward;

        return ClampForwardAboveHorizon(forward, source);
    }

    private void TrySendAimForward(bool force)
    {
        if (!IsOwner)
            return;

        if (!IsSpawned)
            return;

        Vector3 aimForward = GetOwnerClampedAimForward();

        if (!force)
        {
            float interval = 1f / AimSendRate;

            if (Time.time < nextAimSendTime)
                return;

            nextAimSendTime = Time.time + interval;

            if (
                IsValidDirection(lastSentAimForward)
                && Vector3.Angle(lastSentAimForward, aimForward) < AimSendAngleThreshold
            )
            {
                return;
            }
        }

        lastSentAimForward = aimForward;

        if (IsServer)
        {
            SetSyncedAimForwardServer(aimForward);
            return;
        }

        SubmitAimForwardServerRpc(aimForward);
    }

    [ServerRpc]
    private void SubmitAimForwardServerRpc(
        Vector3 aimForward,
        ServerRpcParams serverRpcParams = default
    )
    {
        if (serverRpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!CanUseFlashlight)
            return;

        SetSyncedAimForwardServer(aimForward);
    }

    private void SetSyncedAimForwardServer(Vector3 aimForward)
    {
        if (!IsServer)
            return;

        Vector3 clampedAimForward = ClampForwardAboveHorizon(aimForward);

        if (!IsValidDirection(clampedAimForward))
            return;

        syncedAimForward.Value = clampedAimForward.normalized;
    }

    public void TryToggleFlashlight()
    {
        if (!IsOwner)
            return;

        if (!CanUseFlashlight)
            return;

        bool newState = !isFlashlightOn.Value;

        if (newState)
            TrySendAimForward(true);

        RequestSetFlashlightServerRpc(newState);
    }

    public void TrySetFlashlight(bool isOn)
    {
        if (!IsOwner)
            return;

        if (isOn && !CanUseFlashlight)
            return;

        if (isOn)
            TrySendAimForward(true);

        RequestSetFlashlightServerRpc(isOn);
    }

    [ServerRpc]
    private void RequestSetFlashlightServerRpc(
        bool isOn,
        ServerRpcParams serverRpcParams = default
    )
    {
        if (serverRpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (isOn && !CanUseFlashlight)
            return;

        SetFlashlightOnServer(isOn);
    }

    private void SetFlashlightOnServer(bool isOn)
    {
        if (!IsServer)
            return;

        isFlashlightOn.Value = isOn;
    }

    private void HandleFlashlightStateChanged(bool previousValue, bool newValue)
    {
        ApplyFlashlightVisuals(newValue);

        if (IsOwner && newValue)
            TrySendAimForward(true);

        UpdateStableRig(true);
    }

    private void HandleLifeStateChanged(
        PlayerLifeStateType previousState,
        PlayerLifeStateType newState
    )
    {
        if (newState == PlayerLifeStateType.Alive)
            return;

        if (IsServer)
        {
            SetFlashlightOnServer(false);
            return;
        }

        if (IsOwner)
            RequestSetFlashlightServerRpc(false);
    }

    private void ApplyFlashlightVisuals(bool isOn)
    {
        if (flashlightLight != null)
            flashlightLight.enabled = isOn;

        if (podLensFlare != null)
            podLensFlare.enabled = isOn;

        ApplyLightBulbsMaterial(isOn);
    }

    private void ApplyLightBulbsMaterial(bool isOn)
    {
        if (lightBulbsRenderer == null)
            return;

        if (lightBulbsMaterialIndex < 0)
            return;

        Material materialToUse = isOn ? lightBulbsOnMaterial : lightBulbsOffMaterial;

        if (materialToUse == null)
            return;

        Material[] materials = lightBulbsRenderer.sharedMaterials;

        if (lightBulbsMaterialIndex >= materials.Length)
            return;

        if (materials[lightBulbsMaterialIndex] == materialToUse)
            return;

        materials[lightBulbsMaterialIndex] = materialToUse;
        lightBulbsRenderer.sharedMaterials = materials;
    }

    public void ServerResetForRound()
    {
        if (!IsServer)
            return;

        SetFlashlightOnServer(false);
    }

    public void ResetForRound()
    {
        ApplyFlashlightVisuals(false);

        hasCachedVirtualOriginOffset = false;
        hasSmoothedWorldRotation = false;
        InitializeStableHorizontalForward();

        UpdateStableRig(true);
    }

    private Vector3 ClampForwardAboveHorizon(Vector3 forward, Transform verticalFallbackSource = null)
    {
        if (!IsValidDirection(forward))
            return GetFallbackHorizontalForward();

        forward.Normalize();

        Vector3 horizontalForward = Vector3.ProjectOnPlane(forward, Vector3.up);

        if (!IsValidDirection(horizontalForward) && verticalFallbackSource != null)
        {
            // When the camera is perfectly vertical, forward no longer contains useful yaw.
            // Use camera up/down to recover the yaw direction and avoid a sudden spin.
            Vector3 alternateHorizontal =
                forward.y >= 0f
                    ? -verticalFallbackSource.up
                    : verticalFallbackSource.up;

            horizontalForward = Vector3.ProjectOnPlane(alternateHorizontal, Vector3.up);
        }

        if (IsValidDirection(horizontalForward))
        {
            horizontalForward.Normalize();
            lastStableHorizontalForward = horizontalForward;
        }
        else
        {
            horizontalForward = GetFallbackHorizontalForward();
        }

        float maxUpY = Mathf.Sin(MaxUpAngleDegrees * Mathf.Deg2Rad);
        float clampedY = Mathf.Clamp(forward.y, 0f, maxUpY);

        float horizontalMagnitude = Mathf.Sqrt(Mathf.Max(0f, 1f - clampedY * clampedY));

        Vector3 clampedForward =
            horizontalForward.normalized * horizontalMagnitude
            + Vector3.up * clampedY;

        if (!IsValidDirection(clampedForward))
            return GetFallbackHorizontalForward();

        return clampedForward.normalized;
    }

    private Vector3 GetFallbackHorizontalForward()
    {
        if (IsValidDirection(lastStableHorizontalForward))
            return lastStableHorizontalForward.normalized;

        Vector3 transformHorizontal = Vector3.ProjectOnPlane(transform.forward, Vector3.up);

        if (IsValidDirection(transformHorizontal))
            return transformHorizontal.normalized;

        return Vector3.forward;
    }

    private static Quaternion ExtractYawRotation(Quaternion rotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);

        if (!IsValidDirection(forward))
            return Quaternion.identity;

        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    private static bool IsValidDirection(Vector3 direction)
    {
        return direction.sqrMagnitude > 0.0001f && IsValidVector(direction);
    }

    private static bool IsValidVector(Vector3 value)
    {
        return !float.IsNaN(value.x)
            && !float.IsNaN(value.y)
            && !float.IsNaN(value.z)
            && !float.IsInfinity(value.x)
            && !float.IsInfinity(value.y)
            && !float.IsInfinity(value.z);
    }
}