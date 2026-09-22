using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the arcade combat HUD from code: generates every sprite it needs under Art/UI, derives
/// outlined TextMeshPro material presets from the project font, and rebuilds the canvas hierarchy
/// in place, rebinding every serialized reference on <see cref="CombatHudController"/> and the
/// views so gameplay bindings survive a rebuild. Layout targets a 1920x1080 reference: a bevelled
/// portrait frame and integrated name plate in each top corner, a sheared life bar whose gold to
/// crimson gradient is baked into the sprite so it stays anchored to the bar as the fill drains,
/// a heat gauge beneath it, and a central clock flanked by the round pips of both fighters.
/// </summary>
public static class CombatHudSceneSetup
{
    private const string UiRoot = "Assets/_Project/Art/UI";
    private const string FontsRoot = UiRoot + "/Fonts";
    private const string FontAssetPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    private const string HudRootName = "CombatHUD";

    private const float BarShearDegrees = 14f;

    private const int PortraitSize = 128;
    private const int BarWidth = 700;
    private const int BarHeight = 34;
    private const int GaugeWidth = 620;
    private const int GaugeHeight = 13;
    private const int NamePlateWidth = 340;
    private const int NamePlateHeight = 34;
    private const int PipWidth = 30;
    private const int PipHeight = 12;
    private const int BannerWidth = 1200;
    private const int BannerHeight = 150;

    private const int BevelThickness = 3;
    private const int RoundsPerPlayer = 2;
    private const float EdgeMargin = 42f;
    private const float PanelTop = -30f;
    private const float PortraitToBarGap = 14f;
    private const float BarTopOffset = -6f;

    private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
    private static readonly Color BevelColor = new Color(0.82f, 0.86f, 0.92f, 1f);
    private static readonly Color BackingColor = new Color(0.05f, 0.06f, 0.08f, 0.94f);
    private static readonly Color PortraitPlateColor = new Color(0.09f, 0.1f, 0.13f, 0.96f);
    private static readonly Color GhostColor = new Color(0.72f, 0.06f, 0.12f, 1f);
    private static readonly Color NamePlateColor = new Color(0.04f, 0.045f, 0.06f, 0.9f);
    private static readonly Color GlossColor = new Color(1f, 1f, 1f, 0.18f);
    private static readonly Color PipInactiveColor = new Color(0.18f, 0.2f, 0.24f, 1f);
    private static readonly Color PipActiveColor = new Color(1f, 0.85f, 0.18f, 1f);
    private static readonly Color SlabColor = new Color(0.03f, 0.035f, 0.045f, 0.82f);
    private static readonly Color LabelColor = new Color(0.94f, 0.95f, 0.97f, 1f);
    private static readonly Color DimLabelColor = new Color(0.75f, 0.78f, 0.84f, 1f);
    private static readonly Color TimerColor = new Color(0.86f, 0.95f, 1f, 1f);
    private static readonly Color Player1BustColor = new Color(0.36f, 0.62f, 0.86f, 1f);
    private static readonly Color Player2BustColor = new Color(0.86f, 0.42f, 0.38f, 1f);
    private static readonly Color GaugeLeadColor = new Color(0.85f, 0.98f, 1f, 1f);
    private static readonly Color GaugeFullColor = new Color(0.7f, 0.98f, 1f, 1f);

    private static readonly Color LifeHigh = new Color(1f, 0.85f, 0.18f, 1f);
    private static readonly Color LifeMid = new Color(1f, 0.44f, 0.06f, 1f);
    private static readonly Color LifeLow = new Color(0.86f, 0.08f, 0.13f, 1f);
    private static readonly Color HeatLow = new Color(0.05f, 0.28f, 0.95f, 1f);
    private static readonly Color HeatHigh = new Color(0.45f, 0.95f, 1f, 1f);

    private struct HudSprites
    {
        public Sprite Bar;
        public Sprite BarLife;
        public Sprite BarBevel;
        public Sprite Gauge;
        public Sprite GaugeCharge;
        public Sprite GaugeBevel;
        public Sprite PortraitPlate;
        public Sprite PortraitFrame;
        public Sprite PortraitBust;
        public Sprite NamePlate;
        public Sprite Pip;
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
        EnsureSuperMeters();

