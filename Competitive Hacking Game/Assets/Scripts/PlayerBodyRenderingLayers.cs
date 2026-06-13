using UnityEngine;

public static class PlayerBodyRenderingLayers
{
    public static int GetOwnerLayerIndex(
        ulong ownerClientId,
        int firstPlayerBodyRenderingLayerIndex,
        int playerBodyRenderingLayerCount
    )
    {
        if (playerBodyRenderingLayerCount <= 0)
            return -1;

        if (firstPlayerBodyRenderingLayerIndex < 0 || firstPlayerBodyRenderingLayerIndex > 31)
            return -1;

        int slot = (int)(ownerClientId % (ulong)playerBodyRenderingLayerCount);
        int layerIndex = firstPlayerBodyRenderingLayerIndex + slot;

        if (layerIndex < 0 || layerIndex > 31)
            return -1;

        return layerIndex;
    }

    public static uint GetLayerMaskUInt(int renderingLayerIndex)
    {
        if (renderingLayerIndex < 0 || renderingLayerIndex > 31)
            return 0u;

        return 1u << renderingLayerIndex;
    }

    public static int GetLayerMaskInt(int renderingLayerIndex)
    {
        return unchecked((int)GetLayerMaskUInt(renderingLayerIndex));
    }

    public static uint GetOwnerBodyMaskUInt(
        ulong ownerClientId,
        int firstPlayerBodyRenderingLayerIndex,
        int playerBodyRenderingLayerCount
    )
    {
        int layerIndex = GetOwnerLayerIndex(
            ownerClientId,
            firstPlayerBodyRenderingLayerIndex,
            playerBodyRenderingLayerCount
        );

        return GetLayerMaskUInt(layerIndex);
    }

    public static int GetOwnerBodyMaskInt(
        ulong ownerClientId,
        int firstPlayerBodyRenderingLayerIndex,
        int playerBodyRenderingLayerCount
    )
    {
        return unchecked((int)GetOwnerBodyMaskUInt(
            ownerClientId,
            firstPlayerBodyRenderingLayerIndex,
            playerBodyRenderingLayerCount
        ));
    }

    public static int GetAllPlayerBodyMasksInt(
        int firstPlayerBodyRenderingLayerIndex,
        int playerBodyRenderingLayerCount
    )
    {
        if (playerBodyRenderingLayerCount <= 0)
            return 0;

        int mask = 0;

        for (int i = 0; i < playerBodyRenderingLayerCount; i++)
        {
            int layerIndex = firstPlayerBodyRenderingLayerIndex + i;
            mask |= GetLayerMaskInt(layerIndex);
        }

        return mask;
    }
}
