using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal enum LaptopTerminalMessageKind
{
    Info,
    Scanning,
    NoNetwork,
    Verifying,
    Success,
    Completed,
    Warning,
    Error,
}

/// <summary>
/// Runtime-built ZannOS terminal screen used whenever no minigame is active.
/// The styling intentionally matches the colder cyan-green terminal pass used by
/// the active minigames so the laptop feels like one coherent in-world OS.
/// </summary>
internal sealed class LaptopTerminalMessageShell
{
    private readonly Color _background = new(0.004f, 0.022f, 0.018f, 1f);
    private readonly Color _surface = new(0.009f, 0.035f, 0.029f, 1f);
    private readonly Color _raisedSurface = new(0.013f, 0.046f, 0.038f, 1f);
    private readonly Color _structure = new(0.190f, 0.770f, 0.660f, 1f);
    private readonly Color _accent = new(0.740f, 1.000f, 0.920f, 1f);
    private readonly Color _objective = new(0.540f, 0.965f, 0.885f, 1f);
    private readonly Color _text = new(0.900f, 0.990f, 0.950f, 1f);
    private readonly Color _muted = new(0.230f, 0.530f, 0.470f, 1f);
    private readonly Color _danger = new(1.000f, 0.400f, 0.450f, 1f);

    private readonly string _shellName;
    private readonly float _clockStartUnscaledTime;
    private const int ClockBaseMinutes = 26;

    private TMP_Text _linkAndClockText;
    private TMP_Text _commandText;
    private TMP_Text _stateGlyphText;
    private TMP_Text _titleText;
    private TMP_Text _statusText;
    private TMP_Text _targetLabelText;
    private TMP_Text _targetText;
    private TMP_Text _actionLabelText;
    private TMP_Text _promptText;
    private TMP_Text _progressText;
    private TMP_Text _cursorText;
    private Image _stateRail;
    private int _lastClockMinute = -1;
    private LaptopTerminalMessageKind _kind;

    private LaptopTerminalMessageShell(string shellName)
    {
        _shellName = string.IsNullOrWhiteSpace(shellName)
            ? "ZannOS"
            : shellName.Trim();
        _clockStartUnscaledTime = Time.unscaledTime;
    }

    public RectTransform Root { get; private set; }

    public static LaptopTerminalMessageShell Build(
        RectTransform parent,
        TMP_FontAsset font,
        string shellName
    )
    {
        if (parent == null)
            throw new ArgumentNullException(nameof(parent));

        var shell = new LaptopTerminalMessageShell(shellName);
        shell.BuildInternal(parent, font);
        return shell;
    }

    public void SetVisible(bool visible)
    {
        if (Root != null)
            Root.gameObject.SetActive(visible);
    }

    public void Show(
        LaptopTerminalMessageKind kind,
        string command,
        string title,
        string status,
        string target,
        string prompt
    )
    {
        _kind = kind;
        SetVisible(true);

        Color stateColor = ResolveStateColor(kind);
        string glyph = ResolveStateGlyph(kind);
        string progress = ResolveProgress(kind);

        SetText(_commandText, string.IsNullOrWhiteSpace(command) ? "> system.status" : command);
        SetText(_stateGlyphText, glyph);
        SetText(_titleText, title);
        SetText(_statusText, status);
        SetText(_targetText, target);
        SetText(_promptText, prompt);
        SetText(_progressText, progress);
        ApplyMessageLayout(kind, target, prompt, progress);

        if (_stateGlyphText != null)
            _stateGlyphText.color = stateColor;
        if (_titleText != null)
            _titleText.color = stateColor;
        if (_stateRail != null)
            _stateRail.color = stateColor;
        if (_progressText != null)
            _progressText.color = kind == LaptopTerminalMessageKind.Error ? _danger : _structure;

        UpdateClock(force: true);
        UpdateCursor(Time.unscaledTime);
    }