        CombatHudController hud = EnsureCanvas();
        Transform root = hud.transform;
        ClearChildren(root);

        TMP_Text timer = BuildRoundClock(root, sprites, fonts, out RectTransform timerTransform, out Image[] p1Pips, out Image[] p2Pips);
        HealthBarView p1Bar = BuildFighterPanel(root, sprites, fonts, false, p1Pips, out SuperGaugeView p1Gauge);
        HealthBarView p2Bar = BuildFighterPanel(root, sprites, fonts, true, p2Pips, out SuperGaugeView p2Gauge);
        ComboCounterView p1Combo = BuildComboPanel(root, fonts, false);
        ComboCounterView p2Combo = BuildComboPanel(root, fonts, true);
        MatchBannerView banner = BuildBanner(root, sprites, fonts);

        var serialized = new SerializedObject(hud);
        serialized.FindProperty("matchManager").objectReferenceValue = Object.FindFirstObjectByType<MatchManager>();
        serialized.FindProperty("p1HealthBar").objectReferenceValue = p1Bar;
        serialized.FindProperty("p2HealthBar").objectReferenceValue = p2Bar;
        serialized.FindProperty("p1SuperGauge").objectReferenceValue = p1Gauge;
        serialized.FindProperty("p2SuperGauge").objectReferenceValue = p2Gauge;
        serialized.FindProperty("p1Combo").objectReferenceValue = p1Combo;
        serialized.FindProperty("p2Combo").objectReferenceValue = p2Combo;
        serialized.FindProperty("bannerView").objectReferenceValue = banner;
        serialized.FindProperty("timerLabel").objectReferenceValue = timer;
        serialized.FindProperty("timerTransform").objectReferenceValue = timerTransform;
        serialized.FindProperty("timerColor").colorValue = TimerColor;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[CombatHudSceneSetup] Combat HUD rebuilt with bevelled portraits, gradient life bars and heat gauges.");
    }

