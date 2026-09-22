using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;

/// <summary>
/// Drives the round flow of a match: places both fighters, locks input during the intro, runs
/// the round clock, reacts to knockouts through each fighter's <see cref="HealthComponent.OnDeath"/>
/// or to time over, and tallies round wins until one side reaches <see cref="roundsToWin"/>.
/// Every transition is broadcast through C# events so presentation layers (HUD, announcer) bind
/// once instead of polling. Fighters are injected so a future
/// character select can call <see cref="InitializeMatch"/>. While fighting, Start (or Escape and
/// Enter) toggles the pause; once the match is over, Start or Enter restarts it from round one.
/// </summary>
public class MatchManager : MonoBehaviour
{
    private const float IntroDuration = 2f;
    private const float RoundEndDuration = 4f;
    private const string JoystickStartButton = "button10";

    public enum MatchState { Intro, Fighting, RoundEnd, MatchOver }

    public static MatchManager Instance { get; private set; }

    [Header("Arena Setup")]
    public Transform p1SpawnPoint;
    public Transform p2SpawnPoint;
    public int roundsToWin = 2;
    [Tooltip("Round clock in seconds. It only runs while both fighters are in control and freezes with hitstop.")]
    [SerializeField] private float roundDuration = 99f;

    [Header("Fighters")]
    public RobotController player1;
    public RobotController player2;

    /// <summary>Raised when a match is (re)initialised, before the first round's intro.</summary>
    public event Action OnMatchReset;

    /// <summary>Raised with the 1-based round number when a round's intro begins.</summary>
    public event Action<int> OnRoundStart;

    /// <summary>Raised when the intro ends and both fighters regain control.</summary>
    public event Action OnFightStart;

    /// <summary>Raised whenever the whole-second value of the round clock changes.</summary>
    public event Action<int> OnTimerTick;

    /// <summary>Raised when the round clock expires, immediately before the round end is announced.</summary>
    public event Action OnTimeOver;

    /// <summary>
    /// Raised once a round is decided; the tallies are already updated. The winner is null when the
    /// round ended in a draw, in which case both fighters are credited.
    /// </summary>
    public event Action<RobotController> OnRoundEnd;

    /// <summary>Raised with the match winner (null for a drawn match) after the final round's end pause.</summary>
    public event Action<RobotController> OnMatchOver;

    /// <summary>Raised with the new value whenever the match is paused or resumed.</summary>
    public event Action<bool> OnPauseChanged;

    public MatchState CurrentState { get; private set; }
    public int RoundNumber { get; private set; }
    public int Player1RoundsWon { get; private set; }
    public int Player2RoundsWon { get; private set; }
    public RobotController MatchWinner { get; private set; }
    public float RoundDuration => roundDuration;
    public int SecondsRemaining { get; private set; }
    public bool IsPaused { get; private set; }

    /// <summary>Highest round number the match can reach with the current tallies.</summary>
    public int FinalRoundNumber => roundsToWin * 2 - 1;

    private readonly WaitForSeconds introWait = new WaitForSeconds(IntroDuration);
    private readonly WaitForSeconds roundEndWait = new WaitForSeconds(RoundEndDuration);

    private HealthComponent p1Health;
    private HealthComponent p2Health;
    private float roundTimeRemaining;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    /// <summary>
    /// A pause must never outlive the manager, or the next scene would load with time stopped.
    /// Unity always disables before destroying, so this also covers scene unloads.
    /// </summary>
    private void OnDisable()
    {
        if (IsPaused) SetPaused(false);
    }

    private void OnDestroy()
    {
        UnbindFighterHealth();
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (player1 != null && player2 != null)
        {
            InitializeMatch(player1, player2);
        }
    }

    private void Update()
    {
        if (CurrentState == MatchState.MatchOver)
        {
            if (WasStartPressed()) RestartMatch();
            return;
        }

        if (CurrentState != MatchState.Fighting) return;

        if (WasPauseTogglePressed()) TogglePause();
        if (IsPaused) return;

        TickRoundClock();
    }

