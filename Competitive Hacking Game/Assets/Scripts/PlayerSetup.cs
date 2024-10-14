using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.Rendering;

public class PlayerSetup : NetworkBehaviour
{
    // Start is called before the first frame update


    public GameObject headMesh;
    private SkinnedMeshRenderer headRenderer;


    void Start()
    {

        headRenderer = headMesh.GetComponent<SkinnedMeshRenderer>();

        if (IsOwner) {
            headRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        } else {
            headRenderer.shadowCastingMode = ShadowCastingMode.On;
        }
        if (IsOwner)
        {
            // Set the local player's GameObject layer to PlayerModel
            SetLayerRecursively(gameObject, LayerMask.NameToLayer("MyPlayer"));
        }
        else
        {
            // Set other players' GameObjects to OtherPlayers
            SetLayerRecursively(gameObject, LayerMask.NameToLayer("OtherPlayers"));
        }
    }

    private void SetLayerRecursively(GameObject obj, int newLayer)
    {
       // Set the layer for this object
        obj.layer = newLayer;
    
        // Set the layer for all child objects
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }
}
