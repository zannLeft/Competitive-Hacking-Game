using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal readonly struct LaptopMinigameShellPalette
{
    public LaptopMinigameShellPalette(
        Color background,
        Color surface,
        Color raisedSurface,
        Color structure,
        Color accent,
        Color objective,
        Color text,
        Color mutedText,
        Color danger
    )
    {
        Background = background;
        Surface = surface;
        RaisedSurface = raisedSurface;
        Structure = structure;
        Accent = accent;
        Objective = objective;
        Text = text;
        MutedText = mutedText;
        Danger = danger;
    }

    public Color Background { get; }
    public Color Surface { get; }
    public Color RaisedSurface { get; }
    public Color Structure { get; }
    public Color Accent { get; }
    public Color Objective { get; }
    public Color Text { get; }
    public Color MutedText { get; }
    public Color Danger { get; }
}

/// <summary>
/// Runtime-built visual shell shared by the laptop minigames.
/// It deliberately resembles a compact, customized Linux desktop without
/// copying any real operating system or consuming much gameplay space.
/// </summary>
internal sealed class LaptopMinigameVisualShell
{
    private const float OsBarBottom = 0.949f;
    private const float HeaderBottom = 0.880f;
    private const float FooterTop = 0.056f;

    private readonly LaptopMinigameShellPalette _palette;
    private readonly string _shellName;
    private readonly string _applicationName;
    private readonly Color _startPromptBaseColor;

    private TMP_Text _applicationText;
    private TMP_Text _clockText;
    private TMP_Text _briefingNodeText;
    private TMP_Text _briefingDifficultyText;
    private TMP_Text _startPromptText;
    private RectTransform _briefingRoot;
    private RectTransform _resultOverlayRoot;
    private Image _resultOverlayBackground;
    private Image _resultRailImage;
    private TMP_Text _resultText;
    private RectTransform _progressFillRect;
    private int _lastClockMinute = -1;
    private float _clockStartUnscaledTime;
    private const int FictionalClockBaseMinutes = 0 * 60 + 26;

    private LaptopMinigameVisualShell(
        LaptopMinigameShellPalette palette,
        string shellName,
        string applicationName
    )
    {
        _palette = palette;
        _shellName = string.IsNullOrWhiteSpace(shellName) ? "SABLE" : shellName.Trim();
        _applicationName = string.IsNullOrWhiteSpace(applicationName)
            ? "intrusion-suite"
            : applicationName.Trim();
        _startPromptBaseColor = palette.Accent;
        _clockStartUnscaledTime = Time.unscaledTime;
    }

    public RectTransform GameArea { get; private set; }
    public TMP_Text NetworkText { get; private set; }
    public TMP_Text DifficultyText { get; private set; }
    public TMP_Text StatusText { get; private set; }
    public TMP_Text FooterLeftText { get; private set; }
    public TMP_Text FooterRightText { get; private set; }
    public RectTransform ResultOverlayRect => _resultOverlayRoot;
    public Image ResultOverlayImage => _resultOverlayBackground;
    public TMP_Text ResultText => _resultText;

    public static LaptopMinigameVisualShell Build(
        RectTransform root,
        TMP_FontAsset font,
        LaptopMinigameShellPalette palette,
        string shellName,
        string applicationName,
        string moduleTitle,
        string briefingDescription,
        string briefingObjective,
        string briefingControls,
        string footerObjective
    )
    {
        var shell = new LaptopMinigameVisualShell(
            palette,
            shellName,
            applicationName
        );

        shell.BuildInternal(
            root,
            font,
            moduleTitle,
            briefingDescription,
            briefingObjective,
            briefingControls,
            footerObjective
        );

        return shell;
    }

    public void SetContext(string networkDisplayName, string difficulty)
    {
        string safeNode = string.IsNullOrWhiteSpace(networkDisplayName)
            ? "UNKNOWN"
            : networkDisplayName.Trim();
        string safeDifficulty = string.IsNullOrWhiteSpace(difficulty)
            ? "EASY"
            : difficulty.Trim().ToUpperInvariant();

        if (NetworkText != null)
            NetworkText.text = $"NODE  {safeNode}";

        if (DifficultyText != null)
            DifficultyText.text = safeDifficulty;

        if (_briefingNodeText != null)
            _briefingNodeText.text = $"NODE / {safeNode}";

        if (_briefingDifficultyText != null)
            _briefingDifficultyText.text = $"PROFILE / {safeDifficulty}";
    }

