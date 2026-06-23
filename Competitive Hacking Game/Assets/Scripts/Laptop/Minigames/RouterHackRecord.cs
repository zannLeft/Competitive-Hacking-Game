using System;
using Unity.Collections;
using Unity.Netcode;

public struct RouterHackRecord : INetworkSerializable, IEquatable<RouterHackRecord>
{
    public FixedString128Bytes NetworkId;
    public ushort MinigameId;
    public LaptopMinigameDifficulty Difficulty;
    public int Seed;
    public bool Completed;

    public RouterHackRecord(
        string networkId,
        ushort minigameId,
        LaptopMinigameDifficulty difficulty,
        int seed,
        bool completed = false
    )
    {
        NetworkId = new FixedString128Bytes(networkId);
        MinigameId = minigameId;
        Difficulty = difficulty;
        Seed = seed;
        Completed = completed;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref NetworkId);
        serializer.SerializeValue(ref MinigameId);

        byte difficultyValue = (byte)Difficulty;
        serializer.SerializeValue(ref difficultyValue);

        if (serializer.IsReader)
            Difficulty = (LaptopMinigameDifficulty)difficultyValue;

        serializer.SerializeValue(ref Seed);
        serializer.SerializeValue(ref Completed);
    }

    public bool Equals(RouterHackRecord other)
    {
        return NetworkId.Equals(other.NetworkId)
            && MinigameId == other.MinigameId
            && Difficulty == other.Difficulty
            && Seed == other.Seed
            && Completed == other.Completed;
    }

    public override bool Equals(object obj)
    {
        return obj is RouterHackRecord other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = NetworkId.GetHashCode();
            hash = (hash * 397) ^ MinigameId.GetHashCode();
            hash = (hash * 397) ^ Difficulty.GetHashCode();
            hash = (hash * 397) ^ Seed;
            hash = (hash * 397) ^ Completed.GetHashCode();
            return hash;
        }
    }
}
