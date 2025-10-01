using TMPro;
using UnityEngine;

public class ConnectingOverlayUI : MonoBehaviour
{
    public static ConnectingOverlayUI Instance { get; private set; }

    [SerializeField] private GameObject root;               // Panel GameObject (full-screen overlay)
    [SerializeField] private TextMeshProUGUI message;

    // Internals
    private CanvasGroup _cg;

    private void Awake()
    {
        Debug.Log("[ConnectingOverlay] Awake. root=" + (root? root.name : "null"));

        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Default to this GO if none assigned
        if (root == null) root = gameObject;

        // Try to use a CanvasGroup to preserve alpha / interactable / blocksRaycasts semantics
        _cg = root.GetComponent<CanvasGroup>();
        if (_cg == null)
        {
            // Auto-add to keep behavior consistent without changing your prefabs
            _cg = root.AddComponent<CanvasGroup>();
        }

        // Start hidden but keep object active so it can be shown without re-instantiation
        if (_cg != null)
        {
            if (!root.activeSelf) root.SetActive(true);
            _cg.alpha = 0f;
            _cg.blocksRaycasts = false;
            _cg.interactable = false;
        }
        else
        {
            // Fallback: no CanvasGroup available; simply deactivate
            root.SetActive(false);
        }
    }

    public void Show(string text = "Connecting...")
    {
        Debug.Log("[ConnectingOverlay] Show called. Instance=" + (Instance != null) + " rootActiveSelf=" + (root? root.activeSelf : false));

        if (message != null) message.text = text;

        if (_cg != null)
        {
            if (!root.activeSelf) root.SetActive(true);
            _cg.alpha = 1f;
            _cg.blocksRaycasts = true;
            _cg.interactable = true;
        }
        else
        {
            // Fallback if CanvasGroup is unavailable
            root.SetActive(true);
        }
    }

    public void Hide()
    {
        Debug.Log("[ConnectingOverlay] Hide called.");
        if (_cg != null)
        {
            _cg.alpha = 0f;
            _cg.blocksRaycasts = false;
            _cg.interactable = false;
        }
        else
        {
            // Fallback if CanvasGroup is unavailable
            root.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