    public void Tick(float unscaledTime)
    {
        UpdateClock(force: false);
        UpdateCursor(unscaledTime);
    }

    private void BuildInternal(RectTransform parent, TMP_FontAsset font)
    {
        Root = CreateRect("TerminalMessageShell", parent);
        Stretch(Root, Vector2.zero, Vector2.one);

        Image background = CreateImage("Background", Root, _background);
        Stretch(background.rectTransform, Vector2.zero, Vector2.one);

        RawImage scan = CreateRawImage(
            "TerminalScan",
            Root,
            CreateTerminalTexture(_background, _surface, _structure, _accent)
        );
        Stretch(scan.rectTransform, Vector2.zero, Vector2.one);
        scan.color = Color.white;

        BuildTopBar(font);
        BuildMainPanel(font);
        BuildFooter(font);
    }

    private void BuildTopBar(TMP_FontAsset font)
    {
        Image bar = CreateImage("TopBar", Root, WithAlpha(_surface, 0.94f));
        Stretch(bar.rectTransform, new Vector2(0f, 0.942f), Vector2.one);

        TMP_Text shell = CreateText(
            "ShellName",
            bar.transform,
            _shellName.ToUpperInvariant(),
            29f,
            TextAlignmentOptions.MidlineLeft,
            _text,
            FontStyles.Bold,
            font
        );
        Stretch(shell.rectTransform, new Vector2(0.025f, 0f), new Vector2(0.28f, 1f));

        TMP_Text app = CreateText(
            "AppName",
            bar.transform,
            string.Empty,
            18f,
            TextAlignmentOptions.Center,
            WithAlpha(_muted, 0.0f),
            FontStyles.Normal,
            font
        );
        Stretch(app.rectTransform, new Vector2(0.30f, 0f), new Vector2(0.70f, 1f));

        _linkAndClockText = CreateText(
            "LinkClock",
            bar.transform,
            "LINK  ●     00:26",
            24f,
            TextAlignmentOptions.MidlineRight,
            _accent,
            FontStyles.Bold,
            font
        );
        Stretch(_linkAndClockText.rectTransform, new Vector2(0.65f, 0f), new Vector2(0.975f, 1f));

        Image line = CreateImage("TopBarLine", Root, WithAlpha(_structure, 0.18f));
        Stretch(line.rectTransform, new Vector2(0f, 0.9405f), new Vector2(1f, 0.942f));
    }

