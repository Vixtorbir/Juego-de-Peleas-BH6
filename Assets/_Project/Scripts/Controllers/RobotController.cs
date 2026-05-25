using Unity.IO.LowLevel.Unsafe;
using UnityEngine;

public class RobotController : MonoBehaviour
{
    [Header("Dependencies")]
    public InputReader inputReader;
    public Animator animator;
    public CharacterController characterController;

    public Vector2 MovementInput { get; private set; }

    private RobotState currentState;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (characterController == null) characterController = GetComponent<CharacterController>();
    }

    private void Start()
    {
        inputReader.MoveEvent += HandleMove;
        inputReader.LeftPunchEvent += HandleLeftPunch;

        ChangeState(new IdleState(this));
    }

    private void Update()
    {
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
        MovementInput = input;
    }

    private void HandleLeftPunch()
    {
        Debug.Log("¡Pum! Intentando lanzar el Jab izquierdo.");
    }

    private void OnDestroy()
    {
        inputReader.MoveEvent -= HandleMove;
        inputReader.LeftPunchEvent -= HandleLeftPunch;
    }
}