using System.Collections.Generic;
using UnityEngine;

public class PhoneNetworksUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField]
    private Transform contentRoot;

    [SerializeField]
    private NetworkRowUI rowPrefab;

    [Header("Update")]
    [SerializeField]
    private float refreshInterval = 0.10f;

    private readonly List<NetworkRowUI> _rows = new();
    private float _timer;

    private Camera _playerCam;
    private Canvas _canvas;
    private RouterHackCoordinator _coordinator;

    private void Awake()
    {
        _canvas = GetComponentInParent<Canvas>(true);

        PlayerLook look = GetComponentInParent<PlayerLook>();

        if (look != null && look.cam != null)
            _playerCam = look.cam;
        else
            _playerCam = Camera.main;

        AttachCoordinatorIfNeeded();
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

        _timer -= Time.deltaTime;

        if (_timer > 0f)
            return;

        _timer = refreshInterval;
        RefreshList();
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
        if (contentRoot == null || rowPrefab == null)
            return;

        var routers = RouterRegistry.Routers;
        Vector3 fromPosition =
            _playerCam != null ? _playerCam.transform.position : transform.position;

        int rowIndex = 0;

        for (int i = 0; i < routers.Count; i++)
        {
            RouterBox router = routers[i];

            if (router == null)
                continue;

            if (_coordinator != null && _coordinator.IsCompleted(router.NetworkId))
                continue;

            float strength = router.GetStrength01(fromPosition);

            NetworkRowUI row = GetRow(rowIndex);
            row.gameObject.SetActive(true);
            row.Set(router.NetworkName, strength);
            rowIndex++;
        }

        for (int i = rowIndex; i < _rows.Count; i++)
            _rows[i].gameObject.SetActive(false);
    }

    private NetworkRowUI GetRow(int index)
    {
        while (_rows.Count <= index)
        {
            NetworkRowUI row = Instantiate(rowPrefab, contentRoot);
            _rows.Add(row);
        }

        return _rows[index];
    }
}