    private void BuildMainPanel(TMP_FontAsset font)
    {
        RectTransform panelBorder = CreateRect("PanelBorder", Root);
        Stretch(panelBorder, new Vector2(0.070f, 0.145f), new Vector2(0.930f, 0.845f));
        Image borderImage = panelBorder.gameObject.AddComponent<Image>();
        borderImage.color = WithAlpha(_structure, 0.14f);
        borderImage.raycastTarget = false;

        RectTransform panel = CreateRect("Panel", panelBorder);
        StretchWithOffsets(panel, 2f, 2f, -2f, -2f);
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.color = WithAlpha(_background, 0.92f);
        panelImage.raycastTarget = false;

        Image topLine = CreateImage("PanelTopLine", panel, WithAlpha(_structure, 0.10f));
        Stretch(topLine.rectTransform, new Vector2(0f, 0.985f), new Vector2(1f, 1f));

        _stateRail = CreateImage("StateRail", panel, WithAlpha(_accent, 0.0f));
        Stretch(_stateRail.rectTransform, new Vector2(0.0f, 0.12f), new Vector2(0.0f, 0.88f));

        _commandText = CreateText(
            "Command",
            panel,
            string.Empty,
            18f,
            TextAlignmentOptions.MidlineLeft,
            WithAlpha(_muted, 0.0f),
            FontStyles.Normal,
            font
        );
        Stretch(_commandText.rectTransform, new Vector2(0.070f, 0.845f), new Vector2(0.66f, 0.910f));

        _stateGlyphText = CreateText(
            "StateGlyph",
            panel,
            "[OK]",
            31f,
            TextAlignmentOptions.MidlineRight,
            _accent,
            FontStyles.Bold,
            font
        );
        Stretch(_stateGlyphText.rectTransform, new Vector2(0.80f, 0.838f), new Vector2(0.94f, 0.910f));

        _titleText = CreateText(
            "Title",
            panel,
            "SYSTEM STATUS",
            60f,
            TextAlignmentOptions.MidlineLeft,
            _accent,
            FontStyles.Bold,
            font
        );
        Stretch(_titleText.rectTransform, new Vector2(0.070f, 0.640f), new Vector2(0.92f, 0.795f));
        _titleText.overflowMode = TextOverflowModes.Overflow;

        _statusText = CreateText(
            "Status",
            panel,
            string.Empty,
            33f,
            TextAlignmentOptions.TopLeft,
            WithAlpha(_text, 0.92f),
            FontStyles.Normal,
            font
        );
        Stretch(_statusText.rectTransform, new Vector2(0.070f, 0.505f), new Vector2(0.86f, 0.630f));
        _statusText.textWrappingMode = TextWrappingModes.Normal;
        _statusText.overflowMode = TextOverflowModes.Overflow;

        _targetLabelText = CreateText(
            "TargetLabel",
            panel,
            string.Empty,
            17f,
            TextAlignmentOptions.MidlineLeft,
            WithAlpha(_muted, 0.0f),
            FontStyles.Bold,
            font
        );
        Stretch(_targetLabelText.rectTransform, new Vector2(0.070f, 0.408f), new Vector2(0.20f, 0.455f));

        _targetText = CreateText(
            "Target",
            panel,
            string.Empty,
            29f,
            TextAlignmentOptions.MidlineLeft,
            _objective,
            FontStyles.Bold,
            font
        );
        Stretch(_targetText.rectTransform, new Vector2(0.070f, 0.336f), new Vector2(0.88f, 0.405f));

        Image divider = CreateImage("Divider", panel, WithAlpha(_structure, 0.12f));
        Stretch(divider.rectTransform, new Vector2(0.070f, 0.294f), new Vector2(0.88f, 0.296f));

        _actionLabelText = CreateText(
            "ActionLabel",
            panel,
            string.Empty,
            17f,
            TextAlignmentOptions.MidlineLeft,
            WithAlpha(_muted, 0.0f),
            FontStyles.Bold,
            font
        );
        Stretch(_actionLabelText.rectTransform, new Vector2(0.070f, 0.226f), new Vector2(0.20f, 0.273f));

        _promptText = CreateText(
            "Prompt",
            panel,
            string.Empty,
            28f,
            TextAlignmentOptions.TopLeft,
            WithAlpha(_text, 0.82f),
            FontStyles.Normal,
            font
        );
        Stretch(_promptText.rectTransform, new Vector2(0.070f, 0.145f), new Vector2(0.82f, 0.225f));
        _promptText.textWrappingMode = TextWrappingModes.Normal;
        _promptText.overflowMode = TextOverflowModes.Overflow;

        _progressText = CreateText(
            "Progress",
            panel,
            "[....................]",
            24f,
            TextAlignmentOptions.MidlineLeft,
            WithAlpha(_structure, 0.82f),
            FontStyles.Normal,
            font
        );
        Stretch(_progressText.rectTransform, new Vector2(0.070f, 0.058f), new Vector2(0.54f, 0.118f));

        _cursorText = CreateText(
            "Cursor",
            panel,
            string.Empty,
            18f,
            TextAlignmentOptions.MidlineRight,
            WithAlpha(_muted, 0.0f),
            FontStyles.Normal,
            font
        );
        Stretch(_cursorText.rectTransform, new Vector2(0.58f, 0.058f), new Vector2(0.90f, 0.118f));
    }

