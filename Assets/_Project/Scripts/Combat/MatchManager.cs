using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;

// Round flow: intro, clock, KO / time over, round wins and match reset.
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

    public event Action OnMatchReset;

    public event Action<int> OnRoundStart;

    public event Action OnFightStart;

    public event Action<int> OnTimerTick;

    public event Action OnTimeOver;

    public event Action<RobotController> OnRoundEnd;

    public event Action<RobotController> OnMatchOver;

    public event Action<bool> OnPauseChanged;

    public MatchState CurrentState { get; private set; }
    public int RoundNumber { get; private set; }
    public int Player1RoundsWon { get; private set; }
    public int Player2RoundsWon { get; private set; }
    public RobotController MatchWinner { get; private set; }
    public float RoundDuration => roundDuration;
    public int SecondsRemaining { get; private set; }
    public bool IsPaused { get; private set; }

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

    // Never leave the game paused when this object goes away (scene unload included).
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

    public void RestartMatch()
    {
        if (player1 == null || player2 == null) return;
        InitializeMatch(player1, player2);
    }

    public void TogglePause()
    {
        if (CurrentState != MatchState.Fighting) return;
        SetPaused(!IsPaused);
    }

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

    // Double KO on the same physics step would call this twice; only the first one counts.
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

    // Scaled time, so hitstop and pause freeze the clock automatically.
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

    private static bool WasPauseTogglePressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) return true;

        return WasStartPressed();
    }

    // Face buttons are left out on purpose so mashing after a KO does not restart the match.
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

    // DirectInput pads show up as Joystick, not Gamepad, so buttons are read by HID control name.
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
