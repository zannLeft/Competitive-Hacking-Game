using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime-built portrait interface for the survivor phone's network scanner.
/// The phone intentionally keeps a much simpler hierarchy than the laptop:
/// the network names and signal strengths occupy nearly the entire display.
/// </summary>
internal sealed class PhoneNetworkVisualShell
{
    internal readonly struct Palette
    {
        public Palette(
            Color background,
            Color surface,
            Color raisedSurface,
            Color structure,
            Color accent,
            Color objective,
            Color text,
            Color muted
        )
        {
            Background = background;
            Surface = surface;
            RaisedSurface = raisedSurface;
            Structure = structure;
            Accent = accent;
            Objective = objective;
            Text = text;
            Muted = muted;
        }

        public Color Background { get; }
        public Color Surface { get; }
        public Color RaisedSurface { get; }
        public Color Structure { get; }
        public Color Accent { get; }
        public Color Objective { get; }
        public Color Text { get; }
        public Color Muted { get; }
    }

    private readonly Palette _palette;
    private readonly string _shellName;

    private TMP_Text _scanStateText;
    private TMP_Text _countText;
    private TMP_Text _emptyTitleText;
    private TMP_Text _emptyBodyText;
    private RectTransform _emptyRoot;

    private PhoneNetworkVisualShell(Palette palette, string shellName)
    {
        _palette = palette;
        _shellName = string.IsNullOrWhiteSpace(shellName) ? "ZannOS" : shellName.Trim();
    }

    public RectTransform Root { get; private set; }
    public RectTransform ContentRoot { get; private set; }
    public Palette Colors => _palette;

    public static PhoneNetworkVisualShell Build(
        RectTransform canvasRoot,
        TMP_FontAsset font,
        string shellName
    )
    {
        if (canvasRoot == null)
            throw new ArgumentNullException(nameof(canvasRoot));

        Palette palette = new(
            new Color(0.004f, 0.022f, 0.018f, 1f),
            new Color(0.009f, 0.035f, 0.029f, 1f),
            new Color(0.013f, 0.046f, 0.038f, 1f),
            new Color(0.190f, 0.770f, 0.660f, 1f),
            new Color(0.740f, 1.000f, 0.920f, 1f),
            new Color(0.540f, 0.965f, 0.885f, 1f),
            new Color(0.910f, 0.995f, 0.960f, 1f),
            new Color(0.230f, 0.530f, 0.470f, 1f)
        );

        var shell = new PhoneNetworkVisualShell(palette, shellName);
        shell.BuildInternal(canvasRoot, font);
        return shell;
    }

    public void Tick(float unscaledTime)
    {
        if (_scanStateText == null)
            return;

        int pulse = Mathf.FloorToInt(unscaledTime * 2f) % 4;
        _scanStateText.text = pulse switch
        {
            0 => "•",
            1 => "••",
            2 => "•••",
            _ => string.Empty,
        };
    }

    public void SetSummary(int networkCount, int bestBars, int totalBars, bool hasReadyNetwork)
    {
        if (_countText != null)
            _countText.text = networkCount == 1 ? "1 FOUND" : $"{networkCount} FOUND";

        SetEmpty(networkCount == 0);
    }

    private void SetEmpty(bool empty)
    {
        if (_emptyRoot != null)
            _emptyRoot.gameObject.SetActive(empty);

        if (_emptyTitleText != null)
            _emptyTitleText.text = "NO NETWORKS FOUND";

        if (_emptyBodyText != null)
            _emptyBodyText.text = "KEEP MOVING TO SEARCH";
    }

    private void BuildInternal(RectTransform canvasRoot, TMP_FontAsset font)
    {
        Root = CreateRect("ZannOSPhoneNetworkShell", canvasRoot);
        Stretch(Root, Vector2.zero, Vector2.one);
        Root.SetAsLastSibling();

        Image background = CreateImage("Background", Root, _palette.Background);
        Stretch(background.rectTransform, Vector2.zero, Vector2.one);

        RawImage ambient = CreateRawImage(
            "AmbientGradient",
            Root,
            CreateAmbientTexture(_palette.Background, _palette.Surface, _palette.Structure)
        );
        Stretch(ambient.rectTransform, Vector2.zero, Vector2.one);
        ambient.color = Color.white;

        BuildTopBar(font);
        BuildHeader(font);
        BuildList();
        BuildEmptyState(font);
    }

    private void BuildTopBar(TMP_FontAsset font)
    {
        Image topBar = CreateImage("TopBar", Root, WithAlpha(_palette.Surface, 0.96f));
        Stretch(topBar.rectTransform, new Vector2(0f, 0.938f), Vector2.one);

        TMP_Text shell = CreateText(
            "ShellName",
            topBar.transform,
            _shellName.ToUpperInvariant(),
            29f,
            TextAlignmentOptions.MidlineLeft,
            _palette.Text,
            FontStyles.Bold,
            font
        );
        Stretch(shell.rectTransform, new Vector2(0.045f, 0f), new Vector2(0.46f, 1f));

        TMP_Text scanLabel = CreateText(
            "ScanLabel",
            topBar.transform,
            "SCAN",
            24f,
            TextAlignmentOptions.MidlineRight,
            _palette.Accent,
            FontStyles.Bold,
            font
        );
        Stretch(scanLabel.rectTransform, new Vector2(0.63f, 0f), new Vector2(0.82f, 1f));

        _scanStateText = CreateText(
            "ScanDots",
            topBar.transform,
            "•",
            24f,
            TextAlignmentOptions.MidlineLeft,
            _palette.Accent,
            FontStyles.Bold,
            font
        );
        Stretch(_scanStateText.rectTransform, new Vector2(0.835f, 0f), new Vector2(0.955f, 1f));

        Image underline = CreateImage("TopBarUnderline", Root, WithAlpha(_palette.Structure, 0.18f));
        Stretch(underline.rectTransform, new Vector2(0f, 0.936f), new Vector2(1f, 0.938f));
    }