    public void SetStatus(string value, Color? color = null)
    {
        if (StatusText == null)
            return;

        StatusText.text = value ?? string.Empty;
        StatusText.color = color ?? _palette.Text;
    }

    public void SetProgress(float normalized)
    {
        if (_progressFillRect == null)
            return;

        float clamped = Mathf.Clamp01(normalized);
        _progressFillRect.anchorMin = Vector2.zero;
        _progressFillRect.anchorMax = new Vector2(clamped, 1f);
        _progressFillRect.offsetMin = Vector2.zero;
        _progressFillRect.offsetMax = Vector2.zero;
    }

    public void SetFooterLeft(string value)
    {
        if (FooterLeftText != null)
            FooterLeftText.text = value ?? string.Empty;
    }

    public void SetFooterRight(string value)
    {
        if (FooterRightText != null)
            FooterRightText.text = value ?? string.Empty;
    }

    public void SetBriefingVisible(bool visible)
    {
        if (_briefingRoot != null)
            _briefingRoot.gameObject.SetActive(visible);

        if (_applicationText != null)
            _applicationText.text = visible ? "console" : _applicationName;
    }

    public void HideResult()
    {
        if (_resultOverlayRoot != null)
            _resultOverlayRoot.gameObject.SetActive(false);
    }

    public void ShowResult(string message, Color color)
    {
        SetBriefingVisible(false);

        if (_resultOverlayRoot == null)
            return;

        _resultOverlayRoot.gameObject.SetActive(true);

        if (_resultOverlayBackground != null)
        {
            _resultOverlayBackground.color = new Color(
                _palette.Background.r,
                _palette.Background.g,
                _palette.Background.b,
                0.975f
            );
        }

        if (_resultRailImage != null)
            _resultRailImage.color = color;

        if (_resultText != null)
        {
            _resultText.text = message ?? string.Empty;
            _resultText.color = color;
        }
    }

    public void Tick(float unscaledTime)
    {
        int simulatedMinutes = FictionalClockBaseMinutes + Mathf.FloorToInt((Time.unscaledTime - _clockStartUnscaledTime) / 60f);
        simulatedMinutes = ((simulatedMinutes % (24 * 60)) + (24 * 60)) % (24 * 60);
        int minuteKey = simulatedMinutes;

        if (_clockText != null && minuteKey != _lastClockMinute)
        {
            _lastClockMinute = minuteKey;
            int hour = simulatedMinutes / 60;
            int minute = simulatedMinutes % 60;
            _clockText.text = $"LINK  ●     {hour:00}:{minute:00}";
        }

        if (_startPromptText == null || !_startPromptText.gameObject.activeInHierarchy)
            return;

        float pulse = 0.62f + 0.38f * (0.5f + 0.5f * Mathf.Sin(unscaledTime * 4.2f));
        Color promptColor = _startPromptBaseColor;
        promptColor.a *= pulse;
        _startPromptText.color = promptColor;
    }