    /// <summary>
    /// Serialises a <see cref="SuperMeterComponent"/> onto every fighter in the scene so the heat
    /// gauges bind to an inspector-tunable meter rather than to one the controller adds at runtime.
    /// </summary>
    private static void EnsureSuperMeters()
    {
        RobotController[] fighters = Object.FindObjectsByType<RobotController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (RobotController fighter in fighters)
        {
            if (fighter.TryGetComponent(out SuperMeterComponent _)) continue;

            fighter.gameObject.AddComponent<SuperMeterComponent>();
            EditorUtility.SetDirty(fighter.gameObject);
        }
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

    // ------------------------------------------------------------------ fighter panel

    /// <summary>
    /// One corner of the HUD: portrait, name plate, life bar and heat gauge. Every graphic lives
    /// under a mirror node scaled by -1 on X for Player 2, so both sides share one set of sprites,
    /// one left fill origin and one gradient direction while draining towards their own screen
    /// edge. Text sits outside the mirror node so it never reads backwards.
    /// </summary>
    private static HealthBarView BuildFighterPanel(Transform root, HudSprites sprites, HudFonts fonts, bool rightSide, Image[] pips, out SuperGaugeView gauge)
    {
        float side = rightSide ? -1f : 1f;
        Vector2 anchor = rightSide ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        Vector2 topLeft = new Vector2(0f, 1f);

        RectTransform panel = CreateRect(rightSide ? "Player2Panel" : "Player1Panel", root, anchor, anchor, anchor, new Vector2(side * EdgeMargin, PanelTop), new Vector2(PortraitSize + PortraitToBarGap + BarWidth, PortraitSize));
        HealthBarView view = panel.gameObject.AddComponent<HealthBarView>();

        RectTransform mirror = CreateRect("Mirror", panel, anchor, anchor, topLeft, Vector2.zero, panel.sizeDelta);
        mirror.localScale = new Vector3(side, 1f, 1f);

        BuildPortrait(mirror, sprites, rightSide);

        float barLeft = PortraitSize + PortraitToBarGap;
        RectTransform bar = CreateRect("LifeBar", mirror, topLeft, topLeft, topLeft, new Vector2(barLeft, BarTopOffset), new Vector2(BarWidth, BarHeight));
        CreateImage("Bevel", bar, sprites.BarBevel, BevelColor, new Vector2(BarWidth + BevelThickness * 2f, BarHeight + BevelThickness * 2f));
        CreateImage("Backing", bar, sprites.Bar, BackingColor, new Vector2(BarWidth, BarHeight));
        Image ghost = CreateFilledImage("GhostBar", bar, sprites.Bar, GhostColor, new Vector2(BarWidth, BarHeight));
        Image fill = CreateFilledImage("ForegroundBar", bar, sprites.BarLife, Color.white, new Vector2(BarWidth, BarHeight));
        Image flash = CreateFilledImage("DamageFlash", bar, sprites.Bar, new Color(1f, 1f, 1f, 0f), new Vector2(BarWidth, BarHeight));
        CreateImage("Gloss", bar, sprites.Gloss, GlossColor, new Vector2(BarWidth - BevelThickness * 2f, BarHeight * 0.45f))
            .rectTransform.anchoredPosition = new Vector2(0f, BarHeight * 0.24f);

        gauge = BuildSuperGauge(mirror, panel, sprites, fonts, rightSide, barLeft);

        RectTransform plate = CreateRect("NamePlate", mirror, topLeft, topLeft, topLeft, new Vector2(PortraitSize * 0.55f, -(PortraitSize - NamePlateHeight)), new Vector2(NamePlateWidth, NamePlateHeight));
        CreateImage("Slab", plate, sprites.NamePlate, NamePlateColor, new Vector2(NamePlateWidth, NamePlateHeight));

        TMP_Text name = CreateLabel("NameLabel", panel, rightSide ? "PLAYER 2" : "PLAYER 1", 25f, fonts.Body,
            rightSide ? TextAlignmentOptions.Right : TextAlignmentOptions.Left, LabelColor, anchor,
            new Vector2(side * (PortraitSize * 0.55f + 26f), -(PortraitSize - NamePlateHeight) - 4f), new Vector2(NamePlateWidth - 48f, NamePlateHeight));
        name.fontStyle |= FontStyles.Italic;

        var serialized = new SerializedObject(view);
        serialized.FindProperty("foregroundHealthBar").objectReferenceValue = fill;
        serialized.FindProperty("ghostHealthBar").objectReferenceValue = ghost;
        serialized.FindProperty("damageFlash").objectReferenceValue = flash;
        serialized.FindProperty("playerNameLabel").objectReferenceValue = name;
        serialized.FindProperty("activePipColor").colorValue = PipActiveColor;
        serialized.FindProperty("inactivePipColor").colorValue = PipInactiveColor;
        serialized.FindProperty("ghostDelay").floatValue = 0.5f;
        SerializedProperty pipArray = serialized.FindProperty("winPips");
        pipArray.arraySize = pips.Length;
        for (int i = 0; i < pips.Length; i++) pipArray.GetArrayElementAtIndex(i).objectReferenceValue = pips[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return view;
    }

    private static void BuildPortrait(RectTransform mirror, HudSprites sprites, bool rightSide)
    {
        Vector2 topLeft = new Vector2(0f, 1f);
        RectTransform portrait = CreateRect("Portrait", mirror, topLeft, topLeft, topLeft, Vector2.zero, new Vector2(PortraitSize, PortraitSize));
        CreateImage("Plate", portrait, sprites.PortraitPlate, PortraitPlateColor, new Vector2(PortraitSize, PortraitSize));
        CreateImage("Bust", portrait, sprites.PortraitBust, rightSide ? Player2BustColor : Player1BustColor, new Vector2(PortraitSize, PortraitSize));
        CreateImage("Frame", portrait, sprites.PortraitFrame, BevelColor, new Vector2(PortraitSize + BevelThickness * 2f, PortraitSize + BevelThickness * 2f));
    }

    /// <summary>
    /// Heat gauge under the life bar: the charge fill carries a baked blue to cyan gradient and a
    /// brighter lead segment rides its leading edge while the gauge sweeps up. The percentage
    /// readout sits outside the mirror node so it reads forwards on both sides.
    /// </summary>
    private static SuperGaugeView BuildSuperGauge(RectTransform mirror, RectTransform panel, HudSprites sprites, HudFonts fonts, bool rightSide, float barLeft)
    {
        Vector2 topLeft = new Vector2(0f, 1f);
        float gaugeLeft = barLeft + (BarWidth - GaugeWidth) * 0.5f;
        float gaugeTop = BarTopOffset - BarHeight - 9f;

        RectTransform gaugeRect = CreateRect("HeatGauge", mirror, topLeft, topLeft, topLeft, new Vector2(gaugeLeft, gaugeTop), new Vector2(GaugeWidth, GaugeHeight));
        SuperGaugeView gauge = gaugeRect.gameObject.AddComponent<SuperGaugeView>();

        CreateImage("Bevel", gaugeRect, sprites.GaugeBevel, BevelColor, new Vector2(GaugeWidth + BevelThickness * 2f, GaugeHeight + BevelThickness * 2f));
        CreateImage("Backing", gaugeRect, sprites.Gauge, BackingColor, new Vector2(GaugeWidth, GaugeHeight));
        Image lead = CreateFilledImage("LeadEdge", gaugeRect, sprites.Gauge, GaugeLeadColor, new Vector2(GaugeWidth, GaugeHeight));
        Image charge = CreateFilledImage("ChargeFill", gaugeRect, sprites.GaugeCharge, Color.white, new Vector2(GaugeWidth, GaugeHeight));
        charge.fillAmount = 0f;
        lead.fillAmount = 0f;

        Vector2 labelAnchor = rightSide ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        TMP_Text chargeLabel = CreateLabel("ChargeLabel", panel, "0", 19f, fonts.Body,
            rightSide ? TextAlignmentOptions.Left : TextAlignmentOptions.Right, DimLabelColor, labelAnchor,
            new Vector2((rightSide ? -1f : 1f) * (gaugeLeft + GaugeWidth + 16f), gaugeTop + 4f), new Vector2(64f, 24f));

        var serialized = new SerializedObject(gauge);
        serialized.FindProperty("chargeFill").objectReferenceValue = charge;
        serialized.FindProperty("leadFill").objectReferenceValue = lead;
        serialized.FindProperty("chargeLabel").objectReferenceValue = chargeLabel;
        serialized.FindProperty("chargeColor").colorValue = Color.white;
        serialized.FindProperty("fullColor").colorValue = GaugeFullColor;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return gauge;
    }

    // ------------------------------------------------------------------ clock

    /// <summary>
    /// Central clock with each fighter's round pips flanking the numerals, so a player reads the
    /// score at the same point they read the time instead of tracking two separate corners.
    /// </summary>
    private static TMP_Text BuildRoundClock(Transform root, HudSprites sprites, HudFonts fonts, out RectTransform timerTransform, out Image[] p1Pips, out Image[] p2Pips)
    {
        Vector2 anchor = new Vector2(0.5f, 1f);
        timerTransform = CreateRect("RoundClock", root, anchor, anchor, anchor, new Vector2(0f, -18f), new Vector2(300f, 130f));

        TMP_Text timer = CreateLabel("TimerLabel", timerTransform, "99", 104f, fonts.Heavy, TextAlignmentOptions.Center, TimerColor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 130f));
        timer.characterSpacing = 0f;

        p1Pips = CreatePipRow(timerTransform, sprites, false);
        p2Pips = CreatePipRow(timerTransform, sprites, true);
        return timer;
    }

    private static Image[] CreatePipRow(RectTransform clock, HudSprites sprites, bool rightSide)
    {
        float side = rightSide ? 1f : -1f;
        var pips = new Image[RoundsPerPlayer];

        for (int i = 0; i < pips.Length; i++)
        {
            Image pip = CreateImage((rightSide ? "P2Pip" : "P1Pip") + i, clock, sprites.Pip, PipInactiveColor, new Vector2(PipWidth, PipHeight));
            pip.rectTransform.anchoredPosition = new Vector2(side * (88f + i * (PipWidth + 8f)), -20f);
            pip.rectTransform.localScale = new Vector3(side, 1f, 1f);
            pips[i] = pip;
        }

        return pips;
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

        RectTransform panel = CreateRect(rightSide ? "Player2Impact" : "Player1Impact", root, anchor, anchor, anchor, new Vector2(side * (EdgeMargin + 16f), -200f), new Vector2(420f, 260f));
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
            Heavy = CreateFontPreset(font, "LiberationSans SDF - HUD Heavy", 0.3f, 0.5f)
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
        preset.SetFloat(ShaderUtilities.ID_OutlineSoftness, 0.04f);
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
            Bar = WriteSprite("T_UIBarAngled", BarWidth, BarHeight, ShearedMask),
            BarLife = WriteSprite("T_UIBarLife", BarWidth, BarHeight, LifeGradient),
            BarBevel = WriteSprite("T_UIBarBevel", BarWidth + BevelThickness * 2, BarHeight + BevelThickness * 2, ShearedBevel),
            Gauge = WriteSprite("T_UIGaugeAngled", GaugeWidth, GaugeHeight, ShearedMask),
            GaugeCharge = WriteSprite("T_UIGaugeCharge", GaugeWidth, GaugeHeight, HeatGradient),
            GaugeBevel = WriteSprite("T_UIGaugeBevel", GaugeWidth + BevelThickness * 2, GaugeHeight + BevelThickness * 2, ShearedBevel),
            PortraitPlate = WriteSprite("T_UIPortraitPlate", PortraitSize, PortraitSize, PortraitPlateMask),
            PortraitFrame = WriteSprite("T_UIPortraitFrame", PortraitSize + BevelThickness * 2, PortraitSize + BevelThickness * 2, PortraitFrameBevel),
            PortraitBust = WriteSprite("T_UIPortraitBust", PortraitSize, PortraitSize, PortraitBustSilhouette),
            NamePlate = WriteSprite("T_UINamePlate", NamePlateWidth, NamePlateHeight, NamePlateMask),
            Pip = WriteSprite("T_UIPipChevron", PipWidth, PipHeight, ShearedMask),
            Gloss = WriteSprite("T_UIGloss", 4, 64, GlossMask),
            Slab = WriteSprite("T_UISlab", BannerWidth, BannerHeight, SlabMask)
        };
    }

