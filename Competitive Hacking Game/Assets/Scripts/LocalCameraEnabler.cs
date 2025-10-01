using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class LocalCameraEnabler : NetworkBehaviour
{
    private Camera _cam;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        if (_cam) _cam.enabled = false; // default OFF => no flash on non-owners
    }

    public override void OnNetworkSpawn()
    {
        if (_cam) _cam.enabled = IsOwner;
    }

    public override void OnGainedOwnership()
    {
        if (_cam) _cam.enabled = true;
    }

    public override void OnLostOwnership()
    {
        if (_cam) _cam.enabled = false;
    }
}
