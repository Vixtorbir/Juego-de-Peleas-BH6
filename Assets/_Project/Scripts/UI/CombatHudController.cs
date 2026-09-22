using UnityEngine;

/// <summary>
/// Presenter for the arcade combat HUD. Binds the life bars, combo counters, round clock and
/// announcement banner to the match and the two fighters through their events, so the HUD never
/// polls gameplay state. Numeric readouts come from string tables built once, keeping the combat
/// loop free of managed allocations. The presenter is ordered after the gameplay scripts so it
/// enables once the fighters have resolved their components, yet before the match raises its
/// first events from Start.
/// </summary>
[DefaultExecutionOrder(ExecutionOrder)]
public class CombatHudController : MonoBehaviour
{
    public const int ExecutionOrder = 100;

    private const int MaxTimerSeconds = 99;

    private static readonly string[] TimerLookup = BuildTimerLookup();

    [Header("Match")]
    [SerializeField] private MatchManager matchManager;
    [SerializeField] private RobotController player1;
    [SerializeField] private RobotController player2;

    [Header("Views")]
    [SerializeField] private HealthBarView p1HealthBar;
    [SerializeField] private HealthBarView p2HealthBar;
    [SerializeField] private ComboCounterView p1Combo;
    [SerializeField] private ComboCounterView p2Combo;
    [SerializeField] private MatchBannerView bannerView;
    [SerializeField] private TMPro.TMP_Text timerLabel;

    [Header("Round Clock")]
    [SerializeField] private RectTransform timerTransform;
    [Tooltip("Seconds left at which the clock turns critical: recoloured and pulsing.")]
    [SerializeField] private int criticalSeconds = 10;
    [SerializeField] private Color timerColor = Color.white;
    [SerializeField] private Color criticalTimerColor = new Color(1f, 0.23f, 0.23f);
    [SerializeField] private float criticalPulseFrequency = 2f;
    [SerializeField] private float criticalPulseScale = 0.08f;

    public HealthBarView Player1HealthBar => p1HealthBar;
    public HealthBarView Player2HealthBar => p2HealthBar;
    public ComboCounterView Player1Combo => p1Combo;
    public ComboCounterView Player2Combo => p2Combo;
    public MatchBannerView Banner => bannerView;
    public string TimerText => timerLabel != null ? timerLabel.text : string.Empty;
    public bool IsClockCritical => critical;

    private bool critical;

    private void OnEnable()
    {
        if (matchManager == null) matchManager = MatchManager.Instance;
        if (matchManager != null)
        {
            if (player1 == null) player1 = matchManager.player1;
            if (player2 == null) player2 = matchManager.player2;

            matchManager.OnMatchReset += HandleMatchReset;
            matchManager.OnTimerTick += HandleTimerTick;
            matchManager.OnRoundEnd += HandleRoundEnd;
            if (bannerView != null) bannerView.Bind(matchManager);
        }

        BindFighters();
        SyncFromMatch();
    }

    private void OnDisable()
    {
        if (matchManager != null)
        {
            matchManager.OnMatchReset -= HandleMatchReset;
            matchManager.OnTimerTick -= HandleTimerTick;
            matchManager.OnRoundEnd -= HandleRoundEnd;
        }

        if (bannerView != null) bannerView.Unbind();
        if (p1HealthBar != null) p1HealthBar.Unbind();
        if (p2HealthBar != null) p2HealthBar.Unbind();
        if (p1Combo != null) p1Combo.Unbind();
        if (p2Combo != null) p2Combo.Unbind();
    }

    /// <summary>
    /// Each combo counter belongs to the attacker, so it listens to the opponent's hurtbox.
    /// </summary>
    private void BindFighters()
    {
        if (p1HealthBar != null && player1 != null) p1HealthBar.Bind(player1.Health);
        if (p2HealthBar != null && player2 != null) p2HealthBar.Bind(player2.Health);
        if (p1Combo != null && player2 != null) p1Combo.Bind(player2.Hurtbox);
        if (p2Combo != null && player1 != null) p2Combo.Bind(player1.Hurtbox);
    }

    private void SyncFromMatch()
    {
        if (matchManager == null) return;

        HandleTimerTick(matchManager.SecondsRemaining);
        RefreshRoundWins();
    }

    private void HandleMatchReset()
    {
        RefreshRoundWins();
    }

    private void HandleRoundEnd(RobotController winner)
    {
        RefreshRoundWins();
    }

    private void Update()
    {
        if (timerTransform == null) return;

        float scale = 1f;
        if (critical)
        {
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * criticalPulseFrequency * Mathf.PI * 2f);
            scale += criticalPulseScale * wave;
        }

        timerTransform.localScale = new Vector3(scale, scale, 1f);
    }

    /// <summary>
    /// The label text comes from the prebuilt table; the colour swaps between two cached values
    /// when the clock crosses the critical threshold, so the tick never allocates.
    /// </summary>
    private void HandleTimerTick(int seconds)
    {
        if (timerLabel == null) return;

        timerLabel.text = TimerLookup[Mathf.Clamp(seconds, 0, MaxTimerSeconds)];

        bool nowCritical = seconds <= criticalSeconds && seconds >= 0;
        if (nowCritical == critical && timerLabel.color == (critical ? criticalTimerColor : timerColor)) return;

        critical = nowCritical;
        timerLabel.color = critical ? criticalTimerColor : timerColor;
    }

    private void RefreshRoundWins()
    {
        if (p1HealthBar != null) p1HealthBar.SetRoundWins(matchManager.Player1RoundsWon);
        if (p2HealthBar != null) p2HealthBar.SetRoundWins(matchManager.Player2RoundsWon);
    }

    private static string[] BuildTimerLookup()
    {
        var lookup = new string[MaxTimerSeconds + 1];
        for (int i = 0; i < lookup.Length; i++) lookup[i] = i.ToString("00");
        return lookup;
    }
}
