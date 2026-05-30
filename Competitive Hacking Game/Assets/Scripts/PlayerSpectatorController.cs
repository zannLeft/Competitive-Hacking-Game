using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class PlayerSpectatorController : NetworkBehaviour, IPlayerRoundResettable
{
    [Header("References")]
    [SerializeField]
    private PlayerLook playerLook;

    [SerializeField]
    private InputManager inputManager;

    [SerializeField]
    private Camera spectatorCamera;

    [Header("Testing")]
    [Tooltip("Temporary local test toggle. Later the knockdown system should call EnterSpectatorModeForKnockdown().")]
    [SerializeField]
    private bool enableTestToggle = true;

    [SerializeField]
    private Key testToggleKey = Key.F6;

    [Tooltip("Temporary: lets one-player testing spectate yourself. Keep true until the real knockdown flow exists.")]
    [SerializeField]
    private bool includeSelfForTesting = true;

    [Tooltip("Temporary: lets test mode target bad guys too. Turn this off later.")]
    [SerializeField]
    private bool includeBadGuysForTesting = true;

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

    private readonly List<PlayerSetup> _targets = new List<PlayerSetup>();

    private PlayerSetup _currentTarget;
    private Transform _originalCameraParent;
    private Vector3 _originalCameraLocalPosition;
    private Quaternion _originalCameraLocalRotation;
    private float _originalCameraFOV;

    private bool _isSpectating;
    private bool _activeIncludeSelf;
    private bool _activeIncludeBadGuys;

    private float _orbitYaw;
    private float _orbitPitch;
    private Vector2 _smoothedLookDelta;

    private Vector3 _smoothedAnchor;
    private float _anchorYVelocity;

    public bool IsSpectating => _isSpectating;
    public PlayerSetup CurrentTarget => _currentTarget;

    private void Reset()
    {
        playerLook = GetComponent<PlayerLook>();
        inputManager = GetComponent<InputManager>();
    }

    private void Awake()
    {
        CacheReferences();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        CacheReferences();
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        if (enableTestToggle && Keyboard.current != null && Keyboard.current[testToggleKey].wasPressedThisFrame)
        {
            if (_isSpectating)
                ExitSpectatorMode();
            else
                EnterSpectatorMode(OwnerClientId, includeSelfForTesting, includeBadGuysForTesting);
        }

        if (!_isSpectating)
            return;

        HandleTargetSwitchInput();
        HandleOrbitLookInput();
        UpdateSpectatorCamera(snapAnchor: false);
    }

    public void EnterSpectatorModeForKnockdown()
    {
        // Future real knockdown entry point:
        // show the knocked player first, include self, and exclude the bad guy.
        EnterSpectatorMode(OwnerClientId, includeSelf: true, includeBadGuys: false);
    }

    public void EnterSpectatorMode(ulong preferredTargetClientId, bool includeSelf, bool includeBadGuys)
    {
        if (!IsOwner)
            return;

        CacheReferences();

        if (spectatorCamera == null)
        {
            Debug.LogWarning("[PlayerSpectatorController] No camera found for spectator mode.");
            return;
        }

        _activeIncludeSelf = includeSelf;
        _activeIncludeBadGuys = includeBadGuys;

        if (!RebuildTargets())
        {
            Debug.LogWarning("[PlayerSpectatorController] No valid spectator targets found.");
            return;
        }

        SaveCameraStateIfNeeded();
        InitializeOrbitFromCurrentCamera();

        inputManager?.SetGameplaySuppressed(true);

        if (playerLook != null)
        {
            playerLook.SetPhoneAim(false);
            playerLook.SetAimHeld(false);
            playerLook.enabled = false;
        }

        spectatorCamera.gameObject.SetActive(true);
        spectatorCamera.transform.SetParent(null, worldPositionStays: true);
        spectatorCamera.fieldOfView = playerLook != null ? playerLook.DefaultFOV : spectatorCamera.fieldOfView;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        _isSpectating = true;
        SelectPreferredTargetOrFallback(preferredTargetClientId);
        UpdateSpectatorCamera(snapAnchor: true);
    }

    public void ExitSpectatorMode()
    {
        if (!IsOwner || !_isSpectating)
            return;

        ForceExitSpectatorMode();
    }

    public void ResetForRound()
    {
        if (IsOwner && _isSpectating)
            ForceExitSpectatorMode();
    }

    public override void OnNetworkDespawn()
    {
        ForceExitSpectatorMode();
        base.OnNetworkDespawn();
    }

    private void OnDisable()
    {
        ForceExitSpectatorMode();
    }

    public override void OnDestroy()
    {
        ForceExitSpectatorMode();
        base.OnDestroy();
    }

    private void ForceExitSpectatorMode()
    {
        if (!_isSpectating)
            return;

        _isSpectating = false;
        _currentTarget = null;
        _targets.Clear();
        _smoothedLookDelta = Vector2.zero;
        _anchorYVelocity = 0f;

        RestoreCameraState();

        if (playerLook != null)
        {
            playerLook.SetPhoneAim(false);
            playerLook.SetAimHeld(false);
            playerLook.enabled = true;
        }

        inputManager?.ForceClearGameplaySuppression();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void RestoreCameraState()
    {
        if (spectatorCamera == null)
            return;

        spectatorCamera.transform.SetParent(_originalCameraParent, worldPositionStays: false);
        spectatorCamera.transform.localPosition = _originalCameraLocalPosition;
        spectatorCamera.transform.localRotation = _originalCameraLocalRotation;
        spectatorCamera.fieldOfView = _originalCameraFOV;
        spectatorCamera.gameObject.SetActive(true);
    }

    private void CacheReferences()
    {
        if (playerLook == null)
            playerLook = GetComponent<PlayerLook>();

        if (inputManager == null)
            inputManager = GetComponent<InputManager>();

        if (spectatorCamera == null && playerLook != null)
            spectatorCamera = playerLook.cam;

        if (spectatorCamera == null)
            spectatorCamera = GetComponentInChildren<Camera>(true);
    }

    private void SaveCameraStateIfNeeded()
    {
        if (spectatorCamera == null)
            return;

        _originalCameraParent = spectatorCamera.transform.parent;
        _originalCameraLocalPosition = spectatorCamera.transform.localPosition;
        _originalCameraLocalRotation = spectatorCamera.transform.localRotation;
        _originalCameraFOV = spectatorCamera.fieldOfView;
    }

    private void InitializeOrbitFromCurrentCamera()
    {
        if (spectatorCamera == null)
        {
            _orbitYaw = transform.eulerAngles.y;
            _orbitPitch = 15f;
            return;
        }

        Vector3 euler = spectatorCamera.transform.eulerAngles;
        _orbitYaw = euler.y;
        _orbitPitch = Mathf.Clamp(NormalizeAngle(euler.x), minPitch, maxPitch);
        _smoothedLookDelta = Vector2.zero;
    }

    private void HandleTargetSwitchInput()
    {
        if (Mouse.current == null)
            return;

        // LMB = previous, RMB = next. Swap the signs here if you prefer the opposite feel.
        if (Mouse.current.leftButton.wasPressedThisFrame)
            SwitchTarget(-1);

        if (Mouse.current.rightButton.wasPressedThisFrame)
            SwitchTarget(1);
    }

    private void HandleOrbitLookInput()
    {
        if (Mouse.current == null)
            return;

        float dt = Time.deltaTime;
        Vector2 rawDelta = Mouse.current.delta.ReadValue();

        bool useSmooth = playerLook != null ? playerLook.SmoothMouse : fallbackSmoothMouse;
        float smoothingTime = playerLook != null ? playerLook.MouseSmoothingTime : fallbackMouseSmoothingTime;

        if (useSmooth && smoothingTime > 0f)
        {
            float alpha = 1f - Mathf.Exp(-dt / smoothingTime);
            _smoothedLookDelta = Vector2.Lerp(_smoothedLookDelta, rawDelta, alpha);
        }
        else
        {
            _smoothedLookDelta = rawDelta;
        }

        float xSensitivity = playerLook != null ? playerLook.xSensitivity : fallbackXSensitivity;
        float ySensitivity = playerLook != null ? playerLook.ySensitivity : fallbackYSensitivity;

        _orbitYaw += _smoothedLookDelta.x * dt * xSensitivity;
        _orbitPitch += -_smoothedLookDelta.y * dt * ySensitivity;
        _orbitPitch = Mathf.Clamp(_orbitPitch, minPitch, maxPitch);
    }

    private void UpdateSpectatorCamera(bool snapAnchor)
    {
        if (spectatorCamera == null)
            return;

        if (!IsValidTarget(_currentTarget))
        {
            ulong previousTargetClientId = _currentTarget != null ? _currentTarget.OwnerClientId : OwnerClientId;

            if (!RebuildTargets())
            {
                Debug.LogWarning("[PlayerSpectatorController] No valid spectator targets remain. Exiting spectator mode.");
                ForceExitSpectatorMode();
                return;
            }

            SelectPreferredTargetOrFallback(previousTargetClientId);
            snapAnchor = true;
        }

        Vector3 targetAnchor = GetTargetAnchor(_currentTarget);

        if (snapAnchor)
        {
            _smoothedAnchor = targetAnchor;
            _anchorYVelocity = 0f;
        }
        else
        {
            _smoothedAnchor.x = targetAnchor.x;
            _smoothedAnchor.z = targetAnchor.z;
            _smoothedAnchor.y = Mathf.SmoothDamp(
                _smoothedAnchor.y,
                targetAnchor.y,
                ref _anchorYVelocity,
                Mathf.Max(0.0001f, anchorVerticalSmoothTime),
                Mathf.Infinity,
                Time.deltaTime
            );
        }

        Quaternion orbitRotation = Quaternion.Euler(_orbitPitch, _orbitYaw, 0f);
        Vector3 offset = orbitRotation * Vector3.back * Mathf.Max(0.05f, orbitDistance);
        Vector3 desiredCameraPosition = _smoothedAnchor + offset;
        Vector3 cameraPosition = ResolveCameraCollision(_smoothedAnchor, desiredCameraPosition);

        spectatorCamera.transform.position = cameraPosition;
        spectatorCamera.transform.rotation = Quaternion.LookRotation(
            _smoothedAnchor - cameraPosition,
            Vector3.up
        );
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

        // Ignore our own player/camera colliders and the currently spectated player's collider.
        if (hitTransform.IsChildOf(transform))
            return true;

        if (_currentTarget != null && hitTransform.IsChildOf(_currentTarget.transform))
            return true;

        // Also ignore other player colliders so switching targets does not make the camera pop inward.
        if (hit.collider.GetComponentInParent<PlayerSetup>() != null)
            return true;

        return false;
    }

    private bool RebuildTargets()
    {
        _targets.Clear();

        PlayerSetup[] allPlayers = UnityEngine.Object.FindObjectsByType<PlayerSetup>(FindObjectsInactive.Exclude);

        foreach (PlayerSetup player in allPlayers)
        {
            if (!IsValidTarget(player))
                continue;

            _targets.Add(player);
        }

        _targets.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
        return _targets.Count > 0;
    }

    private bool IsValidTarget(PlayerSetup player)
    {
        if (player == null || !player.IsSpawned)
            return false;

        bool isSelf = player.OwnerClientId == OwnerClientId;

        if (!_activeIncludeSelf && isSelf)
            return false;

        if (!_activeIncludeBadGuys && player.IsBadGuy.Value)
            return false;

        return true;
    }

    private void SelectPreferredTargetOrFallback(ulong preferredTargetClientId)
    {
        for (int i = 0; i < _targets.Count; i++)
        {
            if (_targets[i].OwnerClientId == preferredTargetClientId)
            {
                SelectTarget(_targets[i], snapAnchor: true);
                return;
            }
        }

        SelectTarget(_targets[0], snapAnchor: true);
    }

    private void SwitchTarget(int direction)
    {
        if (!RebuildTargets())
            return;

        int currentIndex = -1;

        if (_currentTarget != null)
        {
            ulong currentOwner = _currentTarget.OwnerClientId;
            for (int i = 0; i < _targets.Count; i++)
            {
                if (_targets[i].OwnerClientId == currentOwner)
                {
                    currentIndex = i;
                    break;
                }
            }
        }

        if (currentIndex < 0)
            currentIndex = 0;

        int nextIndex = Mod(currentIndex + direction, _targets.Count);
        SelectTarget(_targets[nextIndex], snapAnchor: true);
    }

    private void SelectTarget(PlayerSetup target, bool snapAnchor)
    {
        _currentTarget = target;

        if (snapAnchor && _currentTarget != null)
        {
            _smoothedAnchor = GetTargetAnchor(_currentTarget);
            _anchorYVelocity = 0f;
        }
    }

    private Vector3 GetTargetAnchor(PlayerSetup target)
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