    private delegate Color PixelAt(float x, float y, int width, int height);

    /// <summary>
    /// Coverage of a sheared parallelogram at a pixel, inset from every edge by
    /// <paramref name="inset"/> pixels. Returned as coverage rather than a hard test so the
    /// diagonals antialias instead of stepping.
    /// </summary>
    private static float ParallelogramCoverage(float x, float y, int width, int height, float inset)
    {
        float shear = height * Mathf.Tan(BarShearDegrees * Mathf.Deg2Rad);
        float t = y / height;
        float left = t * shear + inset;
        float right = width - shear + t * shear - inset;
        float horizontal = Mathf.Clamp01(x - left) * Mathf.Clamp01(right - x);
        float vertical = Mathf.Clamp01(y - inset) * Mathf.Clamp01(height - inset - y);
        return horizontal * vertical;
    }

    private static Color ShearedMask(float x, float y, int width, int height)
    {
        return new Color(1f, 1f, 1f, ParallelogramCoverage(x, y, width, height, 0f));
    }

    /// <summary>
    /// Hollow bevel ring around the bar silhouette, brightened along the top edge so the frame
    /// reads as brushed metal catching light from above rather than as a flat stroke.
    /// </summary>
    private static Color ShearedBevel(float x, float y, int width, int height)
    {
        float outer = ParallelogramCoverage(x, y, width, height, 0f);
        float inner = ParallelogramCoverage(x, y, width, height, BevelThickness);
        float lift = Mathf.Lerp(0.55f, 1f, y / height);
        return new Color(lift, lift, lift, Mathf.Clamp01(outer - inner));
    }

