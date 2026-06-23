using System;
using UnityEngine;

[Serializable]
public sealed class LaptopMinigameDefinition
{
    [Header("Identity")]
    [SerializeField, Min(1)]
    private int minigameId = 1;

    [SerializeField]
    private string displayName = "Firewall Runner";

    [Tooltip("The UI prefab will be assigned when this minigame is implemented.")]
    [SerializeField]
    private GameObject uiPrefab;

    [Header("Random Selection")]
    [SerializeField, Min(0)]
    private int selectionWeight = 1;

    [Header("Difficulty")]
    [SerializeField]
    private bool allowEasy = true;

    [SerializeField]
    private bool allowHard = true;

    [SerializeField, Range(0f, 1f)]
    private float hardChance = 0.35f;

    [Header("Server Validation")]
    [SerializeField, Min(0f)]
    private float minimumEasyCompletionSeconds = 8f;

    [SerializeField, Min(0f)]
    private float minimumHardCompletionSeconds = 12f;

    public ushort MinigameId => (ushort)Mathf.Clamp(minigameId, 1, ushort.MaxValue);
    public string DisplayName => displayName;
    public GameObject UiPrefab => uiPrefab;
    public int SelectionWeight => Mathf.Max(0, selectionWeight);
    public bool AllowEasy => allowEasy;
    public bool AllowHard => allowHard;
    public float HardChance => Mathf.Clamp01(hardChance);

    public bool IsSelectable =>
        SelectionWeight > 0
        && (allowEasy || allowHard)
        && !string.IsNullOrWhiteSpace(displayName);

    public LaptopMinigameDifficulty ChooseDifficulty(System.Random random)
    {
        if (allowEasy && allowHard)
        {
            double roll = random != null ? random.NextDouble() : 0.0;
            return roll < HardChance
                ? LaptopMinigameDifficulty.Hard
                : LaptopMinigameDifficulty.Easy;
        }

        return allowHard
            ? LaptopMinigameDifficulty.Hard
            : LaptopMinigameDifficulty.Easy;
    }

    public float GetMinimumCompletionSeconds(LaptopMinigameDifficulty difficulty)
    {
        return difficulty == LaptopMinigameDifficulty.Hard
            ? Mathf.Max(0f, minimumHardCompletionSeconds)
            : Mathf.Max(0f, minimumEasyCompletionSeconds);
    }
}
