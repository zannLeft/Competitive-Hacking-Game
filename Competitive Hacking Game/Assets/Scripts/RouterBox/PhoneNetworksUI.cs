using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class PhoneNetworksUI : MonoBehaviour
{
    [Header("Existing Prefab Setup")]
    [SerializeField]
    private Transform contentRoot;

    [SerializeField]
    private NetworkRowUI rowPrefab;

    [Header("ZannOS Phone Theme")]
    [SerializeField]
    private bool buildRuntimeTheme = true;

    [SerializeField]
    private TMP_FontAsset terminalFont;

    [SerializeField]
    private string shellName = "ZannOS";

    [SerializeField, Min(120f)]
    private float rowHeight = 164f;

    [Header("Signal Display")]
    [SerializeField, Min(1)]
    private int barCount = 5;

    [SerializeField, Min(0)]
    private int requiredBars = 5;

    [Header("Update")]
    [SerializeField, Min(0.02f)]
    private float refreshInterval = 0.10f;

    private readonly List<NetworkRowUI> _rows = new();
    private readonly List<NetworkSignal> _signals = new();
    private readonly Dictionary<string, NetworkSignal> _signalsById = new(StringComparer.Ordinal);

    private float _timer;
    private Camera _playerCam;
    private Canvas _canvas;
    private RouterHackCoordinator _coordinator;
    private PhoneNetworkVisualShell _visualShell;
    private RectTransform _legacyPanelRoot;

    private readonly struct NetworkSignal
    {
        public NetworkSignal(string id, string name, float strength)
        {
            Id = id;
            Name = name;
            Strength = strength;
        }

        public string Id { get; }
        public string Name { get; }
        public float Strength { get; }
    }

    private void Awake()
    {
        _canvas = GetComponentInParent<Canvas>(true);

        PlayerLook look = GetComponentInParent<PlayerLook>();
        if (look != null && look.cam != null)
            _playerCam = look.cam;
        else
            _playerCam = Camera.main;

        if (buildRuntimeTheme)
            BuildRuntimeTheme();

        AttachCoordinatorIfNeeded();
    }

    private void OnEnable()
    {
        _timer = 0f;
    }

    private void OnDestroy()
    {
        DetachCoordinator();
    }

    private void Update()
    {
        AttachCoordinatorIfNeeded();

        if (_canvas != null && !_canvas.enabled)
            return;

        _visualShell?.Tick(Time.unscaledTime);

        _timer -= Time.deltaTime;
        if (_timer > 0f)
            return;

        _timer = refreshInterval;
        RefreshList();
    }

    private void BuildRuntimeTheme()
    {
        if (_canvas == null)
            return;

        RectTransform canvasRoot = _canvas.transform as RectTransform;
        if (canvasRoot == null)
            return;

        _legacyPanelRoot = ResolveLegacyPanelRoot(contentRoot, canvasRoot);

        _visualShell = PhoneNetworkVisualShell.Build(
            canvasRoot,
            terminalFont,
            shellName
        );

        contentRoot = _visualShell.ContentRoot;

        if (_legacyPanelRoot != null)
            _legacyPanelRoot.gameObject.SetActive(false);
    }

    private static RectTransform ResolveLegacyPanelRoot(
        Transform source,
        RectTransform canvasRoot
    )
    {
        if (source == null || canvasRoot == null)
            return null;

        Transform current = source;
        while (current.parent != null && current.parent != canvasRoot)
            current = current.parent;

        if (current == canvasRoot)
            return null;

        return current as RectTransform;
    }

    private void AttachCoordinatorIfNeeded()
    {
        RouterHackCoordinator instance = RouterHackCoordinator.Instance;

        if (_coordinator == instance)
            return;

        DetachCoordinator();
        _coordinator = instance;

        if (_coordinator != null)
            _coordinator.RecordsChanged += OnCoordinatorRecordsChanged;

        _timer = 0f;
    }

    private void DetachCoordinator()
    {
        if (_coordinator != null)
            _coordinator.RecordsChanged -= OnCoordinatorRecordsChanged;

        _coordinator = null;
    }

    private void OnCoordinatorRecordsChanged()
    {
        _timer = 0f;
    }

    private void RefreshList()
    {
        if (contentRoot == null)
            return;

        GatherSignals();

        int safeBarCount = Mathf.Max(1, barCount);
        int safeRequiredBars = Mathf.Clamp(requiredBars, 0, safeBarCount);

        for (int i = 0; i < _signals.Count; i++)
        {
            NetworkSignal signal = _signals[i];
            NetworkRowUI row = GetRow(i);
            row.gameObject.SetActive(true);
            row.ConfigureTheme(
                terminalFont,
                _visualShell != null
                    ? _visualShell.Colors
                    : CreateFallbackPalette(),
                rowHeight
            );
            row.Set(
                signal.Name,
                signal.Strength,
                i,
                safeBarCount,
                safeRequiredBars,
                i == 0
            );
        }

        for (int i = _signals.Count; i < _rows.Count; i++)
            _rows[i].gameObject.SetActive(false);

        int bestBars = _signals.Count > 0
            ? Mathf.Clamp(
                Mathf.RoundToInt(_signals[0].Strength * safeBarCount),
                0,
                safeBarCount
            )
            : 0;
        bool hasReadyNetwork = bestBars >= safeRequiredBars;

        _visualShell?.SetSummary(
            _signals.Count,
            bestBars,
            safeBarCount,
            hasReadyNetwork
        );
    }

    private void GatherSignals()
    {
        _signals.Clear();
        _signalsById.Clear();

        IReadOnlyList<RouterBox> routers = RouterRegistry.Routers;
        Vector3 fromPosition =
            _playerCam != null ? _playerCam.transform.position : transform.position;

        for (int i = 0; i < routers.Count; i++)
        {
            RouterBox router = routers[i];
            if (router == null)
                continue;

            string networkId = router.NetworkId;
            if (_coordinator != null && _coordinator.IsCompleted(networkId))
                continue;

            string normalizedId = string.IsNullOrWhiteSpace(networkId)
                ? router.NetworkName ?? string.Empty
                : networkId.Trim();
            if (string.IsNullOrWhiteSpace(normalizedId))
                normalizedId = $"router-{router.GetInstanceID()}";

            string displayName = string.IsNullOrWhiteSpace(router.NetworkName)
                ? "UNNAMED NETWORK"
                : router.NetworkName.Trim();
            float strength = router.GetStrength01(fromPosition);

            if (
                _signalsById.TryGetValue(
                    normalizedId,
                    out NetworkSignal existing
                )
            )
            {
                if (strength > existing.Strength)
                {
                    _signalsById[normalizedId] = new NetworkSignal(
                        normalizedId,
                        displayName,
                        strength
                    );
                }
            }
            else
            {
                _signalsById.Add(
                    normalizedId,
                    new NetworkSignal(normalizedId, displayName, strength)
                );
            }
        }

        foreach (NetworkSignal signal in _signalsById.Values)
            _signals.Add(signal);

        _signals.Sort(CompareSignals);
    }

    private static int CompareSignals(NetworkSignal a, NetworkSignal b)
    {
        int strengthComparison = b.Strength.CompareTo(a.Strength);
        if (strengthComparison != 0)
            return strengthComparison;

        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }

    private NetworkRowUI GetRow(int index)
    {
        while (_rows.Count <= index)
        {
            NetworkRowUI row;

            if (rowPrefab != null)
            {
                row = Instantiate(rowPrefab, contentRoot);
            }
            else
            {
                GameObject rowObject = new(
                    $"NetworkRow_{_rows.Count + 1:00}",
                    typeof(RectTransform),
                    typeof(NetworkRowUI)
                );
                rowObject.transform.SetParent(contentRoot, false);
                row = rowObject.GetComponent<NetworkRowUI>();
            }

            row.gameObject.layer = contentRoot.gameObject.layer;
            _rows.Add(row);
        }

        return _rows[index];
    }

    private static PhoneNetworkVisualShell.Palette CreateFallbackPalette()
    {
        return new PhoneNetworkVisualShell.Palette(
            new Color(0.004f, 0.022f, 0.018f, 1f),
            new Color(0.009f, 0.035f, 0.029f, 1f),
            new Color(0.013f, 0.046f, 0.038f, 1f),
            new Color(0.190f, 0.770f, 0.660f, 1f),
            new Color(0.740f, 1.000f, 0.920f, 1f),
            new Color(0.540f, 0.965f, 0.885f, 1f),
            new Color(0.910f, 0.995f, 0.960f, 1f),
            new Color(0.230f, 0.530f, 0.470f, 1f)
        );
    }

    private void OnValidate()
    {
        refreshInterval = Mathf.Max(0.02f, refreshInterval);
        rowHeight = Mathf.Max(120f, rowHeight);
        barCount = Mathf.Max(1, barCount);
        requiredBars = Mathf.Clamp(requiredBars, 0, barCount);
    }
}
