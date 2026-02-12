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

    private void RefreshList()
    {
        if (contentRoot == null || rowPrefab == null)
            return;

        var routers = RouterRegistry.Routers;
        EnsureRowCount(routers.Count);

        Vector3 fromPos = _playerCam ? _playerCam.transform.position : transform.position;

        // Show ALL networks (even 0 strength), like you wanted
        for (int i = 0; i < routers.Count; i++)
        {
            var r = routers[i];
            float s = (r != null) ? r.GetStrength01(fromPos) : 0f;

            _rows[i].gameObject.SetActive(true);
            _rows[i].Set(r != null ? r.NetworkName : "Missing Router", s);
        }

        // Hide extra pooled rows
        for (int i = routers.Count; i < _rows.Count; i++)
            _rows[i].gameObject.SetActive(false);
    }

    private void EnsureRowCount(int needed)
    {
        while (_rows.Count < needed)
        {
            var row = Instantiate(rowPrefab, contentRoot);
            _rows.Add(row);
        }
    }
}
