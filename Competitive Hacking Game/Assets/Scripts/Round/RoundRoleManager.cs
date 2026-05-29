using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class RoundRoleManager : MonoBehaviour
{
    private const ulong NoBadGuyClientId = ulong.MaxValue;

    private ulong _currentBadGuyClientId = NoBadGuyClientId;

    public ulong CurrentBadGuyClientId => _currentBadGuyClientId;
    public bool HasBadGuy => _currentBadGuyClientId != NoBadGuyClientId;

    public void AssignRandomBadGuy()
    {
        var nm = NetworkManager.Singleton;

        if (nm == null || !nm.IsServer)
            return;

        List<NetworkClient> candidates = new();

        foreach (var client in nm.ConnectedClientsList)
        {
            if (client == null || client.PlayerObject == null)
                continue;

            PlayerSetup setup = GetPlayerSetup(client.PlayerObject);

            if (setup == null)
                continue;

            candidates.Add(client);
        }

        if (candidates.Count < 2)
        {
            Debug.LogWarning(
                $"[RoundRoleManager] Not enough valid players to assign bad guy. Candidates: {candidates.Count}"
            );
            return;
        }

        int chosenIndex = Random.Range(0, candidates.Count);
        NetworkClient chosenClient = candidates[chosenIndex];

        ResetRoles();

        PlayerSetup chosenSetup = GetPlayerSetup(chosenClient.PlayerObject);

        if (chosenSetup == null)
        {
            Debug.LogWarning("[RoundRoleManager] Chosen bad guy has no PlayerSetup.");
            return;
        }

        chosenSetup.IsBadGuy.Value = true;
        _currentBadGuyClientId = chosenClient.ClientId;

        Debug.Log($"[RoundRoleManager] Assigned client {_currentBadGuyClientId} as bad guy.");
    }

    public void ResetRoles()
    {
        var nm = NetworkManager.Singleton;

        if (nm == null || !nm.IsServer)
            return;

        foreach (var client in nm.ConnectedClientsList)
        {
            if (client == null || client.PlayerObject == null)
                continue;

            PlayerSetup setup = GetPlayerSetup(client.PlayerObject);

            if (setup == null)
                continue;

            setup.IsBadGuy.Value = false;
        }

        _currentBadGuyClientId = NoBadGuyClientId;
    }

    public bool IsClientBadGuy(ulong clientId)
    {
        return HasBadGuy && _currentBadGuyClientId == clientId;
    }

    private PlayerSetup GetPlayerSetup(NetworkObject playerObject)
    {
        if (playerObject == null)
            return null;

        PlayerSetup setup = playerObject.GetComponent<PlayerSetup>();

        if (setup == null)
            setup = playerObject.GetComponentInChildren<PlayerSetup>(true);

        return setup;
    }
}