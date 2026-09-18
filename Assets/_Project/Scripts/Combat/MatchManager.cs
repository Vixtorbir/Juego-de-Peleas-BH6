using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

/// <summary>
/// Drives the round flow of a match: places both fighters, locks input during the intro,
/// detects knockouts and tallies round wins until one side reaches <see cref="roundsToWin"/>.
/// Every transition is broadcast through C# events so presentation layers (HUD, announcer)
/// bind once instead of polling. Fighters are injected so a future character select can call
/// <see cref="InitializeMatch"/>. Once the match is over, any player can restart it from the
/// keyboard or from a controller's Start or South button.
/// </summary>
public class MatchManager : MonoBehaviour
{
    private const float IntroDuration = 2f;
    private const float RoundEndDuration = 4f;

    public enum MatchState { Intro, Fighting, RoundEnd, MatchOver }

    public static MatchManager Instance { get; private set; }

    [Header("Arena Setup")]
    public Transform p1SpawnPoint;
    public Transform p2SpawnPoint;
    public int roundsToWin = 2;

    [Header("Fighters")]
    public RobotController player1;
    public RobotController player2;

    /// <summary>Raised when a match is (re)initialised, before the first round's intro.</summary>
    public event Action OnMatchReset;

    /// <summary>Raised with the 1-based round number when a round's intro begins.</summary>
    public event Action<int> OnRoundStart;

    /// <summary>Raised when the intro ends and both fighters regain control.</summary>
    public event Action OnFightStart;

    /// <summary>Raised with the round winner once a knockout is registered; the tallies are already updated.</summary>
    public event Action<RobotController> OnRoundEnd;

    /// <summary>Raised with the match winner after the final round's end pause.</summary>
    public event Action<RobotController> OnMatchOver;

    public MatchState CurrentState { get; private set; }
    public int RoundNumber { get; private set; }
    public int Player1RoundsWon { get; private set; }
    public int Player2RoundsWon { get; private set; }
    public RobotController MatchWinner { get; private set; }

    private readonly WaitForSeconds introWait = new WaitForSeconds(IntroDuration);
    private readonly WaitForSeconds roundEndWait = new WaitForSeconds(RoundEndDuration);

    private HealthComponent p1Health;
    private HealthComponent p2Health;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
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
            if (WasRestartPressed()) RestartMatch();
            return;
        }

        if (CurrentState != MatchState.Fighting) return;

        if (p1Health != null && p1Health.IsDead)
        {
            StartCoroutine(RoundEndRoutine(player2));
        }
        else if (p2Health != null && p2Health.IsDead)
        {
            StartCoroutine(RoundEndRoutine(player1));
        }
    }

    public void InitializeMatch(RobotController p1, RobotController p2)
    {
        player1 = p1;
        player2 = p2;
        p1Health = p1.GetComponent<HealthComponent>();
        p2Health = p2.GetComponent<HealthComponent>();

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

    private IEnumerator RoundFlowRoutine()
    {
        CurrentState = MatchState.Intro;
        RoundNumber++;

        player1.ResetForNewRound(p1SpawnPoint.position);
        player2.ResetForNewRound(p2SpawnPoint.position);
        SetFightersControlActive(false);

        OnRoundStart?.Invoke(RoundNumber);
        yield return introWait;

        SetFightersControlActive(true);
        CurrentState = MatchState.Fighting;
        OnFightStart?.Invoke();
    }

    private IEnumerator RoundEndRoutine(RobotController winner)
    {
        CurrentState = MatchState.RoundEnd;
        SetFightersControlActive(false);

        if (winner == player1) Player1RoundsWon++;
        else Player2RoundsWon++;

        OnRoundEnd?.Invoke(winner);
        yield return roundEndWait;

        if (Player1RoundsWon >= roundsToWin || Player2RoundsWon >= roundsToWin)
        {
            CurrentState = MatchState.MatchOver;
            MatchWinner = winner;
            OnMatchOver?.Invoke(winner);
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

    /// <summary>
    /// Keyboard Enter or, on any connected pad, Start or the South face button. Devices are read
    /// directly rather than through the fighters' action maps, which are disabled once the match ends.
    /// </summary>
    private static bool WasRestartPressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.enterKey.wasPressedThisFrame) return true;

        ReadOnlyArray<Gamepad> gamepads = Gamepad.all;
        for (int i = 0; i < gamepads.Count; i++)
        {
            Gamepad gamepad = gamepads[i];
            if (gamepad.startButton.wasPressedThisFrame || gamepad.buttonSouth.wasPressedThisFrame) return true;
        }

        return false;
    }
}
