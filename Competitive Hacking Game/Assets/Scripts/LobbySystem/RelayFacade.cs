using System.Threading.Tasks;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine; // ✅ needed for DisallowMultipleComponent + MonoBehaviour

[DisallowMultipleComponent]
public class RelayFacade : MonoBehaviour
{
    public async Task<Allocation> AllocateRelayAsync(int maxConnections)
    {
        return await RelayService.Instance.CreateAllocationAsync(maxConnections);
    }

    public async Task<string> GetRelayJoinCodeAsync(Allocation allocation)
    {
        return await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
    }

    public async Task<JoinAllocation> JoinRelayAsync(string joinCode)
    {
        return await RelayService.Instance.JoinAllocationAsync(joinCode);
    }

    public RelayServerData BuildRelayServerData(Allocation allocation, string protocol) =>
        AllocationUtils.ToRelayServerData(allocation, protocol);

    public RelayServerData BuildRelayServerData(JoinAllocation joinAllocation, string protocol) =>
        AllocationUtils.ToRelayServerData(joinAllocation, protocol);
}