    private void BuildInternal(
        RectTransform root,
        TMP_FontAsset font,
        string moduleTitle,
        string briefingDescription,
        string briefingObjective,
        string briefingControls,
        string footerObjective
    )
    {
        if (root == null)
            throw new ArgumentNullException(nameof(root));

        StretchToParent(root);

        Image background = CreateImage("ShellBackground", root, _palette.Background);
        Stretch(background.rectTransform, Vector2.zero, Vector2.one);

        BuildSubtleWallpaper(root);
        BuildOsBar(root, font);
        BuildApplicationHeader(root, font, moduleTitle);
        BuildFooter(root, font, footerObjective);

        Image gameAreaBackground = CreateImage(
            "GameAreaBackground",
            root,
            WithAlpha(_palette.Background, 0.06f)
        );
        Stretch(
            gameAreaBackground.rectTransform,
            new Vector2(0.0f, FooterTop),
            new Vector2(1.0f, HeaderBottom - 0.006f)
        );

        Image gameAreaEdge = CreateImage(
            "GameAreaEdge",
            root,
            WithAlpha(_palette.Structure, 0.24f)
        );
        Stretch(
            gameAreaEdge.rectTransform,
            new Vector2(0.0f, HeaderBottom - 0.006f),
            new Vector2(1.0f, HeaderBottom - 0.0048f)
        );


        Image gameAreaLeftEdge = CreateImage(
            "GameAreaLeftEdge",
            root,
            WithAlpha(_palette.Structure, 0.20f)
        );
        Stretch(
            gameAreaLeftEdge.rectTransform,
            new Vector2(0.0f, FooterTop),
            new Vector2(0.0018f, HeaderBottom - 0.006f)
        );

        Image gameAreaRightEdge = CreateImage(
            "GameAreaRightEdge",
            root,
            WithAlpha(_palette.Structure, 0.20f)
        );
        Stretch(
            gameAreaRightEdge.rectTransform,
            new Vector2(0.9982f, FooterTop),
            new Vector2(1.0f, HeaderBottom - 0.006f)
        );

        Image gameAreaBottomEdge = CreateImage(
            "GameAreaBottomEdge",
            root,
            WithAlpha(_palette.Structure, 0.16f)
        );
        Stretch(
            gameAreaBottomEdge.rectTransform,
            new Vector2(0.0f, FooterTop),
            new Vector2(1.0f, FooterTop + 0.0018f)
        );

        GameArea = CreateRect("GameArea", root);
        Stretch(
            GameArea,
            new Vector2(0.016f, FooterTop),
            new Vector2(0.984f, HeaderBottom)
        );

        BuildBriefing(
            root,
            font,
            moduleTitle,
            briefingDescription,
            briefingObjective,
            briefingControls
        );
        BuildResultOverlay(root, font);
        Tick(Time.unscaledTime);
    }

    private void BuildSubtleWallpaper(RectTransform root)
    {
        RawImage gradient = CreateRawImage(
            "WallpaperGradient",
            root,
            CreateAmbientGradientTexture(
                _palette.Background,
                Color.Lerp(_palette.Structure, _palette.Text, 0.04f),
                Color.Lerp(_palette.Accent, _palette.Text, 0.05f)
            )
        );
        Stretch(gradient.rectTransform, Vector2.zero, Vector2.one);
        gradient.color = Color.white;
    }

    private void BuildOsBar(RectTransform root, TMP_FontAsset font)
    {
        Image bar = CreateImage(
            "OsBar",
            root,
            WithAlpha(_palette.Surface, 0.94f)
        );
        Stretch(bar.rectTransform, new Vector2(0f, OsBarBottom), Vector2.one);

        TMP_Text shellText = CreateText(
            "ShellName",
            bar.transform,
            _shellName.ToUpperInvariant(),
            30f,
            TextAlignmentOptions.MidlineLeft,
            _palette.Text,
            FontStyles.Bold,
            font
        );
        Stretch(shellText.rectTransform, new Vector2(0.024f, 0f), new Vector2(0.24f, 1f));

        _applicationText = CreateText(
            "ApplicationName",
            bar.transform,
            string.Empty,
            18f,
            TextAlignmentOptions.Center,
            WithAlpha(_palette.MutedText, 0.0f),
            FontStyles.Normal,
            font
        );
        Stretch(_applicationText.rectTransform, new Vector2(0.29f, 0f), new Vector2(0.71f, 1f));

        _clockText = CreateText(
            "ConnectionAndClock",
            bar.transform,
            "LINK  ●     00:26",
            25f,
            TextAlignmentOptions.MidlineRight,
            _palette.Accent,
            FontStyles.Bold,
            font
        );
        Stretch(_clockText.rectTransform, new Vector2(0.68f, 0f), new Vector2(0.976f, 1f));

        Image underline = CreateImage(
            "OsBarUnderline",
            root,
            WithAlpha(_palette.Structure, 0.26f)
        );
        Stretch(underline.rectTransform, new Vector2(0f, OsBarBottom - 0.0015f), new Vector2(1f, OsBarBottom));
    }

