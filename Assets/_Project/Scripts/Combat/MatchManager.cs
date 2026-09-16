using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Drives the round flow of a match: places both fighters, locks input during the intro,
/// detects knockouts and tallies round wins until one side reaches <see cref="roundsToWin"/>.
/// Fighters are injected so a future character select can call <see cref="InitializeMatch"/>.
/// </summary>
public class MatchManager : MonoBehaviour
{
    private const float IntroDuration = 2f;
    private const float RoundEndDuration = 4f;

    public enum MatchState { Intro, Fighting, RoundEnd, MatchOver }

    public static MatchManager Instance { get; private set; }

    public MatchState currentState;

    [Header("Arena Setup")]
    public Transform p1SpawnPoint;
    public Transform p2SpawnPoint;
    public int roundsToWin = 2;

    [Header("Fighters")]
    public RobotController player1;
    public RobotController player2;

    private HealthComponent p1Health;
    private HealthComponent p2Health;

    private int p1RoundsWon;
    private int p2RoundsWon;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
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
        if (currentState == MatchState.MatchOver)
        {
            if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
            {
                InitializeMatch(player1, player2);
            }
            return;
        }

        if (currentState != MatchState.Fighting) return;

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

        p1RoundsWon = 0;
        p2RoundsWon = 0;

        StopAllCoroutines();
        StartCoroutine(RoundFlowRoutine());
    }

    private IEnumerator RoundFlowRoutine()
    {
        currentState = MatchState.Intro;

        player1.ResetForNewRound(p1SpawnPoint.position);
        player2.ResetForNewRound(p2SpawnPoint.position);

        SetFightersControlActive(false);

        Debug.Log($"[MatchManager] Round {p1RoundsWon + p2RoundsWon + 1} starting.");

        yield return new WaitForSeconds(IntroDuration);

        SetFightersControlActive(true);
        currentState = MatchState.Fighting;
    }

    private IEnumerator RoundEndRoutine(RobotController winner)
    {
        currentState = MatchState.RoundEnd;
        SetFightersControlActive(false);

        if (winner == player1) p1RoundsWon++;
        else p2RoundsWon++;

        Debug.Log($"[MatchManager] {winner.gameObject.name} wins the round ({p1RoundsWon}-{p2RoundsWon}).");

        yield return new WaitForSeconds(RoundEndDuration);

        if (p1RoundsWon >= roundsToWin || p2RoundsWon >= roundsToWin)
        {
            currentState = MatchState.MatchOver;
            Debug.Log($"[MatchManager] Match over. Winner: {winner.gameObject.name}.");
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
}
