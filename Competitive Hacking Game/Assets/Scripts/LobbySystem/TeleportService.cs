using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class TeleportService : MonoBehaviour
{
    private const string MSG_TELEPORT = "LM_Teleport";
    private bool _registered;

    public void RegisterHandlersIfNeeded()
    {
        if (_registered)
            return;

        var nm = NetworkManager.Singleton;
        if (nm == null)
            return;

        var cmm = nm.CustomMessagingManager;
        if (cmm == null)
            return;

        cmm.RegisterNamedMessageHandler(MSG_TELEPORT, OnTeleportMessage);
        _registered = true;
    }

    public void UnregisterHandlersIfNeeded()
    {
        if (!_registered)
            return;

        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            var cmm = nm.CustomMessagingManager;
            if (cmm != null)
                cmm.UnregisterNamedMessageHandler(MSG_TELEPORT);
        }

        _registered = false;
    }

    public void SendTeleportToClient(ulong targetClientId, Vector3 pos, Quaternion rot)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
            return;

        // If it's us (host/local), teleport immediately
        if (nm.IsConnectedClient && nm.LocalClientId == targetClientId)
        {
            TeleportLocalPlayer(pos, rot);
            return;
        }

        var cmm = nm.CustomMessagingManager;
        if (cmm == null)
        {
            Debug.LogWarning(
                "[TeleportService] CustomMessagingManager not ready; cannot send teleport yet."
            );
            return;
        }

        using var writer = new FastBufferWriter(sizeof(float) * 7, Allocator.Temp);
        writer.WriteValueSafe(pos);
        writer.WriteValueSafe(rot);
        cmm.SendNamedMessage(MSG_TELEPORT, targetClientId, writer);
    }

    private void OnTeleportMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Vector3 pos);
        reader.ReadValueSafe(out Quaternion rot);
        TeleportLocalPlayer(pos, rot);
    }

    public void TeleportLocalPlayer(Vector3 pos, Quaternion rot)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
            return;

        var playerObj = nm.LocalClient?.PlayerObject;
        if (playerObj == null)
            return;

        var cc = playerObj.GetComponent<CharacterController>();
        if (cc != null)
            cc.enabled = false;

        playerObj.transform.SetPositionAndRotation(pos, rot);

        if (cc != null)
            cc.enabled = true;
    }
}
