using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(AudioListener))]
public class LocalAudioListener : NetworkBehaviour
{
    private AudioListener _listener;

    private void Awake()
    {
        _listener = GetComponent<AudioListener>();
        if (_listener) _listener.enabled = false; // default OFF to prevent duplicate blast
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner && _listener)
        {
            _listener.enabled = true;
            LobbyManager.Instance?.HandOffAudioAndMenuCamera(); // tell menu to hand off (see step 2)
        }
    }

    public override void OnNetworkDespawn()
    {
        if (_listener) _listener.enabled = false;
    }


    public override void OnGainedOwnership()
    {
        if (_listener) _listener.enabled = true;
        LobbyManager.Instance?.HandOffAudioAndMenuCamera();
    }

    public override void OnLostOwnership()
    {
        if (_listener) _listener.enabled = false;
    }
}