    private void BuildApplicationHeader(
        RectTransform root,
        TMP_FontAsset font,
        string moduleTitle
    )
    {
        Image header = CreateImage(
            "ApplicationHeader",
            root,
            WithAlpha(_palette.RaisedSurface, 0.72f)
        );
        Stretch(header.rectTransform, new Vector2(0f, HeaderBottom), new Vector2(1f, OsBarBottom));

        TMP_Text moduleText = CreateText(
            "ModuleTitle",
            header.transform,
            (moduleTitle ?? string.Empty).ToUpperInvariant(),
            37f,
            TextAlignmentOptions.MidlineLeft,
            _palette.Accent,
            FontStyles.Bold,
            font
        );
        Stretch(moduleText.rectTransform, new Vector2(0.024f, 0f), new Vector2(0.39f, 1f));

        NetworkText = CreateText(
            "Network",
            header.transform,
            "NODE  UNKNOWN",
            26f,
            TextAlignmentOptions.Center,
            _palette.MutedText,
            FontStyles.Normal,
            font
        );
        Stretch(NetworkText.rectTransform, new Vector2(0.34f, 0f), new Vector2(0.64f, 1f));

        DifficultyText = CreateText(
            "Difficulty",
            header.transform,
            "EASY",
            27f,
            TextAlignmentOptions.MidlineRight,
            _palette.Objective,
            FontStyles.Bold,
            font
        );
        Stretch(DifficultyText.rectTransform, new Vector2(0.64f, 0f), new Vector2(0.77f, 1f));

        StatusText = CreateText(
            "Status",
            header.transform,
            string.Empty,
            29f,
            TextAlignmentOptions.MidlineRight,
            _palette.Text,
            FontStyles.Bold,
            font
        );
        Stretch(StatusText.rectTransform, new Vector2(0.77f, 0f), new Vector2(0.975f, 1f));

        Image progressTrack = CreateImage(
            "HeaderProgressTrack",
            root,
            WithAlpha(_palette.Structure, 0.13f)
        );
        Stretch(progressTrack.rectTransform, new Vector2(0f, HeaderBottom - 0.0036f), new Vector2(1f, HeaderBottom));

        Image progressFill = CreateImage(
            "HeaderProgressFill",
            progressTrack.transform,
            WithAlpha(_palette.Accent, 0.94f)
        );
        _progressFillRect = progressFill.rectTransform;
        SetProgress(0f);
    }

    private void BuildFooter(
        RectTransform root,
        TMP_FontAsset font,
        string footerObjective
    )
    {
        Image footer = CreateImage(
            "ApplicationFooter",
            root,
            WithAlpha(_palette.Surface, 0.93f)
        );
        Stretch(footer.rectTransform, Vector2.zero, new Vector2(1f, FooterTop));

        FooterLeftText = CreateText(
            "FooterObjective",
            footer.transform,
            footerObjective ?? string.Empty,
            25f,
            TextAlignmentOptions.MidlineLeft,
            WithAlpha(_palette.Text, 0.76f),
            FontStyles.Bold,
            font
        );
        Stretch(FooterLeftText.rectTransform, new Vector2(0.024f, 0f), new Vector2(0.74f, 1f));

        FooterRightText = CreateText(
            "FooterDisconnect",
            footer.transform,
            "Q  /DISCONNECT",
            25f,
            TextAlignmentOptions.MidlineRight,
            WithAlpha(_palette.Text, 0.72f),
            FontStyles.Bold,
            font
        );
        Stretch(FooterRightText.rectTransform, new Vector2(0.70f, 0f), new Vector2(0.976f, 1f));

        Image overline = CreateImage(
            "FooterOverline",
            root,
            WithAlpha(_palette.Structure, 0.26f)
        );
        Stretch(overline.rectTransform, new Vector2(0f, FooterTop), new Vector2(1f, FooterTop + 0.0016f));
    }

