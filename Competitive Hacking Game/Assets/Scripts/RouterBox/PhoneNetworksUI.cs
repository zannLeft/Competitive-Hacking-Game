using System.Collections.Generic;
using UnityEngine;

public class PhoneNetworksUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField]
    private Transform contentRoot; // parent with VerticalLayoutGroup

    [SerializeField]
    private NetworkRowUI rowPrefab;

    [Header("Update")]
    [SerializeField]
    private float refreshInterval = 0.10f; // 10 Hz

    private readonly List<NetworkRowUI> _rows = new();
    private float _timer;

    private Camera _playerCam;
    private Canvas _canvas;

    private void Awake()
    {
        _canvas = GetComponentInParent<Canvas>(true);

        // Find the local player's camera via your existing stack
        var look = GetComponentInParent<PlayerLook>();
        if (look != null && look.cam != null)
            _playerCam = look.cam;
        else
            _playerCam = Camera.main;

        RouterHackState.Changed += OnRouterHackStateChanged;
    }

    private void OnDestroy()
    {
        RouterHackState.Changed -= OnRouterHackStateChanged;
    }

    private void Update()
    {
        // PhoneScreenController toggles canvas enabled only for the owner, so this is a cheap early out.
        if (_canvas != null && !_canvas.enabled)
            return;

        _timer -= Time.deltaTime;
        if (_timer > 0f)
            return;

        _timer = refreshInterval;
        RefreshList();
    }

    private void OnRouterHackStateChanged()
    {
        // Force quick refresh when a network gets completed.
        _timer = 0f;
    }

    private void RefreshList()
    {
        if (contentRoot == null || rowPrefab == null)
            return;

        var routers = RouterRegistry.Routers;
        Vector3 fromPos = _playerCam ? _playerCam.transform.position : transform.position;

        int rowIndex = 0;

        for (int i = 0; i < routers.Count; i++)
        {
            var r = routers[i];

            if (r == null)
                continue;

            // Hide completed/hacked networks from the phone scanner.
            if (RouterHackState.IsCompleted(r.NetworkName))
                continue;

            float strength = r.GetStrength01(fromPos);

            NetworkRowUI row = GetRow(rowIndex);
            row.gameObject.SetActive(true);
            row.Set(r.NetworkName, strength);

            rowIndex++;
        }

        // Hide unused pooled rows
        for (int i = rowIndex; i < _rows.Count; i++)
            _rows[i].gameObject.SetActive(false);
    }

    private NetworkRowUI GetRow(int index)
    {
        while (_rows.Count <= index)
        {
            var row = Instantiate(rowPrefab, contentRoot);
            _rows.Add(row);
        }

        return _rows[index];
    }
}