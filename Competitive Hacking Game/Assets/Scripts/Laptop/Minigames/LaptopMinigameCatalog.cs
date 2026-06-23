using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "LaptopMinigameCatalog",
    menuName = "Laptop/Minigame Catalog"
)]
public sealed class LaptopMinigameCatalog : ScriptableObject
{
    [SerializeField]
    private List<LaptopMinigameDefinition> minigames = new();

    public IReadOnlyList<LaptopMinigameDefinition> Minigames => minigames;

    public bool TryGetById(ushort minigameId, out LaptopMinigameDefinition definition)
    {
        for (int i = 0; i < minigames.Count; i++)
        {
            LaptopMinigameDefinition candidate = minigames[i];

            if (candidate == null || candidate.MinigameId != minigameId)
                continue;

            definition = candidate;
            return true;
        }

        definition = null;
        return false;
    }

    public bool TryChooseRandom(
        System.Random random,
        out LaptopMinigameDefinition definition
    )
    {
        definition = null;

        if (random == null)
            return false;

        int totalWeight = 0;

        for (int i = 0; i < minigames.Count; i++)
        {
            LaptopMinigameDefinition candidate = minigames[i];

            if (candidate == null || !candidate.IsSelectable)
                continue;

            totalWeight += candidate.SelectionWeight;
        }

        if (totalWeight <= 0)
            return false;

        int roll = random.Next(totalWeight);

        for (int i = 0; i < minigames.Count; i++)
        {
            LaptopMinigameDefinition candidate = minigames[i];

            if (candidate == null || !candidate.IsSelectable)
                continue;

            if (roll < candidate.SelectionWeight)
            {
                definition = candidate;
                return true;
            }

            roll -= candidate.SelectionWeight;
        }

        return false;
    }

    private void OnValidate()
    {
        var usedIds = new HashSet<ushort>();

        for (int i = 0; i < minigames.Count; i++)
        {
            LaptopMinigameDefinition definition = minigames[i];

            if (definition == null)
                continue;

            if (!usedIds.Add(definition.MinigameId))
            {
                Debug.LogWarning(
                    $"[LaptopMinigameCatalog] Duplicate minigame ID "
                        + $"{definition.MinigameId} in '{name}'. IDs must be unique.",
                    this
                );
            }

            GameObject prefab = definition.UiPrefab;

            if (
                prefab != null
                && prefab.GetComponent<LaptopMinigameBase>() == null
                && prefab.GetComponentInChildren<LaptopMinigameBase>(true) == null
            )
            {
                Debug.LogWarning(
                    $"[LaptopMinigameCatalog] UI prefab '{prefab.name}' for "
                        + $"'{definition.DisplayName}' has no LaptopMinigameBase component.",
                    prefab
                );
            }
        }
    }
}