    public void InitializeMatch(RobotController p1, RobotController p2)
    {
        UnbindFighterHealth();
        player1 = p1;
        player2 = p2;
        p1Health = p1.GetComponent<HealthComponent>();
        p2Health = p2.GetComponent<HealthComponent>();
        BindFighterHealth();

        if (IsPaused) SetPaused(false);

        RoundNumber = 0;
        Player1RoundsWon = 0;
        Player2RoundsWon = 0;
        MatchWinner = null;

        StopAllCoroutines();
        OnMatchReset?.Invoke();
        StartCoroutine(RoundFlowRoutine());
    }

    /// <summary>
    /// Restarts the current pairing from round one. Exposed for HUD buttons alongside the input
    /// shortcut handled in <see cref="Update"/>.
    /// </summary>
    public void RestartMatch()
    {
        if (player1 == null || player2 == null) return;
        InitializeMatch(player1, player2);
    }

    /// <summary>
    /// Pauses or resumes the fight. Only honoured while <see cref="MatchState.Fighting"/>: the intro
    /// and round-end beats are short, scripted and already lock input.
    /// </summary>
    public void TogglePause()
    {
        if (CurrentState != MatchState.Fighting) return;
        SetPaused(!IsPaused);
    }

    /// <summary>
    /// Scene time goes through <see cref="CombatEffectsManager"/> when present so a pause taken
    /// during a slow-motion beat resumes into that beat. Fighters drop button presses and suspend
    /// their own unscaled timers (hitstop, command buffer) for the duration.
    /// </summary>
    private void SetPaused(bool paused)
    {
        IsPaused = paused;

        CombatEffectsManager effects = CombatEffectsManager.Instance;
        if (effects != null) effects.SetPaused(paused);
        else Time.timeScale = paused ? 0f : 1f;

        if (player1 != null) player1.SetSimulationPaused(paused);
        if (player2 != null) player2.SetSimulationPaused(paused);

        OnPauseChanged?.Invoke(paused);
    }

    /// <summary>
    /// Overrides the running round clock, for training options and automated tests that need to
    /// reach time over without waiting out a full round.
    /// </summary>
    public void SetRemainingTime(float seconds)
    {
        roundTimeRemaining = Mathf.Max(0f, seconds);
        PublishSeconds(Mathf.CeilToInt(roundTimeRemaining));
    }

    private void BindFighterHealth()
    {
        if (p1Health != null) p1Health.OnDeath += HandlePlayer1Knockout;
        if (p2Health != null) p2Health.OnDeath += HandlePlayer2Knockout;
    }

    private void UnbindFighterHealth()
    {
        if (p1Health != null) p1Health.OnDeath -= HandlePlayer1Knockout;
        if (p2Health != null) p2Health.OnDeath -= HandlePlayer2Knockout;
    }

    private void HandlePlayer1Knockout() => EndRound(player2);

    private void HandlePlayer2Knockout() => EndRound(player1);

    /// <summary>
    /// Closes the round for <paramref name="winner"/>. A knockout arriving once the round is already
    /// decided (both fighters dropping on the same physics step) is ignored, so a round can only end once.
    /// </summary>
    private void EndRound(RobotController winner)
    {
        if (CurrentState != MatchState.Fighting) return;
        StartCoroutine(RoundEndRoutine(winner));
    }

    private IEnumerator RoundFlowRoutine()
    {
        CurrentState = MatchState.Intro;
        RoundNumber++;

        player1.ResetForNewRound(p1SpawnPoint.position);
        player2.ResetForNewRound(p2SpawnPoint.position);
        SetFightersControlActive(false);
        SetRemainingTime(roundDuration);

        OnRoundStart?.Invoke(RoundNumber);
        yield return introWait;

        SetFightersControlActive(true);
        CurrentState = MatchState.Fighting;
        OnFightStart?.Invoke();
    }

