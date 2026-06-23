public readonly struct LaptopMinigameContext
{
    public string NetworkId { get; }
    public string NetworkDisplayName { get; }
    public ushort MinigameId { get; }
    public string MinigameDisplayName { get; }
    public LaptopMinigameDifficulty Difficulty { get; }
    public int Seed { get; }

    public LaptopMinigameContext(
        string networkId,
        string networkDisplayName,
        ushort minigameId,
        string minigameDisplayName,
        LaptopMinigameDifficulty difficulty,
        int seed
    )
    {
        NetworkId = networkId ?? string.Empty;
        NetworkDisplayName = networkDisplayName ?? string.Empty;
        MinigameId = minigameId;
        MinigameDisplayName = minigameDisplayName ?? string.Empty;
        Difficulty = difficulty;
        Seed = seed;
    }
}
