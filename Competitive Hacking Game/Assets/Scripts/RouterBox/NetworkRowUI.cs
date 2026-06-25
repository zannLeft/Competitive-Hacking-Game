using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NetworkRowUI : MonoBehaviour
{
    [Header("Legacy Prefab References")]
    [SerializeField]
    private TMP_Text nameText;

    [SerializeField]
    private Image[] bars;

    private TMP_Text _runtimeNameText;
    private TMP_Text _statusText;
    private Image[] _runtimeBars;
    private Image _background;
    private PhoneNetworkVisualShell.Palette _palette;
    private bool _visualsBuilt;

    internal void ConfigureTheme(
        TMP_FontAsset font,
        PhoneNetworkVisualShell.Palette palette,
        float preferredHeight
    )
    {
        _palette = palette;

        if (!_visualsBuilt)
            BuildRuntimeVisuals(font, preferredHeight);
    }

    public void Set(
        string networkName,
        float strength01,
        int rank,
        int totalBars,
        int requiredBars,
        bool strongest
    )
    {
        int safeTotalBars = Mathf.Max(1, totalBars);
        int activeBars = Mathf.RoundToInt(Mathf.Clamp01(strength01) * safeTotalBars);
        activeBars = Mathf.Clamp(activeBars, 0, safeTotalBars);
        int safeRequiredBars = Mathf.Clamp(requiredBars, 0, safeTotalBars);
        bool ready = activeBars >= safeRequiredBars;

        if (_runtimeNameText != null)
            _runtimeNameText.text = string.IsNullOrWhiteSpace(networkName)
                ? "UNNAMED NETWORK"
                : networkName.Trim();
        else if (nameText != null)
            nameText.text = networkName;

        if (_statusText != null)
        {
            _statusText.text = ResolveStatus(activeBars, safeTotalBars, ready);
            _statusText.color = ready
                ? _palette.Accent
                : strongest
                    ? _palette.Objective
                    : WithAlpha(_palette.Muted, 0.88f);
        }

        if (_background != null)
        {
            _background.color = strongest
                ? WithAlpha(_palette.RaisedSurface, 0.72f)
                : WithAlpha(_palette.Surface, 0.52f);
        }

        if (_runtimeBars != null && _runtimeBars.Length > 0)
        {
            for (int i = 0; i < _runtimeBars.Length; i++)
            {
                bool active = i < activeBars;
                _runtimeBars[i].color = active
                    ? ready
                        ? _palette.Accent
                        : strongest
                            ? _palette.Objective
                            : _palette.Structure
                    : WithAlpha(_palette.Structure, 0.13f);
            }
        }

        if (bars != null)
        {
            for (int i = 0; i < bars.Length; i++)
            {
                if (bars[i] != null)
                    bars[i].enabled = false;
            }
        }
    }

    public void Set(string networkName, float strength01)
    {
        Set(networkName, strength01, 0, 5, 5, true);
    }

    private void BuildRuntimeVisuals(TMP_FontAsset font, float preferredHeight)
    {
        _visualsBuilt = true;

        for (int i = 0; i < transform.childCount; i++)
            transform.GetChild(i).gameObject.SetActive(false);

        float resolvedHeight = Mathf.Max(178f, preferredHeight);

        LayoutElement layoutElement = GetComponent<LayoutElement>();
        if (layoutElement == null)
            layoutElement = gameObject.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = resolvedHeight;
        layoutElement.minHeight = resolvedHeight;
        layoutElement.flexibleHeight = 0f;
        layoutElement.flexibleWidth = 1f;

        RectTransform root = transform as RectTransform;
        if (root != null)
        {
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.sizeDelta = new Vector2(0f, resolvedHeight);
            root.localScale = Vector3.one;
        }

        RectTransform visual = CreateRect("RuntimeVisual", transform);
        Stretch(visual, Vector2.zero, Vector2.one);

        _background = visual.gameObject.AddComponent<Image>();
        _background.color = WithAlpha(_palette.Surface, 0.52f);
        _background.raycastTarget = false;

        _runtimeNameText = CreateText(
            "NetworkName",
            visual,
            "NETWORK",
            42f,
            TextAlignmentOptions.MidlineLeft,
            _palette.Text,
            FontStyles.Bold,
            font
        );
        Stretch(_runtimeNameText.rectTransform, new Vector2(0.030f, 0.46f), new Vector2(0.70f, 0.92f));

        _statusText = CreateText(
            "Status",
            visual,
            "WEAK",
            24f,
            TextAlignmentOptions.MidlineLeft,
            WithAlpha(_palette.Muted, 0.88f),
            FontStyles.Bold,
            font
        );
        Stretch(_statusText.rectTransform, new Vector2(0.030f, 0.12f), new Vector2(0.68f, 0.48f));

        RectTransform barsRoot = CreateRect("SignalBars", visual);
        Stretch(barsRoot, new Vector2(0.710f, 0.20f), new Vector2(0.975f, 0.84f));

        _runtimeBars = new Image[5];
        const float gap = 0.075f;
        float barWidth = (1f - gap * 4f) / 5f;

        for (int i = 0; i < _runtimeBars.Length; i++)
        {
            _runtimeBars[i] = CreateImage(
                $"Bar{i + 1}",
                barsRoot,
                WithAlpha(_palette.Structure, 0.13f)
            );

            float xMin = i * (barWidth + gap);
            float xMax = xMin + barWidth;
            float height = Mathf.Lerp(0.28f, 1f, i / 4f);
            Stretch(
                _runtimeBars[i].rectTransform,
                new Vector2(xMin, 0f),
                new Vector2(xMax, height)
            );
        }

        Image bottomLine = CreateImage(
            "BottomLine",
            visual,
            WithAlpha(_palette.Structure, 0.13f)
        );
        Stretch(bottomLine.rectTransform, Vector2.zero, new Vector2(1f, 0.008f));
    }

    private static string ResolveStatus(int activeBars, int totalBars, bool ready)
    {
        if (ready)
            return "READY";
        if (activeBars <= 0)
            return "OUT OF RANGE";

        float normalized = activeBars / (float)Mathf.Max(1, totalBars);
        if (normalized >= 0.75f)
            return "STRONG";
        if (normalized >= 0.45f)
            return "STABLE";
        return "WEAK";
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject obj = new(name, typeof(RectTransform));
        obj.layer = parent.gameObject.layer;
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject obj = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.layer = parent.gameObject.layer;
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Image image = obj.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text CreateText(
        string name,
        Transform parent,
        string value,
        float size,
        TextAlignmentOptions alignment,
        Color color,
        FontStyles style,
        TMP_FontAsset font
    )
    {
        GameObject obj = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.layer = parent.gameObject.layer;
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        TextMeshProUGUI text = obj.GetComponent<TextMeshProUGUI>();
        text.text = value ?? string.Empty;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.fontStyle = style;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.characterSpacing = 0.25f;

        if (font != null)
            text.font = font;
        else if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        return text;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