    private void BuildHeader(TMP_FontAsset font)
    {
        RectTransform header = CreateRect("Header", Root);
        Stretch(header, new Vector2(0.055f, 0.820f), new Vector2(0.945f, 0.925f));

        TMP_Text title = CreateText(
            "Title",
            header,
            "NETWORKS",
            52f,
            TextAlignmentOptions.MidlineLeft,
            _palette.Text,
            FontStyles.Bold,
            font
        );
        Stretch(title.rectTransform, new Vector2(0f, 0f), new Vector2(0.68f, 1f));

        _countText = CreateText(
            "NetworkCount",
            header,
            "0 FOUND",
            25f,
            TextAlignmentOptions.MidlineRight,
            WithAlpha(_palette.Objective, 0.88f),
            FontStyles.Bold,
            font
        );
        Stretch(_countText.rectTransform, new Vector2(0.60f, 0f), new Vector2(1f, 1f));

        Image line = CreateImage("HeaderLine", Root, WithAlpha(_palette.Structure, 0.18f));
        Stretch(line.rectTransform, new Vector2(0f, 0.814f), new Vector2(1f, 0.816f));
    }

    private void BuildList()
    {
        RectTransform scrollRoot = CreateRect("NetworksScroll", Root);
        Stretch(scrollRoot, new Vector2(0.025f, 0.035f), new Vector2(0.975f, 0.805f));

        Image scrollBackground = scrollRoot.gameObject.AddComponent<Image>();
        scrollBackground.color = WithAlpha(_palette.Background, 0.20f);
        scrollBackground.raycastTarget = false;

        ScrollRect scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.inertia = true;
        scroll.decelerationRate = 0.14f;
        scroll.scrollSensitivity = 40f;

        RectTransform viewport = CreateRect("Viewport", scrollRoot);
        Stretch(viewport, Vector2.zero, Vector2.one);
        Image viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = Color.clear;
        viewportImage.raycastTarget = true;
        viewport.gameObject.AddComponent<RectMask2D>();

        ContentRoot = CreateRect("Content", viewport);
        ContentRoot.anchorMin = new Vector2(0f, 1f);
        ContentRoot.anchorMax = new Vector2(1f, 1f);
        ContentRoot.pivot = new Vector2(0.5f, 1f);
        ContentRoot.offsetMin = new Vector2(4f, 0f);
        ContentRoot.offsetMax = new Vector2(-4f, 0f);
        ContentRoot.sizeDelta = new Vector2(-8f, 0f);

        VerticalLayoutGroup layout = ContentRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 10, 14);
        layout.spacing = 9f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = ContentRoot.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport;
        scroll.content = ContentRoot;
    }

    private void BuildEmptyState(TMP_FontAsset font)
    {
        _emptyRoot = CreateRect("EmptyState", Root);
        Stretch(_emptyRoot, new Vector2(0.10f, 0.30f), new Vector2(0.90f, 0.67f));

        _emptyTitleText = CreateText(
            "EmptyTitle",
            _emptyRoot,
            "NO NETWORKS FOUND",
            42f,
            TextAlignmentOptions.Center,
            _palette.Text,
            FontStyles.Bold,
            font
        );
        Stretch(_emptyTitleText.rectTransform, new Vector2(0f, 0.48f), new Vector2(1f, 0.80f));

        _emptyBodyText = CreateText(
            "EmptyBody",
            _emptyRoot,
            "KEEP MOVING TO SEARCH",
            24f,
            TextAlignmentOptions.Top,
            WithAlpha(_palette.Muted, 0.82f),
            FontStyles.Normal,
            font
        );
        Stretch(_emptyBodyText.rectTransform, new Vector2(0.08f, 0.18f), new Vector2(0.92f, 0.50f));
        _emptyBodyText.textWrappingMode = TextWrappingModes.Normal;
    }

    private static Texture2D CreateAmbientTexture(Color background, Color surface, Color structure)
    {
        const int width = 64;
        const int height = 128;
        Texture2D texture = new(width, height, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Vector2 center = new(0.58f, 0.58f);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2 uv = new(x / (float)(width - 1), y / (float)(height - 1));
                float vertical = Mathf.Lerp(0.08f, 0.19f, uv.y);
                float glow = Mathf.Clamp01(1f - Vector2.Distance(uv, center) / 0.95f);
                glow = glow * glow * (3f - 2f * glow);
                float edge = Mathf.Abs(uv.x - 0.5f) * 2f;

                Color color = Color.Lerp(background, surface, vertical);
                color = Color.Lerp(color, structure, glow * 0.025f);
                color = Color.Lerp(color, background, edge * 0.07f);
                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply(false, true);
        return texture;
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

    private static RawImage CreateRawImage(string name, Transform parent, Texture texture)
    {
        GameObject obj = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        obj.layer = parent.gameObject.layer;
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
        text.characterSpacing = 0.20f;

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
