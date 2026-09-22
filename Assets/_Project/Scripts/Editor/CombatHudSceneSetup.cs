using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the arcade combat HUD from code: generates the angled bar, diamond pip, gloss and banner
/// slab sprites it needs under Art/UI, derives outlined TextMeshPro material presets from the
/// project font, and rebuilds the canvas hierarchy in place, rebinding every serialized reference on
/// <see cref="CombatHudController"/> and the four views so gameplay bindings survive a rebuild.
/// Layout targets a 1920x1080 reference: mirrored life bars in the top corners around a central
/// round clock, per-player impact readouts under each bar, and a centre banner.
/// </summary>
public static class CombatHudSceneSetup
{
    private const string UiRoot = "Assets/_Project/Art/UI";
    private const string FontsRoot = UiRoot + "/Fonts";
    private const string FontAssetPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    private const string HudRootName = "CombatHUD";

    private const int BarWidth = 720;
    private const int BarHeight = 40;
    private const int FrameInset = 4;
    private const float BarShearDegrees = 12f;
    private const int PipSize = 22;
    private const int PipSpacing = 30;
    private const int BannerWidth = 1200;
    private const int BannerHeight = 150;
    private const float EdgeMargin = 56f;
    private const float BarTop = -58f;

    private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
    private static readonly Color FrameColor = new Color(0.02f, 0.02f, 0.03f, 0.95f);
    private static readonly Color BackingColor = new Color(0.07f, 0.08f, 0.1f, 0.92f);
    private static readonly Color GhostColor = new Color(1f, 0.69f, 0f, 1f);
    private static readonly Color Player1Color = new Color(0.12f, 0.85f, 1f, 1f);
    private static readonly Color Player2Color = new Color(1f, 0.3f, 0.32f, 1f);
    private static readonly Color GlossColor = new Color(1f, 1f, 1f, 0.22f);
    private static readonly Color PipInactiveColor = new Color(0.16f, 0.18f, 0.22f, 1f);
    private static readonly Color SlabColor = new Color(0.03f, 0.035f, 0.045f, 0.82f);
    private static readonly Color LabelColor = new Color(0.94f, 0.95f, 0.97f, 1f);
    private static readonly Color DimLabelColor = new Color(0.75f, 0.78f, 0.84f, 1f);

    private struct HudSprites
    {
        public Sprite Bar;
        public Sprite Diamond;
        public Sprite Gloss;
        public Sprite Slab;
    }

    private struct HudFonts
    {
        public TMP_FontAsset Font;
        public Material Body;
        public Material Heavy;
    }

