using UnityEngine;

/// <summary>
/// Centre-screen announcements driven by <see cref="MatchManager"/> events: the round call,
/// the fight call, the knockout or time-over stamp and the match result with its rematch hint.
/// Each announcement scales in from an authored punch and either fades after a hold or stays
/// until the next one replaces it. Motion runs on unscaled time so the stamps land during the
/// knockout hitstop.
/// </summary>
public class MatchBannerView : MonoBehaviour
{
    private const float RoundCallStartScale = 1.6f;
    private const float RoundCallHold = 1.1f;
    private const float FightCallStartScale = 2.4f;
    private const float FightCallHold = 0.35f;
    private const float FightCallFade = 0.25f;
    private const float StampStartScale = 1.8f;
    private const float ResultStartScale = 1.4f;
    private const float PersistentHold = -1f;
    private const int MaxRoundLabels = 9;

    private const string FinalRoundText = "FINAL ROUND";
    private const string FightText = "FIGHT!";
    private const string KnockoutText = "K.O.";
    private const string TimeOverText = "TIME OVER";
    private const string Player1WinsText = "PLAYER 1 WINS";
    private const string Player2WinsText = "PLAYER 2 WINS";
    private const string DrawText = "DRAW";
    private const string RematchHint = "PRESS START TO REMATCH";

    private static readonly string[] RoundLookup = BuildRoundLookup();

    [Header("References")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform bannerTransform;
    [SerializeField] private TMPro.TMP_Text mainBannerText;
    [SerializeField] private TMPro.TMP_Text subBannerText;

    [Header("Motion")]
    [SerializeField] private float punchDuration = 0.22f;
    [SerializeField] private float fadeDuration = 0.4f;

    private MatchManager boundMatch;
    private float startScale;
    private float punchTimer;
    private float holdTimer;
    private float fadeTimer;
    private float activeFadeDuration;
    private bool timeOverPending;

    public string MainText => mainBannerText != null ? mainBannerText.text : string.Empty;
    public float Alpha => canvasGroup != null ? canvasGroup.alpha : 0f;

    public void Bind(MatchManager match)
    {
        Unbind();
        boundMatch = match;
        if (boundMatch == null) return;

        boundMatch.OnRoundStart += HandleRoundStart;
        boundMatch.OnFightStart += HandleFightStart;
        boundMatch.OnTimeOver += HandleTimeOver;
        boundMatch.OnRoundEnd += HandleRoundEnd;
        boundMatch.OnMatchOver += HandleMatchOver;
    }

    public void Unbind()
    {
        if (boundMatch == null) return;

        boundMatch.OnRoundStart -= HandleRoundStart;
        boundMatch.OnFightStart -= HandleFightStart;
        boundMatch.OnTimeOver -= HandleTimeOver;
        boundMatch.OnRoundEnd -= HandleRoundEnd;
        boundMatch.OnMatchOver -= HandleMatchOver;
        boundMatch = null;
    }

    private void Awake()
    {
        if (canvasGroup != null) canvasGroup.alpha = 0f;
    }

    private void OnDisable()
    {
        Unbind();
    }

    private void Update()
    {
        float deltaTime = Time.unscaledDeltaTime;

        if (punchTimer > 0f && bannerTransform != null)
        {
            punchTimer = Mathf.Max(0f, punchTimer - deltaTime);
            float scale = Mathf.Lerp(startScale, 1f, EaseOutCubic(1f - punchTimer / punchDuration));
            bannerTransform.localScale = new Vector3(scale, scale, 1f);
            return;
        }

        if (holdTimer > 0f)
        {
            holdTimer -= deltaTime;
            if (holdTimer <= 0f) fadeTimer = activeFadeDuration;
            return;
        }

        if (fadeTimer > 0f && canvasGroup != null)
        {
            fadeTimer = Mathf.Max(0f, fadeTimer - deltaTime);
            canvasGroup.alpha = fadeTimer / activeFadeDuration;
        }
    }

    private void HandleRoundStart(int round)
    {
        timeOverPending = false;
        bool finalRound = boundMatch != null && round >= boundMatch.FinalRoundNumber;
        Show(finalRound ? FinalRoundText : RoundLookup[Mathf.Clamp(round, 1, MaxRoundLabels)], string.Empty, RoundCallStartScale, RoundCallHold, fadeDuration);
    }

    private void HandleFightStart()
    {
        Show(FightText, string.Empty, FightCallStartScale, FightCallHold, FightCallFade);
    }

    private void HandleTimeOver()
    {
        timeOverPending = true;
    }

    private void HandleRoundEnd(RobotController winner)
    {
        Show(timeOverPending ? TimeOverText : KnockoutText, string.Empty, StampStartScale, PersistentHold, fadeDuration);
        timeOverPending = false;
    }

    private void HandleMatchOver(RobotController winner)
    {
        string result = DrawText;
        if (boundMatch != null && winner != null)
        {
            result = winner == boundMatch.player1 ? Player1WinsText : Player2WinsText;
        }

        Show(result, RematchHint, ResultStartScale, PersistentHold, fadeDuration);
    }

    /// <summary>
    /// A negative hold keeps the banner on screen until the next announcement replaces it.
    /// </summary>
    private void Show(string main, string sub, float scaleFrom, float hold, float fade)
    {
        if (mainBannerText != null) mainBannerText.text = main;
        if (subBannerText != null) subBannerText.text = sub;
        if (canvasGroup != null) canvasGroup.alpha = 1f;

        startScale = scaleFrom;
        punchTimer = punchDuration;
        holdTimer = hold;
        fadeTimer = 0f;
        activeFadeDuration = Mathf.Max(fade, 0.0001f);
    }

    private static float EaseOutCubic(float t)
    {
        float inverse = 1f - t;
        return 1f - inverse * inverse * inverse;
    }

    private static string[] BuildRoundLookup()
    {
        var lookup = new string[MaxRoundLabels + 1];
        for (int i = 0; i < lookup.Length; i++) lookup[i] = "ROUND " + i;
        return lookup;
    }
}
