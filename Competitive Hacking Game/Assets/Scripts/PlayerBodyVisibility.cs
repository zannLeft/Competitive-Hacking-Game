using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerBodyVisibility : MonoBehaviour, IPlayerRoundResettable
{
    [Header("Explicit Visibility Targets")]
    [Tooltip("Recommended: drag the visible character/model root(s) here. Do not include camera, UI, phone, or laptop objects.")]
    [SerializeField]
    private GameObject[] objectsToToggle;

    [Tooltip("Optional extra renderers to control if you do not want to disable whole GameObjects.")]
    [SerializeField]
    private Renderer[] renderersToToggle;

    [Header("Auto Fallback")]
    [Tooltip("If no objects/renderers are assigned, automatically control child SkinnedMeshRenderers and MeshRenderers. Use only if your player prefab hierarchy is clean.")]
    [SerializeField]
    private bool autoFindRenderersIfNothingAssigned = false;

    [SerializeField]
    private bool includeInactiveAutoRenderers = true;

    private readonly List<Renderer> _autoRenderers = new List<Renderer>();
    private readonly Dictionary<GameObject, bool> _originalObjectActiveStates = new Dictionary<GameObject, bool>();
    private readonly Dictionary<Renderer, bool> _originalRendererEnabledStates = new Dictionary<Renderer, bool>();

    private bool _cachedOriginalStates;

    private void Awake()
    {
        CacheOriginalStates();
    }

    private void Reset()
    {
        autoFindRenderersIfNothingAssigned = false;
        includeInactiveAutoRenderers = true;
    }

    public void SetBodyVisible(bool visible)
    {
        CacheOriginalStates();

        if (objectsToToggle != null)
        {
            for (int i = 0; i < objectsToToggle.Length; i++)
            {
                GameObject target = objectsToToggle[i];
                if (target == null)
                    continue;

                bool originalActive = true;
                _originalObjectActiveStates.TryGetValue(target, out originalActive);
                target.SetActive(visible && originalActive);
            }
        }

        if (renderersToToggle != null)
        {
            for (int i = 0; i < renderersToToggle.Length; i++)
            {
                Renderer target = renderersToToggle[i];
                if (target == null)
                    continue;

                bool originalEnabled = true;
                _originalRendererEnabledStates.TryGetValue(target, out originalEnabled);
                target.enabled = visible && originalEnabled;
            }
        }

        for (int i = 0; i < _autoRenderers.Count; i++)
        {
            Renderer target = _autoRenderers[i];
            if (target == null)
                continue;

            bool originalEnabled = true;
            _originalRendererEnabledStates.TryGetValue(target, out originalEnabled);
            target.enabled = visible && originalEnabled;
        }
    }

    public void ForceShowBody()
    {
        SetBodyVisible(true);
    }

    public void ForceHideBody()
    {
        SetBodyVisible(false);
    }

    public void ResetForRound()
    {
        ForceShowBody();
    }

    private void CacheOriginalStates()
    {
        if (_cachedOriginalStates)
            return;

        _cachedOriginalStates = true;
        _originalObjectActiveStates.Clear();
        _originalRendererEnabledStates.Clear();
        _autoRenderers.Clear();

        if (objectsToToggle != null)
        {
            for (int i = 0; i < objectsToToggle.Length; i++)
            {
                GameObject target = objectsToToggle[i];
                if (target == null)
                    continue;

                if (!_originalObjectActiveStates.ContainsKey(target))
                    _originalObjectActiveStates.Add(target, target.activeSelf);
            }
        }

        if (renderersToToggle != null)
        {
            for (int i = 0; i < renderersToToggle.Length; i++)
            {
                Renderer target = renderersToToggle[i];
                if (target == null)
                    continue;

                if (!_originalRendererEnabledStates.ContainsKey(target))
                    _originalRendererEnabledStates.Add(target, target.enabled);
            }
        }

        bool hasExplicitTargets = HasExplicitTargets();
        if (!hasExplicitTargets && autoFindRenderersIfNothingAssigned)
            CacheAutoRenderers();
    }

    private bool HasExplicitTargets()
    {
        if (objectsToToggle != null)
        {
            for (int i = 0; i < objectsToToggle.Length; i++)
            {
                if (objectsToToggle[i] != null)
                    return true;
            }
        }

        if (renderersToToggle != null)
        {
            for (int i = 0; i < renderersToToggle.Length; i++)
            {
                if (renderersToToggle[i] != null)
                    return true;
            }
        }

        return false;
    }

    private void CacheAutoRenderers()
    {
        Renderer[] foundRenderers = GetComponentsInChildren<Renderer>(includeInactiveAutoRenderers);

        for (int i = 0; i < foundRenderers.Length; i++)
        {
            Renderer renderer = foundRenderers[i];
            if (renderer == null)
                continue;

            if (ShouldSkipAutoRenderer(renderer))
                continue;

            _autoRenderers.Add(renderer);

            if (!_originalRendererEnabledStates.ContainsKey(renderer))
                _originalRendererEnabledStates.Add(renderer, renderer.enabled);
        }
    }

    private bool ShouldSkipAutoRenderer(Renderer renderer)
    {
        Transform current = renderer.transform;
        while (current != null && current != transform)
        {
            string lowerName = current.name.ToLowerInvariant();

            if (lowerName.Contains("camera"))
                return true;

            if (lowerName.Contains("ui") || lowerName.Contains("canvas"))
                return true;

            if (lowerName.Contains("phone"))
                return true;

            if (lowerName.Contains("laptop"))
                return true;

            current = current.parent;
        }

        return false;
    }
}