    private void ApplyMessageLayout(
        LaptopTerminalMessageKind kind,
        string target,
        string prompt,
        string progress
    )
    {
        bool showTarget = !string.IsNullOrWhiteSpace(target);
        bool showPrompt = !string.IsNullOrWhiteSpace(prompt);
        bool showProgress = kind == LaptopTerminalMessageKind.Scanning
            || kind == LaptopTerminalMessageKind.Verifying;

        if (_targetLabelText != null)
            _targetLabelText.gameObject.SetActive(false);
        if (_actionLabelText != null)
            _actionLabelText.gameObject.SetActive(false);
        if (_targetText != null)
            _targetText.gameObject.SetActive(showTarget);
        if (_promptText != null)
            _promptText.gameObject.SetActive(showPrompt);
        if (_progressText != null)
            _progressText.gameObject.SetActive(showProgress && !string.IsNullOrWhiteSpace(progress));

        if (_titleText != null)
            Stretch(_titleText.rectTransform, new Vector2(0.070f, 0.620f), new Vector2(0.92f, 0.800f));
        if (_statusText != null)
            Stretch(_statusText.rectTransform, new Vector2(0.070f, 0.475f), new Vector2(0.88f, 0.620f));

        if (showTarget && _targetText != null)
            Stretch(_targetText.rectTransform, new Vector2(0.070f, 0.335f), new Vector2(0.88f, 0.420f));

        if (showPrompt && _promptText != null)
        {
            Vector2 min = showTarget ? new Vector2(0.070f, 0.195f) : new Vector2(0.070f, 0.285f);
            Vector2 max = showTarget ? new Vector2(0.88f, 0.305f) : new Vector2(0.88f, 0.405f);
            Stretch(_promptText.rectTransform, min, max);
        }

        if (showProgress && _progressText != null)
            Stretch(_progressText.rectTransform, new Vector2(0.070f, 0.080f), new Vector2(0.66f, 0.145f));
    }

    private void BuildFooter(TMP_FontAsset font)
    {
        Image footer = CreateImage("Footer", Root, WithAlpha(_surface, 0.94f));
        Stretch(footer.rectTransform, Vector2.zero, new Vector2(1f, 0.075f));

        TMP_Text left = CreateText(
            "FooterLeft",
            footer.transform,
            string.Empty,
            18f,
            TextAlignmentOptions.MidlineLeft,
            WithAlpha(_text, 0.0f),
            FontStyles.Bold,
            font
        );
        Stretch(left.rectTransform, new Vector2(0.025f, 0f), new Vector2(0.72f, 1f));

        TMP_Text right = CreateText(
            "FooterRight",
            footer.transform,
            "Q  /DISCONNECT",
            20f,
            TextAlignmentOptions.MidlineRight,
            WithAlpha(_text, 0.62f),
            FontStyles.Bold,
            font
        );
        Stretch(right.rectTransform, new Vector2(0.70f, 0f), new Vector2(0.975f, 1f));

        Image line = CreateImage("FooterLine", Root, WithAlpha(_structure, 0.18f));
        Stretch(line.rectTransform, new Vector2(0f, 0.075f), new Vector2(1f, 0.0765f));
    }

    private void UpdateClock(bool force)
    {
        int minutes = ClockBaseMinutes
            + Mathf.FloorToInt((Time.unscaledTime - _clockStartUnscaledTime) / 60f);
        minutes = ((minutes % (24 * 60)) + (24 * 60)) % (24 * 60);

        if (!force && minutes == _lastClockMinute)
            return;

        _lastClockMinute = minutes;
        int hour = minutes / 60;
        int minute = minutes % 60;
        string link = _kind == LaptopTerminalMessageKind.NoNetwork ? "OFFLINE" : "●";

        if (_linkAndClockText != null)
        {
            _linkAndClockText.text = $"LINK  {link}     {hour:00}:{minute:00}";
            _linkAndClockText.color = _kind == LaptopTerminalMessageKind.NoNetwork
                ? WithAlpha(_muted, 0.62f)
                : ResolveStateColor(_kind);
        }
    }

