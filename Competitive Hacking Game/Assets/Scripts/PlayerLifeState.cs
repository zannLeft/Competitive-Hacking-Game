using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public enum PlayerLifeStateType : byte
{
    Alive = 0,
    Downed = 1,
    Dead = 2,
}

[DisallowMultipleComponent]
public class PlayerLifeState
    : NetworkBehaviour,
        IPlayerRoundResettable,
        IPlayerRoundServerResettable
{
    public const ulong NoAttackerClientId = ulong.MaxValue;
    public const ulong NoBodyNetworkObjectId = ulong.MaxValue;

    [Header("References")]
    [SerializeField]
    private PlayerSetup playerSetup;

    [SerializeField]
    private InputManager inputManager;

    [SerializeField]
    private PlayerPhone phone;

    [SerializeField]
    private PlayerLaptopHacker laptopHacker;

    [SerializeField]
    private PlayerSitAction sitAction;

    [SerializeField]
    private PlayerLaptopVisual laptopVisual;

    [SerializeField]
    private PlayerLook playerLook;

    [SerializeField]
    private PlayerBodyVisibility bodyVisibility;

    [SerializeField]
    private PlayerMotor playerMotor;

    [SerializeField]
    private PlayerFlashlight playerFlashlight;

    [Header("Downed Body")]
    [Tooltip("Network prefab spawned when this player becomes Downed. Add DownedBodyObject + NetworkObject to the prefab and register it in NetworkManager Network Prefabs.")]
    [SerializeField]
    private DownedBodyObject downedBodyPrefab;

    [Tooltip("Small upward offset for spawning the placeholder body so it does not start slightly under the floor.")]
    [SerializeField]
    private float downedBodySpawnUpOffset = 0.05f;

    [Header("Debug Testing")]
    [Tooltip("Optional temporary test keys. Leave OFF unless you want to test life states manually.")]
    [SerializeField]
    private bool enableDebugHotkeys = false;

    public NetworkVariable<PlayerLifeStateType> State = new NetworkVariable<PlayerLifeStateType>(
        PlayerLifeStateType.Alive,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<Vector3> DownedPosition = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<ulong> LastAttackerClientId = new NetworkVariable<ulong>(
        NoAttackerClientId,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<double> DownedStartedServerTime = new NetworkVariable<double>(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<ulong> CurrentBodyNetworkObjectId = new NetworkVariable<ulong>(
        NoBodyNetworkObjectId,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public event Action<PlayerLifeStateType, PlayerLifeStateType> OnLifeStateChanged;

    private DownedBodyObject currentDownedBody;

    public PlayerLifeStateType CurrentState => State.Value;

    public bool IsAlive => State.Value == PlayerLifeStateType.Alive;
    public bool IsDowned => State.Value == PlayerLifeStateType.Downed;
    public bool IsDead => State.Value == PlayerLifeStateType.Dead;

    public bool IsBadGuy => playerSetup != null && playerSetup.IsBadGuy.Value;

    public bool CanMove => IsAlive;

    public bool CanUseSurvivorTools => IsAlive && !IsBadGuy;

    public bool CanAttackSurvivors => IsAlive && IsBadGuy;

    public bool CanBeAttacked => IsAlive && !IsBadGuy;

    public bool CanBeRevived => IsDowned && !IsBadGuy;

    public bool ShouldBeInSpectatorMode => IsDead;

    public bool ShouldSuppressGameplayInput => IsDowned || IsDead;

    public bool HasBody => CurrentBodyNetworkObjectId.Value != NoBodyNetworkObjectId;

    public double SecondsSinceDowned
    {
        get
        {
            if (!IsDowned)
                return 0d;

            double now = GetNetworkTime();
            return Math.Max(0d, now - DownedStartedServerTime.Value);
        }
    }

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

        State.OnValueChanged += HandleLifeStateChanged;

        ApplyLifeStateSideEffects(State.Value, State.Value, force: true);
    }

    public override void OnNetworkDespawn()
    {
        State.OnValueChanged -= HandleLifeStateChanged;

        if (IsServer)
            ServerDespawnDownedBody();

        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!enableDebugHotkeys)
            return;

        if (!IsOwner)
            return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.f7Key.wasPressedThisFrame)
            DebugRequestSetStateServerRpc(PlayerLifeStateType.Downed, transform.position);

        if (keyboard.f8Key.wasPressedThisFrame)
            DebugRequestSetStateServerRpc(PlayerLifeStateType.Dead, transform.position);

        if (keyboard.f9Key.wasPressedThisFrame)
            DebugRequestSetStateServerRpc(PlayerLifeStateType.Alive, transform.position);
    }

    private void CacheReferences()
    {
        if (playerSetup == null)
            playerSetup = GetComponent<PlayerSetup>();

        if (inputManager == null)
            inputManager = GetComponent<InputManager>();

        if (phone == null)
            phone = GetComponent<PlayerPhone>();

        if (laptopHacker == null)
            laptopHacker = GetComponent<PlayerLaptopHacker>();

        if (sitAction == null)
            sitAction = GetComponent<PlayerSitAction>();

        if (laptopVisual == null)
            laptopVisual = GetComponent<PlayerLaptopVisual>();

        if (playerLook == null)
            playerLook = GetComponent<PlayerLook>();

        if (bodyVisibility == null)
            bodyVisibility = GetComponent<PlayerBodyVisibility>();

        if (playerMotor == null)
            playerMotor = GetComponent<PlayerMotor>();

        if (playerFlashlight == null)
            playerFlashlight = GetComponent<PlayerFlashlight>();
    }

    private void HandleLifeStateChanged(
        PlayerLifeStateType previousState,
        PlayerLifeStateType newState
    )
    {
        if (newState == PlayerLifeStateType.Downed)
            TryCopyCurrentPoseToBodyLocal();

        ApplyLifeStateSideEffects(previousState, newState, force: false);
        OnLifeStateChanged?.Invoke(previousState, newState);
    }

    private void TryCopyCurrentPoseToBodyLocal()
    {
        if (CurrentBodyNetworkObjectId.Value == NoBodyNetworkObjectId)
            return;

        if (NetworkManager.Singleton == null)
            return;

        if (
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                CurrentBodyNetworkObjectId.Value,
                out NetworkObject bodyNetworkObject
            )
        )
            return;

        DownedBodyObject body = bodyNetworkObject.GetComponent<DownedBodyObject>();

        if (body == null)
            return;

        body.CopyPoseAndActivateRagdollFromSourceTransform(transform);
    }

    private void ApplyLifeStateSideEffects(
        PlayerLifeStateType previousState,
        PlayerLifeStateType newState,
        bool force
    )
    {
        CacheReferences();

        if (!force && previousState == newState)
            return;

        ApplySharedStateCleanup(newState);
        ApplyOwnerStateCleanup(newState);
    }

    private void ApplySharedStateCleanup(PlayerLifeStateType newState)
    {
        bool isAlive = newState == PlayerLifeStateType.Alive;

        bodyVisibility?.SetBodyVisible(isAlive);
        playerMotor?.SetControllerCollisionEnabled(isAlive);

        if (isAlive)
            return;

        // This runs on every client because phone/laptop/sitting visuals are local scene objects,
        // not separate network objects. Without this, remotes can keep seeing stuck visuals.
        phone?.ForceResetPhoneLocal();
        laptopHacker?.ForceResetLocalForRound();
        sitAction?.ForceResetLocalForRound();
        laptopVisual?.ForceResetLocalForRound();
    }

    private void ApplyOwnerStateCleanup(PlayerLifeStateType newState)
    {
        if (!IsOwner)
            return;

        if (newState == PlayerLifeStateType.Alive)
        {
            inputManager?.ForceClearGameplaySuppression();

            if (playerLook != null)
                playerLook.enabled = true;

            return;
        }

        ClearOwnerGameplayActions();

        inputManager?.SetGameplaySuppressed(true);
    }

    private void ClearOwnerGameplayActions()
    {
        phone?.ForceResetPhoneLocal();

        laptopHacker?.SetHackHeld(false);
        laptopHacker?.ForceResetLocalForRound();

        sitAction?.ForceResetLocalForRound();
        laptopVisual?.ForceResetLocalForRound();

        if (playerLook != null)
        {
            playerLook.SetPhoneAim(false);
            playerLook.SetAimHeld(false);
        }
    }

    public void ServerSetAlive()
    {
        if (!IsServer)
            return;

        ServerDespawnDownedBody();

        SetStateServer(
            PlayerLifeStateType.Alive,
            transform.position,
            NoAttackerClientId,
            resetDownedTimer: true
        );
    }

    public void ServerSetDowned(
        Vector3 downedPosition,
        ulong attackerClientId = NoAttackerClientId,
        Vector3 ragdollImpulse = default,
        Vector3 ragdollForcePosition = default
    )
    {
        if (!IsServer)
            return;

        if (IsBadGuy)
            return;

        if (State.Value == PlayerLifeStateType.Dead)
            return;

        Vector3 safeDownedPosition = downedPosition;

        bool flashlightWasOn =
            playerFlashlight != null && playerFlashlight.ShouldCarryLitFlashlightToDownedBody();

        ServerSpawnOrRefreshDownedBody(
            safeDownedPosition,
            PlayerLifeStateType.Downed,
            flashlightWasOn,
            ragdollImpulse,
            ragdollForcePosition
        );

        SetStateServer(
            PlayerLifeStateType.Downed,
            safeDownedPosition,
            attackerClientId,
            resetDownedTimer: true
        );

        sitAction?.ServerForceResetSitNetworkState();
    }

    public void ServerSetDead(ulong attackerClientId = NoAttackerClientId)
    {
        if (!IsServer)
            return;

        if (IsBadGuy)
            return;

        sitAction?.ServerForceResetSitNetworkState();

        Vector3 relevantPosition =
            State.Value == PlayerLifeStateType.Downed ? DownedPosition.Value : transform.position;

        if (HasSpawnedDownedBody())
            currentDownedBody.ServerSetBodyState(PlayerLifeStateType.Dead);

        SetStateServer(
            PlayerLifeStateType.Dead,
            relevantPosition,
            attackerClientId,
            resetDownedTimer: false
        );
    }

    public void ServerReviveFromDowned(Vector3 revivePosition)
    {
        if (!IsServer)
            return;

        if (State.Value != PlayerLifeStateType.Downed)
            return;

        ServerDespawnDownedBody();

        SetStateServer(
            PlayerLifeStateType.Alive,
            revivePosition,
            NoAttackerClientId,
            resetDownedTimer: true
        );
    }

    public void ServerResetForRound()
    {
        if (!IsServer)
            return;

        sitAction?.ServerForceResetSitNetworkState();
        ServerDespawnDownedBody();

        SetStateServer(
            PlayerLifeStateType.Alive,
            transform.position,
            NoAttackerClientId,
            resetDownedTimer: true
        );
    }

    public void ResetForRound()
    {
        CacheReferences();

        phone?.ForceResetPhoneLocal();
        laptopHacker?.ForceResetLocalForRound();
        sitAction?.ForceResetLocalForRound();
        laptopVisual?.ForceResetLocalForRound();
        bodyVisibility?.ForceShowBody();
        playerMotor?.SetControllerCollisionEnabled(true);

        if (!IsOwner)
            return;

        inputManager?.ForceClearGameplaySuppression();

        if (playerLook != null)
            playerLook.enabled = true;
    }

    private void SetStateServer(
        PlayerLifeStateType newState,
        Vector3 relevantPosition,
        ulong attackerClientId,
        bool resetDownedTimer
    )
    {
        if (!IsServer)
            return;

        DownedPosition.Value = relevantPosition;
        LastAttackerClientId.Value = attackerClientId;

        if (newState == PlayerLifeStateType.Downed)
            DownedStartedServerTime.Value = GetNetworkTime();
        else if (resetDownedTimer)
            DownedStartedServerTime.Value = 0d;

        State.Value = newState;
    }

    private void ServerSpawnOrRefreshDownedBody(
        Vector3 bodyPosition,
        PlayerLifeStateType bodyState,
        bool flashlightWasOn,
        Vector3 ragdollImpulse = default,
        Vector3 ragdollForcePosition = default
    )
    {
        if (!IsServer)
            return;

        if (HasSpawnedDownedBody())
        {
            currentDownedBody.ServerSetBodyState(bodyState);
            currentDownedBody.ServerSetCarriedFlashlightOn(flashlightWasOn);
            CurrentBodyNetworkObjectId.Value = currentDownedBody.NetworkObjectId;
            return;
        }

        if (downedBodyPrefab == null)
        {
            Debug.LogWarning(
                $"[PlayerLifeState] {name} became {bodyState}, but no Downed Body Prefab is assigned."
            );
            CurrentBodyNetworkObjectId.Value = NoBodyNetworkObjectId;
            return;
        }

        Vector3 spawnPosition = bodyPosition + Vector3.up * downedBodySpawnUpOffset;
        Quaternion spawnRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        DownedBodyObject spawnedBody = Instantiate(
            downedBodyPrefab,
            spawnPosition,
            spawnRotation
        );

        NetworkObject bodyNetworkObject = spawnedBody.GetComponent<NetworkObject>();
        if (bodyNetworkObject == null)
        {
            Debug.LogError(
                "[PlayerLifeState] Downed Body Prefab must have a NetworkObject component."
            );
            Destroy(spawnedBody.gameObject);
            CurrentBodyNetworkObjectId.Value = NoBodyNetworkObjectId;
            return;
        }

        bodyNetworkObject.Spawn(true);

        int shirtIndex = playerSetup != null ? playerSetup.ShirtIndex.Value : 0;
        bool isBadGuyBody = playerSetup != null && playerSetup.IsBadGuy.Value;

        spawnedBody.InitializeServer(
            OwnerClientId,
            NetworkObjectId,
            bodyState,
            shirtIndex,
            isBadGuyBody,
            flashlightWasOn
        );

        currentDownedBody = spawnedBody;
        CurrentBodyNetworkObjectId.Value = bodyNetworkObject.NetworkObjectId;

        spawnedBody.ServerCopyPoseAndActivateRagdollFromSource(
            NetworkObject,
            ragdollImpulse,
            ragdollForcePosition
        );
    }

    private bool HasSpawnedDownedBody()
    {
        return currentDownedBody != null
            && currentDownedBody.NetworkObject != null
            && currentDownedBody.NetworkObject.IsSpawned;
    }

    private void ServerDespawnDownedBody()
    {
        if (!IsServer)
            return;

        if (HasSpawnedDownedBody())
            currentDownedBody.NetworkObject.Despawn(true);

        currentDownedBody = null;
        CurrentBodyNetworkObjectId.Value = NoBodyNetworkObjectId;
    }

    [ServerRpc]
    private void DebugRequestSetStateServerRpc(
        PlayerLifeStateType requestedState,
        Vector3 requestedPosition,
        ServerRpcParams serverRpcParams = default
    )
    {
        if (!enableDebugHotkeys)
            return;

        if (serverRpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        switch (requestedState)
        {
            case PlayerLifeStateType.Alive:
                ServerSetAlive();
                break;

            case PlayerLifeStateType.Downed:
                ServerSetDowned(requestedPosition, NoAttackerClientId);
                break;

            case PlayerLifeStateType.Dead:
                ServerSetDead(NoAttackerClientId);
                break;
        }
    }

    private double GetNetworkTime()
    {
        if (NetworkManager.Singleton == null)
            return 0d;

        return NetworkManager.Singleton.ServerTime.Time;
    }
}
