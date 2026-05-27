using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LaptopHackUI : MonoBehaviour
{
    [Header("Text")]
    [SerializeField]
    private TMP_Text titleText;

    [SerializeField]
    private TMP_Text statusText;

    [SerializeField]
    private TMP_Text targetText;

    [SerializeField]
    private TMP_Text promptText;

    [Header("Progress")]
    [SerializeField]
    private GameObject progressRoot;

    [SerializeField]
    private Image progressFill;

    [SerializeField]
    private TMP_Text progressPercentText;

    [Header("Update")]
    [SerializeField]
    private float refreshInterval = 0.05f;

    private Canvas _canvas;
    private PlayerLaptopHacker _hacker;
    private float _timer;

    private void Awake()
    {
        _canvas = GetComponentInParent<Canvas>(true);
        _hacker = GetComponentInParent<PlayerLaptopHacker>(true);

        RouterHackState.Changed += OnHackStateChanged;
    }

    private void OnDestroy()
    {
        RouterHackState.Changed -= OnHackStateChanged;
    }

    private void Update()
    {
        if (_canvas != null && !_canvas.enabled)
            return;

        _timer -= Time.deltaTime;

        if (_timer > 0f)
            return;

        _timer = refreshInterval;
        Refresh();
    }

    private void OnHackStateChanged()
    {
        _timer = 0f;
    }

    private void Refresh()
    {
        if (titleText != null)
            titleText.text = "ZANNI OS\nSURVIVOR HACK TERMINAL";

        if (_hacker == null || !_hacker.IsLaptopUsable)
        {
            SetNoTarget(
                "SYSTEM LOCKED",
                "OPEN LAPTOP TO BEGIN"
            );
            return;
        }

        if (!_hacker.HasHackableTarget)
        {
            SetNoTarget(
                "NO HACKABLE NETWORK IN RANGE",
                "USE PHONE TO LOCATE A 5-BAR SIGNAL"
            );
            return;
        }

        if (statusText != null)
            statusText.text = "TARGET ACQUIRED";

        if (targetText != null)
            targetText.text = $"NETWORK:\n{_hacker.CurrentTargetName}";

        if (promptText != null)
            promptText.text = "HOLD E TO HACK";

        float p = _hacker.HackProgress01;

        if (progressRoot != null)
            progressRoot.SetActive(true);

        if (progressFill != null)
            progressFill.fillAmount = p;

        if (progressPercentText != null)
            progressPercentText.text = $"{Mathf.RoundToInt(p * 100f)}%";
    }

    private void SetNoTarget(string status, string message)
    {
        if (statusText != null)
            statusText.text = status;

        if (targetText != null)
            targetText.text = message;

        if (promptText != null)
            promptText.text = "";

        if (progressRoot != null)
            progressRoot.SetActive(false);

        if (progressFill != null)
            progressFill.fillAmount = 0f;

        if (progressPercentText != null)
            progressPercentText.text = "";
    }
}