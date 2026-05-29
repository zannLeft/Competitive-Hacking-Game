using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class TeleportService : MonoBehaviour
{
    private const string MSG_TELEPORT = "LM_Teleport";

    private bool _registered;
    private NetworkManager _registeredNetworkManager;

    public void RegisterHandlersIfNeeded()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            ResetRegistrationStateOnly();
            return;
        }

        var cmm = nm.CustomMessagingManager;
        if (cmm == null)
        {
            ResetRegistrationStateOnly();
            return;
        }

        // If already registered on this exact NetworkManager, do nothing.
        if (_registered && _registeredNetworkManager == nm)
            return;

        // If we think we were registered on an old/stale session, clear it first.
        UnregisterHandlersIfNeeded();

        cmm.RegisterNamedMessageHandler(MSG_TELEPORT, OnTeleportMessage);

        _registered = true;
        _registeredNetworkManager = nm;

        Debug.Log("[TeleportService] Registered teleport message handler.");
    }

    public void UnregisterHandlersIfNeeded()
    {
        if (_registeredNetworkManager != null)
        {
            var cmm = _registeredNetworkManager.CustomMessagingManager;

            if (cmm != null)
            {
                try
                {
                    cmm.UnregisterNamedMessageHandler(MSG_TELEPORT);
                }
                catch
                {
                    // Safe to ignore. This can happen if NetworkManager is already shutting down.
                }
            }
        }
        else if (NetworkManager.Singleton != null)
        {
            var cmm = NetworkManager.Singleton.CustomMessagingManager;

            if (cmm != null)
            {
                try
                {
                    cmm.UnregisterNamedMessageHandler(MSG_TELEPORT);
                }
                catch
                {
                    // Safe to ignore during shutdown/disconnect.
                }
            }
        }

        ResetRegistrationStateOnly();

        Debug.Log("[TeleportService] Unregistered teleport message handler.");
    }

    public void ResetRegistrationStateOnly()
    {
        _registered = false;
        _registeredNetworkManager = null;
    }

    public void SendTeleportToClient(ulong targetClientId, Vector3 pos, Quaternion rot)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
            return;

        // If it's us, teleport immediately.
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