    /// <summary>
    /// Gold through amber to crimson along the length of the bar, with a vertical sheen. Baking the
    /// ramp into the sprite keeps it anchored to the bar: a vertex ramp would compress towards the
    /// remaining fill as the Filled image shrinks.
    /// </summary>
    private static Color LifeGradient(float x, float y, int width, int height)
    {
        float t = x / width;
        Color tone = t < 0.5f ? Color.Lerp(LifeHigh, LifeMid, t * 2f) : Color.Lerp(LifeMid, LifeLow, (t - 0.5f) * 2f);
        float sheen = 1f + 0.18f * Mathf.Sin(y / height * Mathf.PI);
        tone *= sheen;
        tone.a = ParallelogramCoverage(x, y, width, height, 0f);
        return tone;
    }

    private static Color HeatGradient(float x, float y, int width, int height)
    {
        Color tone = Color.Lerp(HeatLow, HeatHigh, x / width);
        tone.a = ParallelogramCoverage(x, y, width, height, 0f);
        return tone;
    }

    /// <summary>
    /// Portrait silhouette: a square plate whose inner edge is cut back at the HUD's shear angle so
    /// the frame, the name plate and the life bar all share one diagonal.
    /// </summary>
    private static float PortraitCoverage(float x, float y, int width, int height, float inset)
    {
        float cut = width * 0.22f;
        float right = width - inset - cut * (1f - y / height);
        float horizontal = Mathf.Clamp01(x - inset) * Mathf.Clamp01(right - x);
        float vertical = Mathf.Clamp01(y - inset) * Mathf.Clamp01(height - inset - y);
        return horizontal * vertical;
    }

