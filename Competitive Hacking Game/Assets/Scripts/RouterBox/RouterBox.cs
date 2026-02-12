using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class RouterBox : MonoBehaviour
{
    [Header("Router Info")]
    [SerializeField]
    private string networkName = "Network 1";

    [Tooltip("Distance where strength becomes 0.")]
    [SerializeField]
    private float maxRange = 20f;

    [Tooltip("Strength curve over normalized distance (0 = close, 1 = far).")]
    [SerializeField]
    private AnimationCurve falloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);

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

        float t = Mathf.Clamp01(d / maxRange); // 0 near, 1 far
        float s = Mathf.Clamp01(falloff.Evaluate(t)); // curve output 0..1
        return s;
    }
}

public static class RouterRegistry
{
    private static readonly List<RouterBox> _routers = new();

    public static IReadOnlyList<RouterBox> Routers => _routers;

    public static void Register(RouterBox r)
    {
        if (r == null)
            return;
        if (!_routers.Contains(r))
            _routers.Add(r);
    }

    public static void Unregister(RouterBox r)
    {
        if (r == null)
            return;
        _routers.Remove(r);
    }
}
