using UnityEngine;

public enum FighterCommand
{
    None,
    LeftPunch,
    RightPunch,
    LeftKick,
    RightKick,
    DashForward,
    DashBackward,
    Hadouken,
    Shoryuken
}

/// <summary>
/// Central fighter controller. Owns the state machine, translates raw input events into a
/// short-lived command buffer, resolves facing and pushbox separation against the opponent,
/// and exposes cached dependencies to the FSM states so they never query components at runtime.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class RobotController : MonoBehaviour
{
    private static readonly CombatDirection[] QuarterCircleForward =
    {
        CombatDirection.Down,
        CombatDirection.DownForward,
        CombatDirection.Forward
    };

    private static readonly CombatDirection[] DragonPunch =
    {
        CombatDirection.Forward,
        CombatDirection.Down,
        CombatDirection.DownForward
    };

    private const float DirectionPressThreshold = 0.6f;
    private const float DirectionReleaseThreshold = 0.2f;
    private const float CrouchHeightRatio = 0.5f;

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
    public AttackData shoryukenData;

    [Header("Projectile Settings")]
    public AttackData hadoukenData;
    public GameObject hadoukenPrefab;
    public Transform hadoukenSpawnPoint;

    [Header("Input Buffer")]
    public FighterCommand bufferedCommand = FighterCommand.None;
    [SerializeField] private float bufferWindow = 0.2f;
    [SerializeField] private float doubleTapWindow = 0.25f;

    [Header("Physics & Grounding")]
    public LayerMask groundLayer;
    public float groundCheckOffset = 0.1f;
    [SerializeField] private float pushboxRadius = 1.2f;
    [SerializeField] private float pushboxSeparationSpeed = 10f;

    public bool IsGrounded { get; private set; }
    public bool IsInAttackStartup { get; set; }
    public Vector2 MovementInput { get; private set; }
    public Transform CameraTransform { get; private set; }
    public Hurtbox Hurtbox { get; private set; }
    public HealthComponent Health { get; private set; }

    private RobotState currentState;
    private InputBuffer inputBuffer;

    private float bufferTimer;
    private float lastTapTime;
    private int lastTapDirection;
    private bool isDirectionHeld;

    private float standingHeight;
    private Vector3 standingCenter;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (characterController == null) characterController = GetComponent<CharacterController>();

        CameraTransform = Camera.main.transform;
        inputBuffer = GetComponent<InputBuffer>();
        Hurtbox = GetComponentInChildren<Hurtbox>();
        Health = GetComponent<HealthComponent>();

        standingHeight = characterController.height;
        standingCenter = characterController.center;
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

    private void OnDestroy()
    {
        if (inputReader == null) return;

        inputReader.MoveEvent -= HandleMove;
        inputReader.LeftPunchEvent -= HandleLeftPunch;
        inputReader.RightPunchEvent -= HandleRightPunch;
        inputReader.LeftKickEvent -= HandleLeftKick;
        inputReader.RightKickEvent -= HandleRightKick;
    }

    private void Update()
    {
        UpdateGroundedState();
        TickCommandBuffer();
        ResolvePushboxSeparation();

        currentState?.Tick();
    }

    public void ChangeState(RobotState newState)
    {
        currentState?.Exit();
        currentState = newState;
        currentState?.Enter();
    }

    /// <summary>
    /// Consumes the buffered command if it matches, so a single press can only trigger one action.
    /// </summary>
    public bool TryConsumeCommand(FighterCommand expectedCommand)
    {
        if (bufferedCommand != expectedCommand) return false;

        bufferedCommand = FighterCommand.None;
        bufferTimer = 0f;
        return true;
    }

    /// <summary>
    /// Returns +1 when the opponent is on the camera's right side and -1 when on the left,
    /// which lets states convert horizontal stick input into "towards" or "away" semantics.
    /// </summary>
    public float GetScreenFacingSign()
    {
        if (opponentTarget == null) return 1f;

        Vector3 toOpponent = opponentTarget.position - transform.position;
        toOpponent.y = 0f;
        return Mathf.Sign(Vector3.Dot(toOpponent, CameraTransform.right));
    }

    public void FaceOpponent()
    {
        if (opponentTarget == null) return;

        Vector3 toOpponent = opponentTarget.position - transform.position;
        toOpponent.y = 0f;

        if (toOpponent.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(toOpponent);
        }
    }

    /// <summary>
    /// Adjusts the capsule so low attacks can pass over a crouching fighter. The visual pose
    /// belongs to the Animator; this only affects collision.
    /// </summary>
    public void SetCrouchingPosture(bool crouching)
    {
        if (crouching)
        {
            characterController.height = standingHeight * CrouchHeightRatio;
            characterController.center = new Vector3(standingCenter.x, standingCenter.y * CrouchHeightRatio, standingCenter.z);
        }
        else
        {
            characterController.height = standingHeight;
            characterController.center = standingCenter;
        }
    }

    public void SetControlsActive(bool active)
    {
        if (inputReader != null) inputReader.enabled = active;
    }

    /// <summary>
    /// Teleports the fighter to its corner, restores health and returns to Idle for the next round.
    /// The CharacterController must be disabled during the teleport or it will reject the move.
    /// </summary>
    public void ResetForNewRound(Vector3 startPosition)
    {
        characterController.enabled = false;
        transform.position = startPosition;
        characterController.enabled = true;

        if (Health != null) Health.ResetHealth();

        bufferedCommand = FighterCommand.None;
        bufferTimer = 0f;

        ChangeState(new IdleState(this));
    }

    private void UpdateGroundedState()
    {
        float rayLength = characterController.bounds.extents.y + groundCheckOffset;
        IsGrounded = Physics.Raycast(transform.position, Vector3.down, rayLength, groundLayer);
    }

    private void TickCommandBuffer()
    {
        if (bufferTimer <= 0f) return;

        bufferTimer -= Time.deltaTime;
        if (bufferTimer <= 0f) bufferedCommand = FighterCommand.None;
    }

    private void BufferCommand(FighterCommand command)
    {
        bufferedCommand = command;
        bufferTimer = bufferWindow;
    }

    /// <summary>
    /// Keeps both fighters from overlapping by pushing this controller away from the opponent
    /// horizontally whenever the distance between them drops below the pushbox radius.
    /// </summary>
    private void ResolvePushboxSeparation()
    {
        if (opponentTarget == null) return;

        Vector3 separation = transform.position - opponentTarget.position;
        separation.y = 0f;

        if (separation.sqrMagnitude < 0.0001f)
        {
            separation = transform.right;
        }

        float distance = separation.magnitude;
        if (distance >= pushboxRadius) return;

        float overlap = pushboxRadius - distance;
        Vector3 push = separation / distance * (overlap * pushboxSeparationSpeed * Time.deltaTime);
        characterController.Move(push);
    }

    private void HandleMove(Vector2 input)
    {
        MovementInput = input;

        if (Mathf.Abs(input.x) < DirectionReleaseThreshold)
        {
            isDirectionHeld = false;
            return;
        }

        if (isDirectionHeld || Mathf.Abs(input.x) < DirectionPressThreshold) return;

        isDirectionHeld = true;
        int tapDirection = input.x > 0f ? 1 : -1;

        if (tapDirection == lastTapDirection && Time.time - lastTapTime <= doubleTapWindow)
        {
            ProcessDirectionalDash(tapDirection);
        }
        else
        {
            lastTapDirection = tapDirection;
            lastTapTime = Time.time;
        }
    }

    private void ProcessDirectionalDash(int tapDirection)
    {
        if (opponentTarget == null) return;

        bool towardsOpponent = tapDirection == (int)GetScreenFacingSign();
        BufferCommand(towardsOpponent ? FighterCommand.DashForward : FighterCommand.DashBackward);
        lastTapDirection = 0;
    }

    private void HandleLeftPunch()
    {
        bool motionDetected = inputBuffer != null && inputBuffer.CheckMotionInput(QuarterCircleForward);
        BufferCommand(motionDetected ? FighterCommand.Hadouken : FighterCommand.LeftPunch);
    }

    private void HandleRightPunch()
    {
        bool motionDetected = inputBuffer != null && inputBuffer.CheckMotionInput(DragonPunch);
        BufferCommand(motionDetected ? FighterCommand.Shoryuken : FighterCommand.RightPunch);
    }

    private void HandleLeftKick() => BufferCommand(FighterCommand.LeftKick);
    private void HandleRightKick() => BufferCommand(FighterCommand.RightKick);

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 0.6f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, pushboxRadius);
    }
}
