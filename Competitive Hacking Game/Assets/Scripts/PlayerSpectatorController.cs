using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class PlayerSpectatorController : NetworkBehaviour, IPlayerRoundResettable
{
    private sealed class SpectatorTarget
    {
        public ulong ClientId;
        public PlayerSetup PlayerSetup;
        public PlayerLifeState LifeState;
    }

    [Header("References")]
    [SerializeField]
    private PlayerLifeState lifeState;

    [SerializeField]
    private PlayerLook playerLook;

    [SerializeField]
    private InputManager inputManager;

    [SerializeField]
    private PlayerDownedCameraController downedCameraController;

    [SerializeField]
    private Camera spectatorCamera;

    [Header("UI")]
    [Tooltip("Normal in-game HUD root. If left empty, this script searches for a scene object named GameUI.")]
    [SerializeField]
    private GameObject gameUIRoot;

    [Tooltip("Spectator-only UI root. If left empty, this script searches for a scene object named SpectatorUI.")]
    [SerializeField]
    private GameObject spectatorUIRoot;

    [SerializeField]
    private string gameUIRootName = "GameUI";

    [SerializeField]
    private string spectatorUIRootName = "SpectatorUI";

    [Header("Spectator UI Text")]
    [SerializeField]
    private TMP_Text spectatorTitleText;

    [SerializeField]
    private TMP_Text spectatorTargetText;

    [SerializeField]
    private TMP_Text spectatorControlsText;

    [SerializeField]
    private string spectatorTitleTextName = "SpectatorTitleText";

    [SerializeField]
    private string spectatorTargetTextName = "SpectatorTargetText";

    [SerializeField]
    private string spectatorControlsTextName = "SpectatorControlsText";

    [Header("Orbit")]
    [SerializeField]
    private float orbitDistance = 3.25f;

    [SerializeField]
    private float minPitch = -85f;

    [SerializeField]
    private float maxPitch = 85f;

    [Tooltip("Only the anchor Y is smoothed. X/Z follow instantly so switching and following feel tight.")]
    [SerializeField]
    private float anchorVerticalSmoothTime = 0.12f;

    [Header("Body Targets")]
    [Tooltip("Uses the body's hips-following ReviveAnchor instead of the head-following CameraAnchor. This keeps corpse spectating stable while the ragdoll settles.")]
    [SerializeField]
    private bool useReviveAnchorForBodies = true;

    [Tooltip("World-space height added to the selected body anchor so the orbit centers around the torso rather than the floor/hips.")]
    [SerializeField]
    private float bodyAnchorUpOffset = 0.35f;

    [Tooltip("Briefly keep the previous anchor while a body despawns or a revived player becomes Alive, avoiding a one-frame target jump.")]
    [Min(0f)]
    [SerializeField]
    private float missingTargetGraceSeconds = 0.35f;

    [Header("Camera Collision")]
    [Tooltip("Prevents the spectator camera from clipping through walls/solid level geometry.")]
    [SerializeField]
    private bool preventCameraClipping = true;

    [Tooltip("Layers that should block the spectator camera. Usually set this to your level/world geometry layers, not player layers.")]
    [SerializeField]
    private LayerMask cameraCollisionMask = ~0;

    [Tooltip("Sphere radius used when checking from the orbit anchor to the camera. Larger values keep the camera farther from walls.")]
    [SerializeField]
    private float cameraCollisionRadius = 0.2f;

    [Tooltip("Extra space kept between the camera and the hit surface.")]
    [SerializeField]
    private float cameraCollisionPadding = 0.08f;

    [Tooltip("Closest the camera is allowed to get to the orbit anchor when blocked by geometry.")]
    [SerializeField]
    private float minCameraDistanceFromAnchor = 0.45f;

    [Header("Fallback Look Settings")]
    [SerializeField]
    private float fallbackXSensitivity = 30f;

    [SerializeField]
    private float fallbackYSensitivity = 30f;

    [SerializeField]
    private bool fallbackSmoothMouse = true;

    [SerializeField]
    private float fallbackMouseSmoothingTime = 0.05f;

    private readonly List<SpectatorTarget> targets = new List<SpectatorTarget>();

    private SpectatorTarget currentTarget;

    private Transform originalCameraParent;
    private Vector3 originalCameraLocalPosition;
    private Quaternion originalCameraLocalRotation;
    private Vector3 originalCameraLocalScale;
    private float originalCameraFOV;
    private bool hasSavedCameraState;

    private bool isSpectating;
    private bool activeIncludeSelf;
    private bool activeIncludeBadGuys;
    private bool pendingOwnerStateRefresh;

    private float orbitYaw;
    private float orbitPitch;
    private Vector2 smoothedLookDelta;

    private Vector3 smoothedAnchor;
    private float anchorYVelocity;
    private bool hasAnchor;
    private float currentTargetMissingSince = -1f;

    private bool hasCachedUiState;
    private bool gameUIWasActive;
    private bool spectatorUIWasActive;
    private bool subscribedToInputManager;
    private bool subscribedToLifeState;

    private ulong lastUiTargetClientId = ulong.MaxValue;
    private PlayerLifeStateType lastUiTargetState;
    private bool hadUiTarget;

    public bool IsSpectating => isSpectating;
    public PlayerSetup CurrentTarget => currentTarget != null ? currentTarget.PlayerSetup : null;

    private void Reset()
    {
        CacheReferences();
    }

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        if (IsSpawned && IsOwner)
            pendingOwnerStateRefresh = true;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        CacheReferences();

        if (!IsOwner)
            return;

        SubscribeToInputManager();
        SubscribeToLifeState();
        pendingOwnerStateRefresh = true;
    }

    private void LateUpdate()
    {
        if (!IsOwner)
            return;

        if (pendingOwnerStateRefresh)
            RefreshOwnerSpectatorState();

        if (!isSpectating)
            return;

        HandleOrbitLookInput();
        UpdateSpectatorCamera(snapAnchor: false);
        RefreshSpectatorUiIfTargetChanged();
    }

    private void HandleOwnerLifeStateChanged(
        PlayerLifeStateType previousState,
        PlayerLifeStateType newState
    )
    {
        if (!IsOwner)
            return;

        if (newState == PlayerLifeStateType.Dead)
        {
            pendingOwnerStateRefresh = true;
            return;
        }

        pendingOwnerStateRefresh = false;

        if (isSpectating)
            ForceExitSpectatorMode();
    }

    private void RefreshOwnerSpectatorState()
    {
        pendingOwnerStateRefresh = false;
        CacheReferences();

        if (lifeState == null || !lifeState.ShouldBeInSpectatorMode)
        {
            if (isSpectating)
                ForceExitSpectatorMode();

            return;
        }

        if (!TryEnterSpectatorMode(OwnerClientId, includeSelf: true, includeBadGuys: false))
            pendingOwnerStateRefresh = true;
    }

    public void EnterSpectatorModeForDeath()
    {
        if (!IsOwner)
            return;

        if (lifeState != null && !lifeState.IsDead)
            return;

        TryEnterSpectatorMode(OwnerClientId, includeSelf: true, includeBadGuys: false);
    }

    // Kept as a compatibility alias for any existing UnityEvent or older call site.
    // It is deliberately guarded so Downed players cannot run both camera controllers.
    public void EnterSpectatorModeForKnockdown()
    {
        EnterSpectatorModeForDeath();
    }

    public void EnterSpectatorMode(
        ulong preferredTargetClientId,
        bool includeSelf,
        bool includeBadGuys
    )
    {
        if (!IsOwner)
            return;

        if (lifeState != null && !lifeState.IsDead)
            return;

        TryEnterSpectatorMode(preferredTargetClientId, includeSelf, includeBadGuys);
    }

    private bool TryEnterSpectatorMode(
        ulong preferredTargetClientId,
        bool includeSelf,
        bool includeBadGuys
    )
    {
        if (!IsOwner)
            return false;

        CacheReferences();

        if (spectatorCamera == null)
        {
            Debug.LogWarning("[PlayerSpectatorController] No camera found for spectator mode.");
            return false;
        }

        activeIncludeSelf = includeSelf;
        activeIncludeBadGuys = includeBadGuys;

        RebuildTargets();

        if (isSpectating)
        {
            TrySelectPreferredTargetOrFallback(preferredTargetClientId, snapAnchor: true);
            SetSpectatorUiVisible(true);
            UpdateSpectatorCamera(snapAnchor: true);
            return true;
        }

        if (!TrySelectPreferredTargetOrFallback(preferredTargetClientId, snapAnchor: true))
            return false;

        // The downed camera owns the same Camera while the player is Downed. Restore it
        // completely before spectator mode records its own normal camera state.
        downedCameraController?.PrepareForSpectatorMode();

        SaveCameraStateIfNeeded();
        InitializeOrbitFromCurrentCamera();

        inputManager?.SetGameplaySuppressed(true);
        inputManager?.SetSpectatorInputEnabled(true);

        if (playerLook != null)
        {
            playerLook.SetPhoneAim(false);
            playerLook.SetAimHeld(false);
            playerLook.enabled = false;
        }

        spectatorCamera.gameObject.SetActive(true);
        spectatorCamera.transform.SetParent(null, worldPositionStays: true);
        spectatorCamera.fieldOfView =
            playerLook != null ? playerLook.DefaultFOV : spectatorCamera.fieldOfView;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        isSpectating = true;

        SetSpectatorUiVisible(true);
        UpdateSpectatorCamera(snapAnchor: true);
        return true;
    }

    public void ExitSpectatorMode()
    {
        if (!IsOwner || !isSpectating)
            return;

        // Permanent death owns spectator mode. Normal gameplay may only be restored
        // after the life state leaves Dead (normally during round reset).
        if (lifeState != null && lifeState.IsDead)
            return;

        ForceExitSpectatorMode();
    }

    public void ResetForRound()
    {
        if (IsOwner)
            ForceExitSpectatorMode();
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeFromLifeState();
        UnsubscribeFromInputManager();
        ForceExitSpectatorMode();
        base.OnNetworkDespawn();
    }

    private void OnDisable()
    {
        ForceExitSpectatorMode();
    }

    public override void OnDestroy()
    {
        UnsubscribeFromLifeState();
        UnsubscribeFromInputManager();
        ForceExitSpectatorMode();
        base.OnDestroy();
    }

    private void ForceExitSpectatorMode()
    {
        if (!isSpectating)
            return;

        isSpectating = false;
        currentTarget = null;
        targets.Clear();
        smoothedLookDelta = Vector2.zero;
        anchorYVelocity = 0f;
        hasAnchor = false;
        currentTargetMissingSince = -1f;
        hadUiTarget = false;
        lastUiTargetClientId = ulong.MaxValue;

        RestoreSpectatorUiState();
        RestoreCameraState();

        inputManager?.SetSpectatorInputEnabled(false);

        bool restoreGameplay = lifeState == null || lifeState.IsAlive;

        if (playerLook != null)
        {
            playerLook.SetPhoneAim(false);
            playerLook.SetAimHeld(false);
            playerLook.enabled = restoreGameplay;
        }

        if (restoreGameplay)
            inputManager?.ForceClearGameplaySuppression();
        else
            inputManager?.SetGameplaySuppressed(true);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void SubscribeToLifeState()
    {
        if (subscribedToLifeState)
            return;

        CacheReferences();

        if (lifeState == null)
            return;

        lifeState.OnLifeStateChanged += HandleOwnerLifeStateChanged;
        subscribedToLifeState = true;
    }

    private void UnsubscribeFromLifeState()
    {
        if (!subscribedToLifeState)
            return;

        if (lifeState != null)
            lifeState.OnLifeStateChanged -= HandleOwnerLifeStateChanged;

        subscribedToLifeState = false;
    }

    private void SubscribeToInputManager()
    {
        if (subscribedToInputManager)
            return;

        CacheReferences();

        if (inputManager == null)
            return;

        inputManager.SpectatorPreviousTargetPressed += HandleSpectatorPreviousTargetPressed;
        inputManager.SpectatorNextTargetPressed += HandleSpectatorNextTargetPressed;
        subscribedToInputManager = true;
    }

    private void UnsubscribeFromInputManager()
    {
        if (!subscribedToInputManager)
            return;

        if (inputManager != null)
        {
            inputManager.SpectatorPreviousTargetPressed -= HandleSpectatorPreviousTargetPressed;
            inputManager.SpectatorNextTargetPressed -= HandleSpectatorNextTargetPressed;
        }

        subscribedToInputManager = false;
    }

    private void HandleSpectatorPreviousTargetPressed()
    {
        if (isSpectating)
            SwitchTarget(-1);
    }

    private void HandleSpectatorNextTargetPressed()
    {
        if (isSpectating)
            SwitchTarget(1);
    }

    private void RestoreCameraState()
    {
        if (spectatorCamera == null || !hasSavedCameraState)
            return;

        Transform cameraTransform = spectatorCamera.transform;
        cameraTransform.SetParent(originalCameraParent, worldPositionStays: false);
        cameraTransform.localPosition = originalCameraLocalPosition;
        cameraTransform.localRotation = originalCameraLocalRotation;
        cameraTransform.localScale = originalCameraLocalScale;
        spectatorCamera.fieldOfView = originalCameraFOV;
        spectatorCamera.gameObject.SetActive(true);

        hasSavedCameraState = false;
    }

    private void CacheReferences()
    {
        if (lifeState == null)
            lifeState = GetComponent<PlayerLifeState>();

        if (playerLook == null)
            playerLook = GetComponent<PlayerLook>();

        if (inputManager == null)
            inputManager = GetComponent<InputManager>();

        if (downedCameraController == null)
            downedCameraController = GetComponent<PlayerDownedCameraController>();

        if (spectatorCamera == null && playerLook != null)
            spectatorCamera = playerLook.cam;

        if (spectatorCamera == null)
            spectatorCamera = GetComponentInChildren<Camera>(true);

        CacheUiReferences();
    }

    private void CacheUiReferences()
    {
        if (gameUIRoot == null && !string.IsNullOrEmpty(gameUIRootName))
            gameUIRoot = FindSceneObjectByName(gameUIRootName);

        if (spectatorUIRoot == null && !string.IsNullOrEmpty(spectatorUIRootName))
            spectatorUIRoot = FindSceneObjectByName(spectatorUIRootName);

        CacheSpectatorTextReferences();
    }

    private void CacheSpectatorTextReferences()
    {
        if (spectatorUIRoot == null)
            return;

        if (spectatorTitleText == null)
            spectatorTitleText = FindTmpTextInChildren(spectatorUIRoot.transform, spectatorTitleTextName);

        if (spectatorTargetText == null)
            spectatorTargetText = FindTmpTextInChildren(spectatorUIRoot.transform, spectatorTargetTextName);

        if (spectatorControlsText == null)
            spectatorControlsText = FindTmpTextInChildren(spectatorUIRoot.transform, spectatorControlsTextName);
    }

    private static TMP_Text FindTmpTextInChildren(Transform root, string objectName)
    {
        if (root == null || string.IsNullOrEmpty(objectName))
            return null;

        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);

        foreach (TMP_Text text in texts)
        {
            if (text != null && text.name == objectName)
                return text;
        }

        return null;
    }

    private void SetSpectatorUiVisible(bool visible)
    {
        CacheUiReferences();

        if (!hasCachedUiState)
        {
            gameUIWasActive = gameUIRoot != null && gameUIRoot.activeSelf;
            spectatorUIWasActive = spectatorUIRoot != null && spectatorUIRoot.activeSelf;
            hasCachedUiState = true;
        }

        if (gameUIRoot != null)
            gameUIRoot.SetActive(!visible);

        if (spectatorUIRoot != null)
            spectatorUIRoot.SetActive(visible);

        if (visible)
            UpdateSpectatorUiText();
    }

    private void RestoreSpectatorUiState()
    {
        CacheUiReferences();

        if (!hasCachedUiState)
        {
            if (spectatorUIRoot != null)
                spectatorUIRoot.SetActive(false);

            return;
        }

        if (gameUIRoot != null)
            gameUIRoot.SetActive(gameUIWasActive);

        if (spectatorUIRoot != null)
            spectatorUIRoot.SetActive(spectatorUIWasActive);

        hasCachedUiState = false;
    }

    private void RefreshSpectatorUiIfTargetChanged()
    {
        bool hasCurrentTarget = currentTarget != null && IsValidTarget(currentTarget);
        ulong targetClientId = hasCurrentTarget ? currentTarget.ClientId : ulong.MaxValue;
        PlayerLifeStateType targetState = hasCurrentTarget
            ? currentTarget.LifeState.CurrentState
            : PlayerLifeStateType.Alive;

        if (
            hadUiTarget == hasCurrentTarget
            && lastUiTargetClientId == targetClientId
            && (!hasCurrentTarget || lastUiTargetState == targetState)
        )
            return;

        UpdateSpectatorUiText();
    }

    private void UpdateSpectatorUiText()
    {
        CacheSpectatorTextReferences();

        if (spectatorTitleText != null)
            spectatorTitleText.text = "SPECTATOR MODE";

        bool hasCurrentTarget = currentTarget != null && IsValidTarget(currentTarget);

        if (spectatorTargetText != null)
        {
            spectatorTargetText.text = hasCurrentTarget
                ? $"Spectating: {GetSpectatorTargetDisplayName(currentTarget)} | {GetTargetStateLabel(currentTarget)}"
                : "Spectating: Waiting for survivor target";
        }

        if (spectatorControlsText != null)
            spectatorControlsText.text = "Mouse: Orbit  |  LMB/Q: Previous  |  RMB/E: Next";

        hadUiTarget = hasCurrentTarget;
        lastUiTargetClientId = hasCurrentTarget ? currentTarget.ClientId : ulong.MaxValue;
        lastUiTargetState = hasCurrentTarget
            ? currentTarget.LifeState.CurrentState
            : PlayerLifeStateType.Alive;
    }

    private string GetSpectatorTargetDisplayName(SpectatorTarget target)
    {
        if (target == null)
            return "None";

        if (target.ClientId == OwnerClientId)
            return "You";

        return $"Player {target.ClientId}";
    }

    private static string GetTargetStateLabel(SpectatorTarget target)
    {
        if (target == null || target.LifeState == null)
            return "UNKNOWN";

        switch (target.LifeState.CurrentState)
        {
            case PlayerLifeStateType.Alive:
                return "ALIVE";

            case PlayerLifeStateType.Downed:
                return "DOWNED BODY";

            case PlayerLifeStateType.Dead:
                return "DEAD BODY";

            default:
                return "UNKNOWN";
        }
    }

    private static GameObject FindSceneObjectByName(string objectName)
    {
        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();

        foreach (GameObject obj in allObjects)
        {
            if (obj == null || obj.name != objectName)
                continue;

            Scene scene = obj.scene;

            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            return obj;
        }

        return null;
    }

    private void SaveCameraStateIfNeeded()
    {
        if (spectatorCamera == null || hasSavedCameraState)
            return;

        Transform cameraTransform = spectatorCamera.transform;

        originalCameraParent = cameraTransform.parent;
        originalCameraLocalPosition = cameraTransform.localPosition;
        originalCameraLocalRotation = cameraTransform.localRotation;
        originalCameraLocalScale = cameraTransform.localScale;
        originalCameraFOV = spectatorCamera.fieldOfView;
        hasSavedCameraState = true;
    }

    private void InitializeOrbitFromCurrentCamera()
    {
        if (spectatorCamera == null)
        {
            orbitYaw = transform.eulerAngles.y;
            orbitPitch = 15f;
            return;
        }

        Vector3 euler = spectatorCamera.transform.eulerAngles;
        orbitYaw = euler.y;
        orbitPitch = Mathf.Clamp(NormalizeAngle(euler.x), minPitch, maxPitch);
        smoothedLookDelta = Vector2.zero;
    }

    private void HandleOrbitLookInput()
    {
        float dt = Time.deltaTime;
        Vector2 rawDelta =
            inputManager != null ? inputManager.ReadSpectatorLookInput() : Vector2.zero;

        if (rawDelta == Vector2.zero && inputManager == null && Mouse.current != null)
            rawDelta = Mouse.current.delta.ReadValue();

        bool useSmooth = playerLook != null ? playerLook.SmoothMouse : fallbackSmoothMouse;
        float smoothingTime =
            playerLook != null ? playerLook.MouseSmoothingTime : fallbackMouseSmoothingTime;

        if (useSmooth && smoothingTime > 0f)
        {
            float alpha = 1f - Mathf.Exp(-dt / smoothingTime);
            smoothedLookDelta = Vector2.Lerp(smoothedLookDelta, rawDelta, alpha);
        }
        else
        {
            smoothedLookDelta = rawDelta;
        }

        float xSensitivity = playerLook != null ? playerLook.xSensitivity : fallbackXSensitivity;
        float ySensitivity = playerLook != null ? playerLook.ySensitivity : fallbackYSensitivity;

        orbitYaw += smoothedLookDelta.x * dt * xSensitivity;
        orbitPitch += -smoothedLookDelta.y * dt * ySensitivity;
        orbitPitch = Mathf.Clamp(orbitPitch, minPitch, maxPitch);
    }

    private void UpdateSpectatorCamera(bool snapAnchor)
    {
        if (spectatorCamera == null)
            return;

        if (!TryGetCurrentOrReplacementAnchor(out Vector3 targetAnchor, out bool hasFreshAnchor))
            return;

        if (snapAnchor || !hasAnchor)
        {
            smoothedAnchor = targetAnchor;
            anchorYVelocity = 0f;
            hasAnchor = true;
        }
        else if (hasFreshAnchor)
        {
            smoothedAnchor.x = targetAnchor.x;
            smoothedAnchor.z = targetAnchor.z;
            smoothedAnchor.y = Mathf.SmoothDamp(
                smoothedAnchor.y,
                targetAnchor.y,
                ref anchorYVelocity,
                Mathf.Max(0.0001f, anchorVerticalSmoothTime),
                Mathf.Infinity,
                Time.deltaTime
            );
        }

        Quaternion orbitRotation = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
        Vector3 offset = orbitRotation * Vector3.back * Mathf.Max(0.05f, orbitDistance);
        Vector3 desiredCameraPosition = smoothedAnchor + offset;
        Vector3 cameraPosition = ResolveCameraCollision(smoothedAnchor, desiredCameraPosition);

        spectatorCamera.transform.position = cameraPosition;

        Vector3 lookDirection = smoothedAnchor - cameraPosition;
        if (lookDirection.sqrMagnitude > 0.0001f)
            spectatorCamera.transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
    }

    private bool TryGetCurrentOrReplacementAnchor(
        out Vector3 targetAnchor,
        out bool hasFreshAnchor
    )
    {
        targetAnchor = smoothedAnchor;
        hasFreshAnchor = false;

        if (IsValidTarget(currentTarget) && TryGetTargetAnchor(currentTarget, out targetAnchor))
        {
            currentTargetMissingSince = -1f;
            hasFreshAnchor = true;
            return true;
        }

        if (IsValidTarget(currentTarget) && hasAnchor)
        {
            if (currentTargetMissingSince < 0f)
                currentTargetMissingSince = Time.unscaledTime;

            if (
                Time.unscaledTime - currentTargetMissingSince
                <= Mathf.Max(0f, missingTargetGraceSeconds)
            )
            {
                targetAnchor = smoothedAnchor;
                return true;
            }
        }

        ulong previousTargetClientId =
            currentTarget != null ? currentTarget.ClientId : OwnerClientId;

        RebuildTargets();

        if (TrySelectPreferredTargetOrFallback(previousTargetClientId, snapAnchor: true))
        {
            currentTargetMissingSince = -1f;
            hasFreshAnchor = TryGetTargetAnchor(currentTarget, out targetAnchor);
            return hasFreshAnchor || hasAnchor;
        }

        currentTarget = null;
        currentTargetMissingSince = -1f;
        UpdateSpectatorUiText();

        // A permanently dead player must remain in spectator mode even if every
        // target is temporarily unavailable during despawn/scene transitions.
        return hasAnchor;
    }

    private Vector3 ResolveCameraCollision(Vector3 anchor, Vector3 desiredCameraPosition)
    {
        if (!preventCameraClipping)
            return desiredCameraPosition;

        Vector3 anchorToCamera = desiredCameraPosition - anchor;
        float desiredDistance = anchorToCamera.magnitude;

        if (desiredDistance <= 0.001f)
            return desiredCameraPosition;

        Vector3 direction = anchorToCamera / desiredDistance;
        float closestBlockingDistance = desiredDistance;
        bool foundBlockingHit = false;

        RaycastHit[] hits = Physics.SphereCastAll(
            anchor,
            Mathf.Max(0.01f, cameraCollisionRadius),
            direction,
            desiredDistance,
            cameraCollisionMask,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];

            if (IsIgnoredCameraCollisionHit(hit))
                continue;

            if (hit.distance < closestBlockingDistance)
            {
                closestBlockingDistance = hit.distance;
                foundBlockingHit = true;
            }
        }

        if (!foundBlockingHit)
            return desiredCameraPosition;

        float resolvedDistance = Mathf.Clamp(
            closestBlockingDistance - Mathf.Max(0f, cameraCollisionPadding),
            Mathf.Max(0.05f, minCameraDistanceFromAnchor),
            desiredDistance
        );

        return anchor + direction * resolvedDistance;
    }

    private bool IsIgnoredCameraCollisionHit(RaycastHit hit)
    {
        if (hit.collider == null)
            return true;

        Transform hitTransform = hit.collider.transform;

        if (hitTransform.IsChildOf(transform))
            return true;

        if (
            currentTarget != null
            && currentTarget.PlayerSetup != null
            && hitTransform.IsChildOf(currentTarget.PlayerSetup.transform)
        )
            return true;

        if (hit.collider.GetComponentInParent<PlayerSetup>() != null)
            return true;

        DownedBodyObject hitBody = hit.collider.GetComponentInParent<DownedBodyObject>();
        if (
            hitBody != null
            && currentTarget != null
            && hitBody.IsForPlayer(currentTarget.ClientId)
        )
            return true;

        return false;
    }

    private void RebuildTargets()
    {
        targets.Clear();

        PlayerLifeState[] allLifeStates =
            UnityEngine.Object.FindObjectsByType<PlayerLifeState>(FindObjectsInactive.Exclude);

        foreach (PlayerLifeState targetLifeState in allLifeStates)
        {
            if (targetLifeState == null)
                continue;

            PlayerSetup targetPlayerSetup = targetLifeState.GetComponent<PlayerSetup>();
            if (targetPlayerSetup == null)
                continue;

            SpectatorTarget target = new SpectatorTarget
            {
                ClientId = targetPlayerSetup.OwnerClientId,
                PlayerSetup = targetPlayerSetup,
                LifeState = targetLifeState,
            };

            if (IsValidTarget(target))
                targets.Add(target);
        }

        targets.Sort((a, b) => a.ClientId.CompareTo(b.ClientId));
    }

    private bool IsValidTarget(SpectatorTarget target)
    {
        if (target == null || target.PlayerSetup == null || target.LifeState == null)
            return false;

        if (!target.PlayerSetup.IsSpawned || !target.LifeState.IsSpawned)
            return false;

        bool isSelf = target.ClientId == OwnerClientId;

        if (!activeIncludeSelf && isSelf)
            return false;

        if (!activeIncludeBadGuys && target.PlayerSetup.IsBadGuy.Value)
            return false;

        return true;
    }

    private bool TrySelectPreferredTargetOrFallback(
        ulong preferredTargetClientId,
        bool snapAnchor
    )
    {
        for (int i = 0; i < targets.Count; i++)
        {
            SpectatorTarget target = targets[i];

            if (target.ClientId != preferredTargetClientId)
                continue;

            if (TryGetTargetAnchor(target, out _))
                return SelectTarget(target, snapAnchor);
        }

        for (int i = 0; i < targets.Count; i++)
        {
            SpectatorTarget target = targets[i];

            if (TryGetTargetAnchor(target, out _))
                return SelectTarget(target, snapAnchor);
        }

        return false;
    }

    private void SwitchTarget(int direction)
    {
        RebuildTargets();

        if (targets.Count == 0)
            return;

        int currentIndex = -1;

        if (currentTarget != null)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].ClientId == currentTarget.ClientId)
                {
                    currentIndex = i;
                    break;
                }
            }
        }

        if (currentIndex < 0)
            currentIndex = direction >= 0 ? -1 : 0;

        for (int step = 1; step <= targets.Count; step++)
        {
            int nextIndex = Mod(currentIndex + direction * step, targets.Count);
            SpectatorTarget candidate = targets[nextIndex];

            if (!TryGetTargetAnchor(candidate, out _))
                continue;

            SelectTarget(candidate, snapAnchor: true);
            UpdateSpectatorCamera(snapAnchor: true);
            return;
        }
    }

    private bool SelectTarget(SpectatorTarget target, bool snapAnchor)
    {
        if (!IsValidTarget(target))
            return false;

        if (!TryGetTargetAnchor(target, out Vector3 targetAnchor))
            return false;

        currentTarget = target;
        currentTargetMissingSince = -1f;

        if (snapAnchor)
        {
            smoothedAnchor = targetAnchor;
            anchorYVelocity = 0f;
            hasAnchor = true;
        }

        if (isSpectating)
            UpdateSpectatorUiText();

        return true;
    }

    private bool TryGetTargetAnchor(SpectatorTarget target, out Vector3 anchor)
    {
        anchor = default;

        if (!IsValidTarget(target))
            return false;

        if (target.LifeState.IsAlive)
        {
            anchor = GetAliveTargetAnchor(target.PlayerSetup);
            return true;
        }

        if (!TryResolveBody(target, out DownedBodyObject body))
            return false;

        Transform bodyAnchor = useReviveAnchorForBodies ? body.ReviveAnchor : body.CameraAnchor;

        if (bodyAnchor == null)
            bodyAnchor = body.transform;

        anchor = bodyAnchor.position + Vector3.up * bodyAnchorUpOffset;
        return true;
    }

    private bool TryResolveBody(SpectatorTarget target, out DownedBodyObject body)
    {
        body = null;

        if (target == null || target.LifeState == null)
            return false;

        ulong bodyNetworkObjectId = target.LifeState.CurrentBodyNetworkObjectId.Value;

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

        return body != null
            && body.IsSpawned
            && body.IsForPlayer(target.ClientId);
    }

    private Vector3 GetAliveTargetAnchor(PlayerSetup target)
    {
        if (target == null)
            return transform.position + Vector3.up;

        PlayerMotor motor = target.GetComponent<PlayerMotor>();

        if (motor != null)
            return motor.GetColliderTopWorldPosition();

        CharacterController controller = target.GetComponent<CharacterController>();

        if (controller != null)
        {
            Vector3 localTop = controller.center + Vector3.up * (controller.height * 0.5f);
            return target.transform.TransformPoint(localTop);
        }

        return target.transform.position + Vector3.up;
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;

        if (angle > 180f)
            angle -= 360f;
        else if (angle < -180f)
            angle += 360f;

        return angle;
    }

    private static int Mod(int value, int length)
    {
        if (length <= 0)
            return 0;

        int result = value % length;
        return result < 0 ? result + length : result;
    }
}