    [MenuItem("Fighting Game/Setup Combat HUD", priority = 55)]
    public static void SetupCombatHud()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != FighterSceneSetup.ScenePath)
            scene = EditorSceneManager.OpenScene(FighterSceneSetup.ScenePath, OpenSceneMode.Single);

        HudSprites sprites = GenerateSprites();
        HudFonts fonts = CreateFontPresets();

        CombatHudController hud = EnsureCanvas();
        Transform root = hud.transform;
        ClearChildren(root);

        HealthBarView p1Bar = BuildHealthBar(root, sprites, fonts, false);
        HealthBarView p2Bar = BuildHealthBar(root, sprites, fonts, true);
        TMP_Text timer = BuildTimer(root, sprites, fonts, out RectTransform timerTransform);
        ComboCounterView p1Combo = BuildComboPanel(root, fonts, false);
        ComboCounterView p2Combo = BuildComboPanel(root, fonts, true);
        MatchBannerView banner = BuildBanner(root, sprites, fonts);

        var serialized = new SerializedObject(hud);
        serialized.FindProperty("matchManager").objectReferenceValue = Object.FindFirstObjectByType<MatchManager>();
        serialized.FindProperty("p1HealthBar").objectReferenceValue = p1Bar;
        serialized.FindProperty("p2HealthBar").objectReferenceValue = p2Bar;
        serialized.FindProperty("p1Combo").objectReferenceValue = p1Combo;
        serialized.FindProperty("p2Combo").objectReferenceValue = p2Combo;
        serialized.FindProperty("bannerView").objectReferenceValue = banner;
        serialized.FindProperty("timerLabel").objectReferenceValue = timer;
        serialized.FindProperty("timerTransform").objectReferenceValue = timerTransform;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[CombatHudSceneSetup] Combat HUD rebuilt with angled life bars, hit readouts and outlined type.");
    }

    // ------------------------------------------------------------------ canvas

    private static CombatHudController EnsureCanvas()
    {
        CombatHudController hud = Object.FindFirstObjectByType<CombatHudController>(FindObjectsInactive.Include);
        GameObject rootObject;

        if (hud != null)
        {
            Canvas existing = hud.GetComponentInParent<Canvas>();
            rootObject = existing != null ? existing.rootCanvas.gameObject : hud.gameObject;
            if (hud.gameObject != rootObject)
            {
                Object.DestroyImmediate(hud);
                hud = null;
            }
        }
        else
        {
            rootObject = GameObject.Find(HudRootName) ?? new GameObject(HudRootName);
        }

        Canvas canvas = rootObject.TryGetComponent(out Canvas canvasComponent) ? canvasComponent : rootObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = false;

        CanvasScaler scaler = rootObject.TryGetComponent(out CanvasScaler scalerComponent) ? scalerComponent : rootObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        if (!rootObject.TryGetComponent(out GraphicRaycaster _)) rootObject.AddComponent<GraphicRaycaster>();
        if (hud == null) hud = rootObject.TryGetComponent(out CombatHudController existingHud) ? existingHud : rootObject.AddComponent<CombatHudController>();

        return hud;
    }

    private static void ClearChildren(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(root.GetChild(i).gameObject);
        }
    }

    // ------------------------------------------------------------------ life bars

    /// <summary>
    /// Panel per player: name above, angled bar (frame, backing, ghost, fill, impact flash, gloss)
    /// and round pips below. Player 2's bar container is mirrored on X about its inner edge so the
    /// same sprite and the same left fill origin drain towards the screen edge; labels sit outside
    /// the mirrored node.
    /// </summary>
    private static HealthBarView BuildHealthBar(Transform root, HudSprites sprites, HudFonts fonts, bool rightSide)
    {
        float side = rightSide ? -1f : 1f;
        Vector2 anchor = rightSide ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        Color playerColor = rightSide ? Player2Color : Player1Color;

        RectTransform panel = CreateRect(rightSide ? "Player2Panel" : "Player1Panel", root, anchor, anchor, anchor, new Vector2(side * EdgeMargin, BarTop), new Vector2(BarWidth, BarHeight));
        HealthBarView view = panel.gameObject.AddComponent<HealthBarView>();

        Vector2 barPivot = rightSide ? new Vector2(0f, 1f) : anchor;
        RectTransform bar = CreateRect("Bar", panel, anchor, anchor, barPivot, Vector2.zero, new Vector2(BarWidth, BarHeight));
        bar.localScale = new Vector3(side, 1f, 1f);

        CreateImage("Frame", bar, sprites.Bar, FrameColor, new Vector2(BarWidth + FrameInset * 2f, BarHeight + FrameInset * 2f));
        CreateImage("Backing", bar, sprites.Bar, BackingColor, new Vector2(BarWidth, BarHeight));
        Image ghost = CreateFilledImage("GhostBar", bar, sprites.Bar, GhostColor, new Vector2(BarWidth, BarHeight));
        Image fill = CreateFilledImage("ForegroundBar", bar, sprites.Bar, playerColor, new Vector2(BarWidth, BarHeight));
        Image flash = CreateFilledImage("DamageFlash", bar, sprites.Bar, new Color(1f, 1f, 1f, 0f), new Vector2(BarWidth, BarHeight));
        CreateImage("Gloss", bar, sprites.Gloss, GlossColor, new Vector2(BarWidth - FrameInset * 2f, BarHeight * 0.5f))
            .rectTransform.anchoredPosition = new Vector2(0f, -FrameInset);

        TMP_Text name = CreateLabel("NameLabel", panel, rightSide ? "PLAYER 2" : "PLAYER 1", 24f, fonts.Body, rightSide ? TextAlignmentOptions.Right : TextAlignmentOptions.Left, LabelColor, anchor, new Vector2(side * 10f, 30f), new Vector2(420f, 28f));

        Image[] pips = new Image[2];
        for (int i = 0; i < pips.Length; i++)
        {
            Image pip = CreateImage("Pip" + i, panel, sprites.Diamond, PipInactiveColor, new Vector2(PipSize, PipSize));
            pip.rectTransform.anchorMin = anchor;
            pip.rectTransform.anchorMax = anchor;
            pip.rectTransform.pivot = anchor;
            pip.rectTransform.anchoredPosition = new Vector2(side * (14f + i * PipSpacing), -(BarHeight + 10f));
            pips[i] = pip;
        }

        var serialized = new SerializedObject(view);
        serialized.FindProperty("foregroundHealthBar").objectReferenceValue = fill;
        serialized.FindProperty("ghostHealthBar").objectReferenceValue = ghost;
        serialized.FindProperty("damageFlash").objectReferenceValue = flash;
        serialized.FindProperty("playerNameLabel").objectReferenceValue = name;
        serialized.FindProperty("activePipColor").colorValue = playerColor;
        serialized.FindProperty("inactivePipColor").colorValue = PipInactiveColor;
        serialized.FindProperty("ghostDelay").floatValue = 0.5f;
        SerializedProperty pipArray = serialized.FindProperty("winPips");
        pipArray.arraySize = pips.Length;
        for (int i = 0; i < pips.Length; i++) pipArray.GetArrayElementAtIndex(i).objectReferenceValue = pips[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return view;
    }

    // ------------------------------------------------------------------ clock

    private static TMP_Text BuildTimer(Transform root, HudSprites sprites, HudFonts fonts, out RectTransform timerTransform)
    {
        Vector2 anchor = new Vector2(0.5f, 1f);
        timerTransform = CreateRect("RoundClock", root, anchor, anchor, anchor, new Vector2(0f, -26f), new Vector2(260f, 110f));

        CreateImage("Slab", timerTransform, sprites.Slab, SlabColor, new Vector2(260f, 96f)).rectTransform.anchoredPosition = new Vector2(0f, -6f);
        TMP_Text timer = CreateLabel("TimerLabel", timerTransform, "99", 96f, fonts.Heavy, TextAlignmentOptions.Center, LabelColor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(260f, 110f));
        return timer;
    }

    // ------------------------------------------------------------------ readouts

    /// <summary>
    /// Per-player impact panel under the bar: the hit-confirm readout (tag + per-hit damage) and,
    /// below it, the combo counter (hits + total damage). Each group scales from its outer edge so
    /// the punch pushes towards the centre of the screen.
    /// </summary>
    private static ComboCounterView BuildComboPanel(Transform root, HudFonts fonts, bool rightSide)
    {
        float side = rightSide ? -1f : 1f;
        Vector2 anchor = rightSide ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        Vector2 pivot = rightSide ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
        TextAlignmentOptions alignment = rightSide ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;

        RectTransform panel = CreateRect(rightSide ? "Player2Impact" : "Player1Impact", root, anchor, anchor, anchor, new Vector2(side * (EdgeMargin + 16f), -150f), new Vector2(420f, 260f));
        ComboCounterView view = panel.gameObject.AddComponent<ComboCounterView>();

        RectTransform readout = CreateRect("HitReadout", panel, anchor, anchor, pivot, new Vector2(0f, -40f), new Vector2(420f, 90f));
        CanvasGroup readoutGroup = readout.gameObject.AddComponent<CanvasGroup>();
        readoutGroup.alpha = 0f;
        readoutGroup.blocksRaycasts = false;
        TMP_Text tag = CreateLabel("HitTagLabel", readout, "", 26f, fonts.Body, alignment, LabelColor, new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(420f, 30f));
        TMP_Text damage = CreateLabel("HitDamageLabel", readout, "-0", 64f, fonts.Heavy, alignment, LabelColor, new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(420f, 70f));

        RectTransform counter = CreateRect("ComboCounter", panel, anchor, anchor, pivot, new Vector2(0f, -170f), new Vector2(420f, 130f));
        CanvasGroup counterGroup = counter.gameObject.AddComponent<CanvasGroup>();
        counterGroup.alpha = 0f;
        counterGroup.blocksRaycasts = false;
        TMP_Text hits = CreateLabel("HitsLabel", counter, "2 HITS", 84f, fonts.Heavy, alignment, LabelColor, new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(420f, 90f));
        TMP_Text total = CreateLabel("DamageLabel", counter, "0 DMG", 30f, fonts.Body, alignment, DimLabelColor, new Vector2(0.5f, 1f), new Vector2(0f, -92f), new Vector2(420f, 34f));

        var serialized = new SerializedObject(view);
        serialized.FindProperty("canvasGroup").objectReferenceValue = counterGroup;
        serialized.FindProperty("hitsLabel").objectReferenceValue = hits;
        serialized.FindProperty("damageLabel").objectReferenceValue = total;
        serialized.FindProperty("punchTransform").objectReferenceValue = counter;
        serialized.FindProperty("readoutGroup").objectReferenceValue = readoutGroup;
        serialized.FindProperty("readoutTransform").objectReferenceValue = readout;
        serialized.FindProperty("hitTagLabel").objectReferenceValue = tag;
        serialized.FindProperty("hitDamageLabel").objectReferenceValue = damage;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return view;
    }

    // ------------------------------------------------------------------ banner

    private static MatchBannerView BuildBanner(Transform root, HudSprites sprites, HudFonts fonts)
    {
        Vector2 centre = new Vector2(0.5f, 0.5f);
        RectTransform panel = CreateRect("MatchBanner", root, centre, centre, centre, new Vector2(0f, 120f), new Vector2(BannerWidth, 220f));
        CanvasGroup group = panel.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        MatchBannerView view = panel.gameObject.AddComponent<MatchBannerView>();

        RectTransform slab = CreateRect("Slab", panel, centre, centre, centre, Vector2.zero, new Vector2(BannerWidth, BannerHeight));
        CreateImage("Backdrop", slab, sprites.Slab, SlabColor, new Vector2(BannerWidth, BannerHeight));
        TMP_Text main = CreateLabel("MainText", slab, "ROUND 1", 118f, fonts.Heavy, TextAlignmentOptions.Center, LabelColor, centre, new Vector2(0f, 14f), new Vector2(BannerWidth, 130f));
        TMP_Text sub = CreateLabel("SubText", slab, "PRESS START TO REMATCH", 30f, fonts.Body, TextAlignmentOptions.Center, DimLabelColor, centre, new Vector2(0f, -60f), new Vector2(BannerWidth, 36f));

        var serialized = new SerializedObject(view);
        serialized.FindProperty("canvasGroup").objectReferenceValue = group;
        serialized.FindProperty("bannerTransform").objectReferenceValue = slab;
        serialized.FindProperty("mainBannerText").objectReferenceValue = main;
        serialized.FindProperty("subBannerText").objectReferenceValue = sub;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return view;
    }

    // ------------------------------------------------------------------ element helpers

    private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        return rect;
    }

    private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size)
    {
        Vector2 centre = new Vector2(0.5f, 0.5f);
        RectTransform rect = CreateRect(name, parent, centre, centre, centre, Vector2.zero, size);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        image.type = Image.Type.Simple;
        image.preserveAspect = false;
        return image;
    }

    private static Image CreateFilledImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size)
    {
        Image image = CreateImage(name, parent, sprite, color, size);
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Horizontal;
        image.fillOrigin = (int)Image.OriginHorizontal.Left;
        image.fillAmount = 1f;
        return image;
    }

    private static TMP_Text CreateLabel(string name, Transform parent, string text, float size, Material preset, TextAlignmentOptions alignment, Color color, Vector2 pivot, Vector2 anchoredPosition, Vector2 rectSize)
    {
        Vector2 anchor = pivot;
        RectTransform rect = CreateRect(name, parent, anchor, anchor, pivot, anchoredPosition, rectSize);
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        label.alignment = alignment;
        label.color = color;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.characterSpacing = 2f;
        if (preset != null) label.fontSharedMaterial = preset;
        return label;
    }

    // ------------------------------------------------------------------ fonts

    /// <summary>
    /// Two presets derived from the project font's own material (so the atlas stays bound): a body
    /// preset with a thin outline and drop shadow for names and tags, and a heavy preset with a
    /// thicker outline for the clock, the combo counter and the banner.
    /// </summary>
    private static HudFonts CreateFontPresets()
    {
        EnsureFolder(FontsRoot);
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (font == null)
        {
            Debug.LogError($"[CombatHudSceneSetup] Font asset not found at '{FontAssetPath}'.");
            return default;
        }

        return new HudFonts
        {
            Font = font,
            Body = CreateFontPreset(font, "LiberationSans SDF - HUD Body", 0.16f, 0.35f),
            Heavy = CreateFontPreset(font, "LiberationSans SDF - HUD Heavy", 0.24f, 0.5f)
        };
    }

    private static Material CreateFontPreset(TMP_FontAsset font, string name, float outlineWidth, float underlayAlpha)
    {
        string path = $"{FontsRoot}/{name}.mat";
        Material preset = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (preset == null)
        {
            preset = new Material(font.material);
            AssetDatabase.CreateAsset(preset, path);
        }

        preset.shader = font.material.shader;
        preset.SetTexture(ShaderUtilities.ID_MainTex, font.material.GetTexture(ShaderUtilities.ID_MainTex));
        preset.SetFloat(ShaderUtilities.ID_FaceDilate, 0.06f);
        preset.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
        preset.SetFloat(ShaderUtilities.ID_OutlineSoftness, 0.05f);
        preset.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.02f, 0.02f, 0.03f, 1f));
        preset.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        preset.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, underlayAlpha));
        preset.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.35f);
        preset.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.45f);
        preset.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.15f);
        preset.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.3f);
        EditorUtility.SetDirty(preset);
        return preset;
    }

    // ------------------------------------------------------------------ sprites

    private static HudSprites GenerateSprites()
    {
        EnsureFolder(UiRoot);

        return new HudSprites
        {
            Bar = WriteSprite("T_UIBarAngled", BarWidth, BarHeight, ParallelogramAlpha),
            Diamond = WriteSprite("T_UIDiamond", 64, 64, DiamondAlpha),
            Gloss = WriteSprite("T_UIGloss", 4, 64, GlossAlpha),
            Slab = WriteSprite("T_UISlab", BannerWidth, BannerHeight, SlabAlpha)
        };
    }

    private delegate float AlphaAt(int x, int y, int width, int height);

    private static float ParallelogramAlpha(int x, int y, int width, int height)
    {
        float shear = height * Mathf.Tan(BarShearDegrees * Mathf.Deg2Rad);
        float t = (y + 0.5f) / height;
        float left = t * shear;
        float right = width - shear + t * shear;
        return Mathf.Clamp01(x + 0.5f - left) * Mathf.Clamp01(right - (x + 0.5f));
    }

    private static float DiamondAlpha(int x, int y, int width, int height)
    {
        float dx = Mathf.Abs(x + 0.5f - width * 0.5f);
        float dy = Mathf.Abs(y + 0.5f - height * 0.5f);
        float radius = width * 0.5f - 1f;
        return Mathf.Clamp01(radius - (dx + dy) + 0.5f);
    }

    private static float GlossAlpha(int x, int y, int width, int height)
    {
        float t = (y + 0.5f) / height;
        return Mathf.SmoothStep(0f, 1f, t) * t;
    }

    private static float SlabAlpha(int x, int y, int width, int height)
    {
        float shear = height * Mathf.Tan(BarShearDegrees * Mathf.Deg2Rad);
        float t = (y + 0.5f) / height;
        float left = t * shear;
        float right = width - shear + t * shear;
        float edge = Mathf.Clamp01(x + 0.5f - left) * Mathf.Clamp01(right - (x + 0.5f));
        float fade = Mathf.Min(1f, Mathf.Min(x, width - 1 - x) / (width * 0.08f));
        return edge * Mathf.SmoothStep(0f, 1f, fade);
    }

    private static Sprite WriteSprite(string name, int width, int height, AlphaAt alpha)
    {
        string path = $"{UiRoot}/{name}.png";
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(x, y, width, height)) * 255f);
                pixels[y * width + x] = new Color32(255, 255, 255, a);
            }
        }

        texture.SetPixels32(pixels);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        if (AssetImporter.GetAtPath(path) is TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        Directory.CreateDirectory(path);
        AssetDatabase.Refresh();
    }
}
