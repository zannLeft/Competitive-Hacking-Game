using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class RoundResetManager : MonoBehaviour
{
    public void ResetAllPlayersForMatchStart()
    {
        var nm = NetworkManager.Singleton;

        if (nm == null || !nm.IsServer)
            return;

        RouterHackState.Clear();

        foreach (var client in nm.ConnectedClientsList)
        {
            if (client == null || client.PlayerObject == null)
                continue;

            PlayerRoundReset reset = GetPlayerRoundReset(client.PlayerObject);
            reset?.ServerResetForMatchStart();
        }
    }

    public void ResetAllPlayersForMatchEnd()
    {
        var nm = NetworkManager.Singleton;

        if (nm == null || !nm.IsServer)
            return;

        foreach (var client in nm.ConnectedClientsList)
        {
            if (client == null || client.PlayerObject == null)
                continue;

            PlayerRoundReset reset = GetPlayerRoundReset(client.PlayerObject);
            reset?.ServerResetForMatchEnd();
        }
    }

    private PlayerRoundReset GetPlayerRoundReset(NetworkObject playerObject)
    {
        if (playerObject == null)
            return null;

        PlayerRoundReset reset = playerObject.GetComponent<PlayerRoundReset>();

        if (reset == null)
            reset = playerObject.GetComponentInChildren<PlayerRoundReset>(true);

        return reset;
    }
}