    private void UpdateCursor(float time)
    {
        if (_cursorText == null)
            return;

        bool showCursor = Mathf.FloorToInt(time * 2f) % 2 == 0;
        _cursorText.text = showCursor ? "root@zannos:~$ _" : "root@zannos:~$  ";
    }

    private Color ResolveStateColor(LaptopTerminalMessageKind kind)
    {
        return kind switch
        {
            LaptopTerminalMessageKind.Success => _accent,
            LaptopTerminalMessageKind.Completed => _accent,
            LaptopTerminalMessageKind.Verifying => _objective,
            LaptopTerminalMessageKind.Warning => _objective,
            LaptopTerminalMessageKind.Error => _danger,
            _ => _accent,
        };
    }

    private static string ResolveStateGlyph(LaptopTerminalMessageKind kind)
    {
        return kind switch
        {
            LaptopTerminalMessageKind.Scanning => "[..]",
            LaptopTerminalMessageKind.NoNetwork => "[--]",
            LaptopTerminalMessageKind.Verifying => "[??]",
            LaptopTerminalMessageKind.Success => "[OK]",
            LaptopTerminalMessageKind.Completed => "[OK]",
            LaptopTerminalMessageKind.Warning => "[! ]",
            LaptopTerminalMessageKind.Error => "[!!]",
            _ => "[ i]",
        };
    }

    private static string ResolveProgress(LaptopTerminalMessageKind kind)
    {
        return kind switch
        {
            LaptopTerminalMessageKind.Scanning => "[####................] SCANNING",
            LaptopTerminalMessageKind.NoNetwork => string.Empty,
            LaptopTerminalMessageKind.Verifying => "[############........] VERIFYING",
            LaptopTerminalMessageKind.Success => string.Empty,
            LaptopTerminalMessageKind.Completed => string.Empty,
            LaptopTerminalMessageKind.Warning => string.Empty,
            LaptopTerminalMessageKind.Error => string.Empty,
            _ => string.Empty,
        };
    }

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null)
            text.text = value ?? string.Empty;
    }

    private static Texture2D CreateTerminalTexture(Color background, Color surface, Color glow, Color accent)
    {
        const int size = 256;
        Texture2D texture = new(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Vector2 mainCenter = new(0.56f, 0.54f);
        Vector2 secondaryCenter = new(0.24f, 0.32f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 uv = new(x / (float)(size - 1), y / (float)(size - 1));
                float main = SmoothInfluence(uv, mainCenter, 1.00f);
                float secondary = SmoothInfluence(uv, secondaryCenter, 0.78f);
                float edge = Mathf.Abs(uv.x - 0.5f) * 2f;
                float scan = 1f;

                Color color = Color.Lerp(background, surface, 0.16f + uv.y * 0.06f);
                color = Color.Lerp(color, glow, main * 0.045f + secondary * 0.022f);
                color = Color.Lerp(color, accent, secondary * 0.006f);
                color = Color.Lerp(color, background, edge * 0.10f);
                color *= scan;

                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply(false, true);
        return texture;
    }

    private static float SmoothInfluence(Vector2 uv, Vector2 center, float radius)
    {
        float distance = Vector2.Distance(uv, center) / Mathf.Max(0.0001f, radius);
        float value = Mathf.Clamp01(1f - distance);
        return value * value * (3f - 2f * value);
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject obj = new(name, typeof(RectTransform));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject obj = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Image image = obj.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static RawImage CreateRawImage(string name, Transform parent, Texture texture)
    {
        GameObject obj = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        RawImage image = obj.GetComponent<RawImage>();
        image.texture = texture;
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

    private static void StretchWithOffsets(
        RectTransform rect,
        float left,
        float bottom,
        float right,
        float top
    )
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(right, top);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