    private void BuildBriefing(
        RectTransform root,
        TMP_FontAsset font,
        string moduleTitle,
        string briefingDescription,
        string briefingObjective,
        string briefingControls
    )
    {
        _briefingRoot = CreateRect("DesktopBriefing", root);
        Stretch(_briefingRoot, Vector2.zero, new Vector2(1f, OsBarBottom));

        Image desktopShade = CreateImage(
            "DesktopShade",
            _briefingRoot,
            new Color(
                _palette.Background.r,
                _palette.Background.g,
                _palette.Background.b,
                0.90f
            )
        );
        Stretch(desktopShade.rectTransform, Vector2.zero, Vector2.one);

        TMP_Text workspaceText = CreateText(
            "WorkspaceLabel",
            _briefingRoot,
            string.Empty,
            18f,
            TextAlignmentOptions.TopLeft,
            WithAlpha(_palette.MutedText, 0.0f),
            FontStyles.Bold,
            font
        );
        Stretch(workspaceText.rectTransform, new Vector2(0.074f, 0.865f), new Vector2(0.44f, 0.928f));

        TMP_Text sideInfo = CreateText(
            "WorkspaceSideInfo",
            _briefingRoot,
            string.Empty,
            18f,
            TextAlignmentOptions.TopRight,
            WithAlpha(_palette.MutedText, 0.0f),
            FontStyles.Normal,
            font
        );
        Stretch(sideInfo.rectTransform, new Vector2(0.60f, 0.865f), new Vector2(0.90f, 0.928f));

        RectTransform panelBorder = CreateRect("ModulePanelBorder", _briefingRoot);
        Stretch(panelBorder, new Vector2(0.090f, 0.166f), new Vector2(0.900f, 0.796f));
        Image panelBorderImage = panelBorder.gameObject.AddComponent<Image>();
        panelBorderImage.color = WithAlpha(_palette.Structure, 0.16f);
        panelBorderImage.raycastTarget = false;

        RectTransform panel = CreateRect("ModulePanel", panelBorder);
        StretchToParentWithOffsets(panel, 2f, 2f, -2f, -2f);
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.color = WithAlpha(_palette.Background, 0.94f);
        panelImage.raycastTarget = false;

        Image accentRail = CreateImage("AccentRail", panel, WithAlpha(_palette.Accent, 0.0f));
        Stretch(accentRail.rectTransform, new Vector2(0.0f, 0.08f), new Vector2(0.0f, 0.92f));

        Image topLine = CreateImage("PanelTopLine", panel, WithAlpha(_palette.Structure, 0.12f));
        Stretch(topLine.rectTransform, new Vector2(0.0f, 0.985f), new Vector2(1f, 1f));

        TMP_Text suiteLabel = CreateText(
            "SuiteLabel",
            panel,
            string.Empty,
            18f,
            TextAlignmentOptions.MidlineLeft,
            WithAlpha(_palette.MutedText, 0.0f),
            FontStyles.Normal,
            font
        );
        Stretch(suiteLabel.rectTransform, new Vector2(0.055f, 0.846f), new Vector2(0.62f, 0.915f));

        TMP_Text title = CreateText(
            "BriefingTitle",
            panel,
            (moduleTitle ?? string.Empty).ToUpperInvariant(),
            60f,
            TextAlignmentOptions.MidlineLeft,
            _palette.Text,
            FontStyles.Bold,
            font
        );
        Stretch(title.rectTransform, new Vector2(0.055f, 0.655f), new Vector2(0.74f, 0.835f));
        title.overflowMode = TextOverflowModes.Overflow;

        TMP_Text description = CreateText(
            "BriefingDescription",
            panel,
            briefingDescription ?? string.Empty,
            31f,
            TextAlignmentOptions.TopLeft,
            WithAlpha(_palette.Text, 0.78f),
            FontStyles.Normal,
            font
        );
        Stretch(description.rectTransform, new Vector2(0.055f, 0.500f), new Vector2(0.82f, 0.668f));
        description.textWrappingMode = TextWrappingModes.Normal;
        description.overflowMode = TextOverflowModes.Overflow;

        TMP_Text metaText = CreateText(
            "MetaText",
            panel,
            string.Empty,
            18f,
            TextAlignmentOptions.MidlineRight,
            WithAlpha(_palette.MutedText, 0.0f),
            FontStyles.Normal,
            font
        );
        Stretch(metaText.rectTransform, new Vector2(0.60f, 0.835f), new Vector2(0.93f, 0.900f));
        metaText.textWrappingMode = TextWrappingModes.NoWrap;
        metaText.overflowMode = TextOverflowModes.Ellipsis;

        BuildBriefingRow(panel, font, "OBJECTIVE", briefingObjective, 0.330f, _palette.Objective);
        BuildBriefingRow(panel, font, "CONTROL", briefingControls, 0.226f, _palette.Text);

        Image infoDivider = CreateImage("InfoDivider", panel, WithAlpha(_palette.Structure, 0.16f));
        Stretch(infoDivider.rectTransform, new Vector2(0.055f, 0.18f), new Vector2(0.93f, 0.183f));

        _briefingNodeText = CreateText(
            "BriefingNode",
            panel,
            "NODE / UNKNOWN",
            21f,
            TextAlignmentOptions.MidlineLeft,
            WithAlpha(_palette.MutedText, 0.78f),
            FontStyles.Bold,
            font
        );
        Stretch(_briefingNodeText.rectTransform, new Vector2(0.055f, 0.112f), new Vector2(0.42f, 0.168f));

        _briefingDifficultyText = CreateText(
            "BriefingDifficulty",
            panel,
            "PROFILE / EASY",
            21f,
            TextAlignmentOptions.MidlineLeft,
            WithAlpha(_palette.MutedText, 0.78f),
            FontStyles.Bold,
            font
        );
        Stretch(_briefingDifficultyText.rectTransform, new Vector2(0.43f, 0.112f), new Vector2(0.74f, 0.168f));

        _startPromptText = CreateText(
            "StartPrompt",
            panel,
            "[ SPACE ]  EXECUTE MODULE",
            36f,
            TextAlignmentOptions.MidlineRight,
            _palette.Accent,
            FontStyles.Bold,
            font
        );
        Stretch(_startPromptText.rectTransform, new Vector2(0.52f, 0.044f), new Vector2(0.93f, 0.115f));

        TMP_Text promptLead = CreateText(
            "StartPromptLead",
            panel,
            string.Empty,
            18f,
            TextAlignmentOptions.MidlineLeft,
            WithAlpha(_palette.MutedText, 0.0f),
            FontStyles.Normal,
            font
        );
        Stretch(promptLead.rectTransform, new Vector2(0.055f, 0.044f), new Vector2(0.48f, 0.115f));
    }

