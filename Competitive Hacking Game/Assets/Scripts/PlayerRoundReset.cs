using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerRoundReset : NetworkBehaviour
{
    [Header("Compatibility Refs")]
    [SerializeField]
    private PlayerSitAction sitAction;

    [SerializeField]
    private PlayerLaptopVisual laptopVisual;

    [SerializeField]
    private PlayerLaptopHacker laptopHacker;

    [SerializeField]
    private PlayerPhone phone;

    [SerializeField]
    private PlayerLook look;

    private readonly HashSet<object> _calledResetObjects = new();

    void Reset()
    {
        sitAction = GetComponent<PlayerSitAction>();
        laptopVisual = GetComponent<PlayerLaptopVisual>();
        laptopHacker = GetComponent<PlayerLaptopHacker>();
        phone = GetComponent<PlayerPhone>();
        look = GetComponent<PlayerLook>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (sitAction == null)
            sitAction = GetComponent<PlayerSitAction>();

        if (laptopVisual == null)
            laptopVisual = GetComponent<PlayerLaptopVisual>();

        if (laptopHacker == null)
            laptopHacker = GetComponent<PlayerLaptopHacker>();

        if (phone == null)
            phone = GetComponent<PlayerPhone>();

        if (look == null)
            look = GetComponent<PlayerLook>();
    }

    public void ServerResetForMatchStart()
    {
        if (!IsServer)
            return;

        ServerResetNetworkState();
        ResetForMatchStartClientRpc();
    }

    public void ServerResetForMatchEnd()
    {
        if (!IsServer)
            return;

        ServerResetNetworkState();
        ResetForMatchEndClientRpc();
    }

    private void ServerResetNetworkState()
    {
        var behaviours = GetComponentsInChildren<MonoBehaviour>(true);

        foreach (var behaviour in behaviours)
        {
            if (behaviour is IPlayerRoundServerResettable serverResettable)
                serverResettable.ServerResetForRound();
        }

        // Compatibility fallback for older scripts.
        sitAction?.ServerResetForRound();
    }

    [ClientRpc]
    private void ResetForMatchStartClientRpc()
    {
        RouterHackState.Clear();
        ResetLocalPlayerState();
    }

    [ClientRpc]
    private void ResetForMatchEndClientRpc()
    {
        ResetLocalPlayerState();
    }

    private void ResetLocalPlayerState()
    {
        _calledResetObjects.Clear();

        ResetInterfaceComponents();

        // Compatibility fallbacks for scripts that do not implement the interface yet.
        ResetFallback(sitAction, () => sitAction.ForceResetLocalForRound());
        ResetFallback(laptopVisual, () => laptopVisual.ForceResetLocalForRound());
        ResetFallback(laptopHacker, () => laptopHacker.ForceResetLocalForRound());
        ResetFallback(phone, () => phone.ForceResetPhoneLocal());

        if (look != null)
        {
            look.SetPhoneAim(false);
            look.SetAimHeld(false);
        }
    }

    private void ResetInterfaceComponents()
    {
        var behaviours = GetComponentsInChildren<MonoBehaviour>(true);

        foreach (var behaviour in behaviours)
        {
            if (behaviour is not IPlayerRoundResettable resettable)
                continue;

            resettable.ResetForRound();
            _calledResetObjects.Add(behaviour);
        }
    }

    private void ResetFallback(MonoBehaviour behaviour, Action resetAction)
    {
        if (behaviour == null)
            return;

        if (_calledResetObjects.Contains(behaviour))
            return;

        resetAction?.Invoke();
        _calledResetObjects.Add(behaviour);
    }
}