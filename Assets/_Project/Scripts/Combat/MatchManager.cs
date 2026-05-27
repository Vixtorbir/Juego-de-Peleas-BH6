using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class MatchManager : MonoBehaviour
{
    public static MatchManager Instance;

    public enum MatchState { Intro, Fighting, RoundEnd, MatchOver }
    public MatchState currentState;

    [Header("Configuración de la Arena")]
    public Transform p1SpawnPoint;
    public Transform p2SpawnPoint;
    public int roundsToWin = 2; 

    [Header("Jugadores Actuales (Inyectados dinámicamente)")]
    public RobotController player1;
    public RobotController player2;

    private HealthComponent p1Health;
    private HealthComponent p2Health;

    private int p1RoundsWon = 0;
    private int p2RoundsWon = 0;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        // POR AHORA: Si hemos arrastrado las cápsulas de prueba al inspector, arrancamos.
        // EN EL FUTURO: Esto estará vacío, y será el CharacterSelect quien llame a InitializeMatch()
        if (player1 != null && player2 != null)
        {
            InitializeMatch(player1, player2);
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

        StartCoroutine(RoundFlowRoutine());
    }

    private IEnumerator RoundFlowRoutine()
    {
        currentState = MatchState.Intro;

        // 1. Colocar a los jugadores en sus esquinas y curarlos
        player1.ResetForNewRound(p1SpawnPoint.position);
        player2.ResetForNewRound(p2SpawnPoint.position);

        // 2. Bloquear controles
        player1.SetControlsActive(false);
        player2.SetControlsActive(false);

        int currentRound = p1RoundsWon + p2RoundsWon + 1;
        Debug.Log($"<color=yellow><b>¡ROUND {currentRound}!</b></color>");

        // Esperamos 2 segundos para dar tiempo a la cámara y a las animaciones de entrada
        yield return new WaitForSeconds(2f);

        Debug.Log("<color=green><b>¡FIGHT!</b></color>");

        // 3. Devolver el control a los jugadores
        player1.SetControlsActive(true);
        player2.SetControlsActive(true);

        currentState = MatchState.Fighting;
    }

    private void Update()
    {
        if (currentState == MatchState.MatchOver)
        {
            if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
            {
                Debug.Log("<color=lime><b>[RESTART]</b> Reiniciando el combate completo...</color>");

                InitializeMatch(player1, player2);
            }
            return; 
        }

        if (currentState != MatchState.Fighting) return;

        if (p1Health != null && p1Health.IsDead)
        {
            StartCoroutine(RoundEndRoutine(player2, player1));
        }
        else if (p2Health != null && p2Health.IsDead)
        {
            StartCoroutine(RoundEndRoutine(player1, player2));
        }
    }

    private IEnumerator RoundEndRoutine(RobotController winner, RobotController loser)
    {
        currentState = MatchState.RoundEnd;

        winner.SetControlsActive(false);
        loser.SetControlsActive(false);

        Debug.Log($"<color=red><b>¡K.O.!</b></color> {winner.gameObject.name} gana la ronda.");

        if (winner == player1) p1RoundsWon++;
        else p2RoundsWon++;

        yield return new WaitForSeconds(4f);

        if (p1RoundsWon >= roundsToWin || p2RoundsWon >= roundsToWin)
        {
            currentState = MatchState.MatchOver;
            Debug.Log($"<color=cyan><b>¡GANADOR DEL COMBATE: {winner.gameObject.name}!</b></color>");

            // TODO: En el futuro, aquí cargaremos la escena de "Victoria" o el Menú Principal
        }
        else
        {
            StartCoroutine(RoundFlowRoutine());
        }
    }
}