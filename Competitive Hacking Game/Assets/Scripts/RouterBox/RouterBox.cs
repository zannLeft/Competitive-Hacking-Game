using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class RouterBox : MonoBehaviour
{
    [Header("Router Info")]
    [Tooltip(
        "Stable logical ID used for minigame assignment and completion. "
        + "Physical routers that broadcast the same network must share this ID."
    )]
    [SerializeField]
    private string networkId;

    [Tooltip("Player-facing wireless network name.")]
    [SerializeField]
    private string networkName = "Network 1";

    [Tooltip("Distance where strength becomes 0.")]
    [SerializeField]
    private float maxRange = 20f;

    [Tooltip("Strength curve over normalized distance (0 = close, 1 = far).")]
    [SerializeField]
    private AnimationCurve falloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);

    public string NetworkId
    {
        get
        {
            string id = string.IsNullOrWhiteSpace(networkId) ? networkName : networkId;
            return string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
        }
    }

    public string NetworkName => networkName;
    public float MaxRange => maxRange;

    private void OnEnable() => RouterRegistry.Register(this);

    private void OnDisable() => RouterRegistry.Unregister(this);

    private void OnDestroy() => RouterRegistry.Unregister(this);

    public float GetStrength01(Vector3 fromPosition)
    {
        float d = Vector3.Distance(fromPosition, transform.position);

        if (maxRange <= 0.001f)
            return 0f;

        float t = Mathf.Clamp01(d / maxRange);
        return Mathf.Clamp01(falloff.Evaluate(t));
    }

    private void OnValidate()
    {
        maxRange = Mathf.Max(0f, maxRange);
    }
}

public static class RouterRegistry
{
    private static readonly List<RouterBox> _routers = new();

    public static IReadOnlyList<RouterBox> Routers => _routers;

    public static void Register(RouterBox router)
    {
        if (router == null)
            return;

        if (!_routers.Contains(router))
            _routers.Add(router);
    }

    public static void Unregister(RouterBox router)
    {
        if (router == null)
            return;

        _routers.Remove(router);
    }
}
