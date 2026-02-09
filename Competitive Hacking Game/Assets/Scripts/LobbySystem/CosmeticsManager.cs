using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CosmeticsManager : MonoBehaviour
{
    [Header("Shirt materials (assign in inspector)")]
    [SerializeField]
    private List<Material> shirtMaterials = new();

    [SerializeField]
    private Material blackShirtMaterial;

    private readonly HashSet<int> usedShirtIndices = new();

    public Material BlackShirtMaterial => blackShirtMaterial;

    public int AssignColorIndex()
    {
        if (shirtMaterials == null || shirtMaterials.Count == 0)
        {
            Debug.LogWarning("[Cosmetics] No shirt materials configured. returning index 0");
            return 0;
        }

        for (int i = 0; i < shirtMaterials.Count; i++)
        {
            if (!usedShirtIndices.Contains(i))
            {
                usedShirtIndices.Add(i);
                return i;
            }
        }

        int fallback = Random.Range(0, shirtMaterials.Count);
        Debug.LogWarning($"[Cosmetics] All shirt indices used, falling back to {fallback}");
        return fallback;
    }

    public void ReleaseColorIndex(int index)
    {
        usedShirtIndices.Remove(index);
    }

    public Material GetShirtMaterial(int index)
    {
        if (shirtMaterials == null || shirtMaterials.Count == 0)
            return null;

        if (index < 0 || index >= shirtMaterials.Count)
            return shirtMaterials[0];

        return shirtMaterials[index];
    }
}
