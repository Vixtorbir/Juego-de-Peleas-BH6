using UnityEngine;

public class RobotController : MonoBehaviour
{
    [Header("Dependencies")]
    public InputReader inputReader;
    public Animator animator;
    public CharacterController characterController;

    [Header("Combat Settings")]
    public Transform opponentTarget;

    // Propiedad pública para que los Estados puedan leer qué hace el mando
    public Vector2 MovementInput { get; private set; }

    private RobotState currentState;

    private void Awake()
    {
        // Asignación automática por si olvidamos arrastrarlos en el inspector
        if (animator == null) animator = GetComponent<Animator>();
        if (characterController == null) characterController = GetComponent<CharacterController>();
    }

    private void Start()
    {
        // Nos suscribimos a los eventos del input
        inputReader.MoveEvent += HandleMove;
        inputReader.LeftPunchEvent += HandleLeftPunch;

        // Arrancamos la máquina de estados en reposo
        ChangeState(new IdleState(this));
    }

    private void Update()
    {
        // Ejecutamos la lógica del estado actual constantemente
        currentState?.Tick();
    }

    public void ChangeState(RobotState newState)
    {
        currentState?.Exit();
        currentState = newState;
        currentState?.Enter();
    }

    // --- RESPUESTAS A LOS EVENTOS ---

    private void HandleMove(Vector2 input)
    {
        // Guardamos el input para que los estados lo procesen
        MovementInput = input;
    }

    private void HandleLeftPunch()
    {
        // Regla de oro de los Fighting Games: Solo puedes atacar si estás en reposo o caminando.
        if (currentState is IdleState || currentState is WalkState)
        {
            ChangeState(new AttackState(this));
        }
    }

    private void OnDestroy()
    {
        // Limpiamos las suscripciones al destruir el objeto
        inputReader.MoveEvent -= HandleMove;
        inputReader.LeftPunchEvent -= HandleLeftPunch;
    }
}