    /// <summary>
    /// The clock runs on scaled time, so hitstop and any pause freeze it for free. The tick event
    /// only fires when the displayed whole second changes.
    /// </summary>
    private void TickRoundClock()
    {
        roundTimeRemaining -= Time.deltaTime;

        if (roundTimeRemaining > 0f)
        {
            PublishSeconds(Mathf.CeilToInt(roundTimeRemaining));
            return;
        }

        roundTimeRemaining = 0f;
        PublishSeconds(0);
        OnTimeOver?.Invoke();
        EndRound(ResolveTimeOverWinner());
    }

    private void PublishSeconds(int seconds)
    {
        if (seconds == SecondsRemaining) return;

        SecondsRemaining = seconds;
        OnTimerTick?.Invoke(seconds);
    }

    /// <summary>
    /// Time over goes to the fighter with the larger share of their life bar; an exact tie is a draw.
    /// </summary>
    private RobotController ResolveTimeOverWinner()
    {
        float p1Ratio = p1Health != null ? p1Health.CurrentHealth / p1Health.MaxHealth : 0f;
        float p2Ratio = p2Health != null ? p2Health.CurrentHealth / p2Health.MaxHealth : 0f;

        if (Mathf.Approximately(p1Ratio, p2Ratio)) return null;
        return p1Ratio > p2Ratio ? player1 : player2;
    }

    private IEnumerator RoundEndRoutine(RobotController winner)
    {
        CurrentState = MatchState.RoundEnd;
        SetFightersControlActive(false);

        if (winner == player1) Player1RoundsWon++;
        else if (winner == player2) Player2RoundsWon++;
        else
        {
            Player1RoundsWon++;
            Player2RoundsWon++;
        }

        OnRoundEnd?.Invoke(winner);
        yield return roundEndWait;

        bool p1Wins = Player1RoundsWon >= roundsToWin;
        bool p2Wins = Player2RoundsWon >= roundsToWin;

        if (p1Wins || p2Wins)
        {
            CurrentState = MatchState.MatchOver;
            MatchWinner = p1Wins == p2Wins ? null : p1Wins ? player1 : player2;
            OnMatchOver?.Invoke(MatchWinner);
        }
        else
        {
            StartCoroutine(RoundFlowRoutine());
        }
    }

    private void SetFightersControlActive(bool active)
    {
        player1.SetControlsActive(active);
        player2.SetControlsActive(active);
    }

    /// <summary>Escape toggles the pause alongside the Start and Enter shortcuts.</summary>
    private static bool WasPauseTogglePressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) return true;

        return WasStartPressed();
    }

    /// <summary>
    /// Keyboard Enter or Start on any connected pad. Face buttons are deliberately excluded so
    /// mashing after a knockout cannot restart the match. Devices are read directly rather than
    /// through the fighters' action maps, which drop buttons while paused and are disabled once the
    /// match ends.
    /// </summary>
    private static bool WasStartPressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.enterKey.wasPressedThisFrame) return true;

        ReadOnlyArray<Gamepad> gamepads = Gamepad.all;
        for (int i = 0; i < gamepads.Count; i++)
        {
            if (gamepads[i].startButton.wasPressedThisFrame) return true;
        }

        return WasJoystickButtonPressed(JoystickStartButton);
    }

    /// <summary>
    /// DirectInput pads such as the Logitech RumblePad 2 enumerate as <see cref="Joystick"/> rather than
    /// <see cref="Gamepad"/>, so their buttons are only reachable by HID control name.
    /// </summary>
    private static bool WasJoystickButtonPressed(string controlName)
    {
        ReadOnlyArray<Joystick> joysticks = Joystick.all;
        for (int i = 0; i < joysticks.Count; i++)
        {
            ButtonControl button = joysticks[i].TryGetChildControl<ButtonControl>(controlName);
            if (button != null && button.wasPressedThisFrame) return true;
        }

        return false;
    }
}