    private void BuildDesktopDock(RectTransform briefingRoot, TMP_FontAsset font)
    {
        RectTransform dock = CreateRect("DesktopDock", briefingRoot);
        Stretch(
            dock,
            new Vector2(0.018f, 0.185f),
            new Vector2(0.067f, 0.815f)
        );
        Image dockImage = dock.gameObject.AddComponent<Image>();
        dockImage.color = WithAlpha(_palette.Surface, 0.94f);
        dockImage.raycastTarget = false;

        string[] glyphs = { ">", "#", "~", "□" };
        for (int i = 0; i < glyphs.Length; i++)
        {
            float centerY = 0.81f - i * 0.205f;
            float halfHeight = 0.075f;
            Color iconColor = i == 0
                ? WithAlpha(_palette.Accent, 0.92f)
                : WithAlpha(_palette.RaisedSurface, 0.96f);

            Image icon = CreateImage($"DockIcon{i}", dock, iconColor);
            Stretch(
                icon.rectTransform,
                new Vector2(0.16f, centerY - halfHeight),
                new Vector2(0.84f, centerY + halfHeight)
            );

            TMP_Text glyph = CreateText(
                $"DockGlyph{i}",
                icon.transform,
                glyphs[i],
                24f,
                TextAlignmentOptions.Center,
                i == 0 ? _palette.Background : _palette.MutedText,
                FontStyles.Bold,
                font
            );
            Stretch(glyph.rectTransform, Vector2.zero, Vector2.one);
        }
    }

    private void BuildBriefingRow(
        Transform parent,
        TMP_FontAsset font,
        string label,
        string value,
        float bottom,
        Color valueColor
    )
    {
        TMP_Text labelText = CreateText(
            $"{label}Label",
            parent,
            $"> {label}",
            22f,
            TextAlignmentOptions.MidlineLeft,
            _palette.MutedText,
            FontStyles.Bold,
            font
        );
        Stretch(
            labelText.rectTransform,
            new Vector2(0.066f, bottom),
            new Vector2(0.27f, bottom + 0.082f)
        );

        TMP_Text valueText = CreateText(
            $"{label}Value",
            parent,
            value ?? string.Empty,
            29f,
            TextAlignmentOptions.MidlineLeft,
            valueColor,
            FontStyles.Bold,
            font
        );
        Stretch(
            valueText.rectTransform,
            new Vector2(0.275f, bottom),
            new Vector2(0.915f, bottom + 0.082f)
        );
    }

