using UnityEngine;

public enum FighterCommand
{
    None,
    LeftPunch,
    RightPunch,
    LeftKick,
    RightKick,
    DashForward,
    DashBackward
}

public class RobotController : MonoBehaviour
{
    [Header("Dependencies")]
    public InputReader inputReader;
    public Animator animator;
    public CharacterController characterController;
    public Hitbox hitboxBase;

    [Header("Combat Settings")]
    public Transform opponentTarget;

    [Header("Attack Data Assets")]
    public AttackData leftPunchBase;
    public AttackData rightPunchBase;
    public AttackData leftKickBase;
    public AttackData rightKickBase;
    public AttackData crouchPunchBase;
    public AttackData crouchSweepBase;
    public AttackData launcherBase;

    [Header("Input Buffer")]
    public FighterCommand bufferedCommand = FighterCommand.None;
    private float bufferTimer = 0f;
    private float bufferWindow = 0.2f;

    [Header("Dash Detection Settings")]
    private float lastTapTime;
    private float doubleTapWindow = 0.25f;
    private int lastTapDirection = 0; 
    private bool isDirectionPressed = false;

    [Header("Physics & Grounding")]
    public LayerMask groundLayer; 
    public float groundCheckOffset = 0.1f; 
    public bool IsGrounded { get; private set; }

    public bool IsInAttackStartup { get; set; }

    public Vector2 MovementInput { get; private set; }
    private RobotState currentState;
    private Transform cameraTransform;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (characterController == null) characterController = GetComponent<CharacterController>();
        cameraTransform = Camera.main.transform;
    }

    private void Start()
    {
        if (inputReader != null)
        {
            inputReader.MoveEvent += HandleMove;
            inputReader.LeftPunchEvent += HandleLeftPunch;
            inputReader.RightPunchEvent += HandleRightPunch;
            inputReader.LeftKickEvent += HandleLeftKick;
            inputReader.RightKickEvent += HandleRightKick;
        }

        ChangeState(new IdleState(this));
    }

    private void Update()
    {
        CalcularSuelo();

        if (bufferTimer > 0f)
        {
            bufferTimer -= Time.deltaTime;
            if (bufferTimer <= 0f) bufferedCommand = FighterCommand.None;
        }

        currentState?.Tick();
    }

    private void CalcularSuelo()
    {
        float distanciaAlSuelo = characterController.bounds.extents.y + groundCheckOffset;
        IsGrounded = Physics.Raycast(transform.position, Vector3.down, distanciaAlSuelo, groundLayer);
    }

    public bool TryConsumeCommand(FighterCommand expectedCommand)
    {
        if (bufferedCommand == expectedCommand)
        {
            bufferedCommand = FighterCommand.None;
            bufferTimer = 0f;
            return true;
        }
        return false;
    }

    private void HandleMove(Vector2 input)
    {
        MovementInput = input;

        if (Mathf.Abs(input.x) < 0.2f)
        {
            isDirectionPressed = false;
            return;
        }

        if (!isDirectionPressed && Mathf.Abs(input.x) > 0.6f)
        {
            isDirectionPressed = true;
            int currentTapDirection = input.x > 0f ? 1 : -1;

            if (currentTapDirection == lastTapDirection && (Time.time - lastTapTime) <= doubleTapWindow)
            {
                ProcesarDashRelativo(currentTapDirection);
            }
            else
            {
                lastTapDirection = currentTapDirection;
                lastTapTime = Time.time;
            }
        }
    }

    private void ProcesarDashRelativo(int tapDirection)
    {
        if (opponentTarget == null) return;

        Vector3 directionToOpponent = opponentTarget.position - transform.position;
        directionToOpponent.y = 0f;
        float screenSide = Mathf.Sign(Vector3.Dot(directionToOpponent.normalized, cameraTransform.right));

        if (tapDirection == (int)screenSide)
            bufferedCommand = FighterCommand.DashForward;
        else
            bufferedCommand = FighterCommand.DashBackward;

        bufferTimer = bufferWindow;
        lastTapDirection = 0;
    }

    private void HandleLeftPunch() { bufferedCommand = FighterCommand.LeftPunch; bufferTimer = bufferWindow; }
    private void HandleRightPunch() { bufferedCommand = FighterCommand.RightPunch; bufferTimer = bufferWindow; }
    private void HandleLeftKick() { bufferedCommand = FighterCommand.LeftKick; bufferTimer = bufferWindow; }
    private void HandleRightKick() { bufferedCommand = FighterCommand.RightKick; bufferTimer = bufferWindow; }

    public void ChangeState(RobotState newState) { currentState?.Exit(); currentState = newState; currentState?.Enter(); }

    private void OnDestroy()
    {
        if (inputReader != null)
        {
            inputReader.MoveEvent -= HandleMove;
            inputReader.LeftPunchEvent -= HandleLeftPunch;
            inputReader.RightPunchEvent -= HandleRightPunch;
            inputReader.LeftKickEvent -= HandleLeftKick;
            inputReader.RightKickEvent -= HandleRightKick;
        }
    }
}