    private static Color PortraitPlateMask(float x, float y, int width, int height)
    {
        float shade = Mathf.Lerp(1f, 0.55f, y / height);
        return new Color(shade, shade, shade, PortraitCoverage(x, y, width, height, 0f));
    }

    private static Color PortraitFrameBevel(float x, float y, int width, int height)
    {
        float outer = PortraitCoverage(x, y, width, height, 0f);
        float inner = PortraitCoverage(x, y, width, height, BevelThickness);
        float lift = Mathf.Lerp(0.5f, 1f, y / height);
        return new Color(lift, lift, lift, Mathf.Clamp01(outer - inner));
    }

    /// <summary>
    /// Stylised bust: a head disc over a shoulder arc, both clipped to the portrait plate, standing
    /// in for a rendered character portrait without a second camera or a render texture.
    /// </summary>
    private static Color PortraitBustSilhouette(float x, float y, int width, int height)
    {
        float u = x / width;
        float v = y / height;

        float headDistance = new Vector2((u - 0.46f) * 1.35f, v - 0.66f).magnitude;
        float head = Mathf.Clamp01((0.19f - headDistance) * height * 0.25f);

        float shoulderDistance = new Vector2((u - 0.46f) * 0.75f, (v - 0.04f) * 1.6f).magnitude;
        float shoulders = Mathf.Clamp01((0.38f - shoulderDistance) * height * 0.25f);

        float body = Mathf.Max(head, shoulders) * PortraitCoverage(x, y, width, height, BevelThickness);
        float shade = Mathf.Lerp(0.45f, 1f, v);
        return new Color(shade, shade, shade, body * 0.85f);
    }

    /// <summary>
    /// Name plate slab: sheared like the bars and faded out along its inner edge so the label
    /// dissolves into the scene instead of ending on a hard rectangle.
    /// </summary>
    private static Color NamePlateMask(float x, float y, int width, int height)
    {
        float coverage = ParallelogramCoverage(x, y, width, height, 0f);
        float fade = 1f - Mathf.SmoothStep(0.55f, 1f, x / width);
        return new Color(1f, 1f, 1f, coverage * fade);
    }

    private static Color GlossMask(float x, float y, int width, int height)
    {
        float t = y / height;
        return new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, t) * t);
    }

    private static Color SlabMask(float x, float y, int width, int height)
    {
        float coverage = ParallelogramCoverage(x, y, width, height, 0f);
        float fade = Mathf.Min(1f, Mathf.Min(x, width - x) / (width * 0.08f));
        return new Color(1f, 1f, 1f, coverage * Mathf.SmoothStep(0f, 1f, fade));
    }

    /// <summary>
    /// Writes a generated texture to Art/UI and reimports it as an uncompressed sprite. Colours are
    /// written straight rather than premultiplied so UGUI's standard alpha blend reproduces the
    /// baked gradients unchanged.
    /// </summary>
    private static Sprite WriteSprite(string name, int width, int height, PixelAt pixelShader)
    {
        string path = $"{UiRoot}/{name}.png";
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color color = pixelShader(x + 0.5f, y + 0.5f, width, height);
                pixels[y * width + x] = new Color32(ToByte(color.r), ToByte(color.g), ToByte(color.b), ToByte(color.a));
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

    private static byte ToByte(float channel)
    {
        return (byte)Mathf.RoundToInt(Mathf.Clamp01(channel) * 255f);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        Directory.CreateDirectory(path);
        AssetDatabase.Refresh();
    }
}