    private void BuildResultOverlay(RectTransform root, TMP_FontAsset font)
    {
        _resultOverlayBackground = CreateImage(
            "ResultOverlay",
            root,
            new Color(
                _palette.Background.r,
                _palette.Background.g,
                _palette.Background.b,
                0.975f
            )
        );
        _resultOverlayRoot = _resultOverlayBackground.rectTransform;
        Stretch(
            _resultOverlayRoot,
            Vector2.zero,
            new Vector2(1f, OsBarBottom)
        );

        _resultRailImage = CreateImage(
            "ResultRail",
            _resultOverlayRoot,
            _palette.Danger
        );
        Stretch(
            _resultRailImage.rectTransform,
            new Vector2(0.245f, 0.30f),
            new Vector2(0.251f, 0.70f)
        );

        TMP_Text eventLabel = CreateText(
            "ResultEventLabel",
            _resultOverlayRoot,
            "> intrusion-suite  ::  session event",
            20f,
            TextAlignmentOptions.BottomLeft,
            _palette.MutedText,
            FontStyles.Bold,
            font
        );
        Stretch(
            eventLabel.rectTransform,
            new Vector2(0.285f, 0.64f),
            new Vector2(0.80f, 0.72f)
        );

        _resultText = CreateText(
            "ResultText",
            _resultOverlayRoot,
            string.Empty,
            64f,
            TextAlignmentOptions.Left,
            _palette.Danger,
            FontStyles.Bold,
            font
        );
        Stretch(
            _resultText.rectTransform,
            new Vector2(0.285f, 0.30f),
            new Vector2(0.84f, 0.64f)
        );
        _resultText.textWrappingMode = TextWrappingModes.Normal;
        _resultText.overflowMode = TextOverflowModes.Overflow;

        _resultOverlayRoot.gameObject.SetActive(false);
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        var gameObject = new GameObject(objectName, typeof(RectTransform));
        var rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static Image CreateImage(
        string objectName,
        Transform parent,
        Color color
    )
    {
        var gameObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        var rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        Image image = gameObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static RawImage CreateRawImage(
        string objectName,
        Transform parent,
        Texture texture
    )
    {
        var gameObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(RawImage)
        );
        var rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        RawImage image = gameObject.GetComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
        return image;
    }

    private static Texture2D CreateAmbientGradientTexture(Color background, Color cool, Color warm)
    {
        const int size = 256;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Vector2 primaryCenter = new Vector2(0.62f, 0.54f);
        Vector2 secondaryCenter = new Vector2(0.28f, 0.34f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 uv = new Vector2(x / (float)(size - 1), y / (float)(size - 1));
                float primary = SmoothInfluence(uv, primaryCenter, 1.05f);
                float secondary = SmoothInfluence(uv, secondaryCenter, 0.82f);
                float edge = Mathf.Abs(uv.x - 0.5f) * 2f;
                float vignette = Mathf.Clamp01(1f - Vector2.Distance(uv, new Vector2(0.5f, 0.5f)) * 0.88f);
                float scan = 1.0f;

                Color color = Color.Lerp(background, cool, 0.08f + primary * 0.085f + secondary * 0.030f);
                color = Color.Lerp(color, warm, secondary * 0.002f);
                color = Color.Lerp(color, background, edge * 0.10f);
                color *= scan;
                color = Color.Lerp(background, color, 0.93f + vignette * 0.07f);

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

    private static TMP_Text CreateText(
        string objectName,
        Transform parent,
        string value,
        float fontSize,
        TextAlignmentOptions alignment,
        Color color,
        FontStyles style,
        TMP_FontAsset font
    )
    {
        var gameObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI)
        );
        var rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        var text = gameObject.GetComponent<TextMeshProUGUI>();
        text.text = value ?? string.Empty;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.fontStyle = style;
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.characterSpacing = 0.35f;

        if (font != null)
            text.font = font;
        else if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        return text;
    }

    private static void StretchToParent(RectTransform rect)
    {
        Stretch(rect, Vector2.zero, Vector2.one);
    }

    private static void Stretch(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax
    )
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void StretchToParentWithOffsets(
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

    private static void SetCenteredRect(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static Color WithAlpha(Color color, float multiplier)
    {
        color.a = Mathf.Clamp01(color.a * multiplier);
        return color;
    }
}
