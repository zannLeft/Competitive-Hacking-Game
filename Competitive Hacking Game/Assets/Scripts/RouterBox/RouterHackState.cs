using System;
using System.Collections.Generic;

public static class RouterHackState
{
    private static readonly HashSet<string> _completedNetworks = new();

    public static event Action Changed;

    public static IEnumerable<string> CompletedNetworks => _completedNetworks;

    public static bool IsCompleted(string networkName)
    {
        if (string.IsNullOrWhiteSpace(networkName))
            return false;

        return _completedNetworks.Contains(networkName);
    }

    public static bool MarkCompleted(string networkName)
    {
        if (string.IsNullOrWhiteSpace(networkName))
            return false;

        bool added = _completedNetworks.Add(networkName);

        if (added)
            Changed?.Invoke();

        return added;
    }

    public static void Clear()
    {
        _completedNetworks.Clear();
        Changed?.Invoke();
    }
}