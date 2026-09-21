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
/// Every state is allocated once in Awake and entered through a typed method that configures
/// the cached instance, so a full match runs without a single state allocation.
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

    private static readonly Color PushboxGizmoColor = new Color(0f, 0.6f, 1f, 0.9f);
    private static readonly Color BodyGizmoColor = new Color(0f, 1f, 0f, 0.12f);

    private const float DirectionPressThreshold = 0.6f;
    private const float DirectionReleaseThreshold = 0.2f;
    private const float CrouchHeightRatio = 0.5f;
    private const float FacingDeadZone = 0.001f;
    private const int ProjectilePoolCapacity = 2;

    [Header("Dependencies")]
    public InputReader inputReader;
    public Animator animator;
    public CharacterController characterController;
    public Hitbox hitboxBase;

    [Header("Combat Settings")]
    public Transform opponentTarget;

    [Header("Timed Animation Clips")]
    [Tooltip("Clips whose playback speed is rescaled at runtime to match the gameplay window they cover.")]
    public AnimationClip hitReactionClip;
    public AnimationClip blockReactionClip;
    public AnimationClip dashForwardClip;
    public AnimationClip dashBackwardClip;

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
    [Tooltip("Projectile prefab pre-instantiated into this fighter's pool at startup; never spawned during the match.")]
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
    public bool IsInvincible { get; set; }
    public bool AttackConnected { get; set; }
    public Vector2 MovementInput { get; private set; }
    public Hurtbox Hurtbox { get; private set; }
    public HealthComponent Health { get; private set; }
    public ProjectilePool Projectiles { get; private set; }
    public RobotState CurrentState => currentState;

    /// <summary>
    /// Number of state entries since startup. Re-entering the cached instance of the current state
    /// (chain cancels, air juggles) counts as a transition even though the reference is unchanged.
    /// </summary>
    public int TransitionCount { get; private set; }

    private RobotState currentState;
    private InputBuffer inputBuffer;

    private IdleState idleState;
    private WalkState walkState;
    private JumpState jumpState;
    private CrouchState crouchState;
    private DashState dashState;
    private AttackState attackState;
    private CrouchAttackState crouchAttackState;
    private SpecialAttackState specialAttackState;
    private ShoryukenState shoryukenState;
    private HitstunState hitstunState;
    private BlockstunState blockstunState;
    private AirborneHurtState airborneHurtState;
    private DeathState deathState;

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
        animator.applyRootMotion = false;

        inputBuffer = GetComponent<InputBuffer>();
        Hurtbox = GetComponentInChildren<Hurtbox>();
        Health = GetComponent<HealthComponent>();
        AllocateStates();

        if (hitboxBase == null) hitboxBase = GetComponentInChildren<Hitbox>(true);
        if (hitboxBase == null)
        {
            Debug.LogError("RobotController requires a Hitbox in its hierarchy; the fighter has been disabled.", this);
            enabled = false;
            return;
        }

        standingHeight = characterController.height;
        standingCenter = characterController.center;

        if (hadoukenPrefab != null)
        {
            Projectiles = new ProjectilePool(hadoukenPrefab, name, ProjectilePoolCapacity);
        }
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

        EnterIdle();
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
        animator.SetBool(FighterAnimatorParameters.IsGrounded, IsGrounded);
        TickCommandBuffer();
        ResolvePushboxSeparation();

        currentState?.Tick();
    }

    private void AllocateStates()
    {
        idleState = new IdleState(this);
        walkState = new WalkState(this);
        jumpState = new JumpState(this);
        crouchState = new CrouchState(this);
        dashState = new DashState(this);
        attackState = new AttackState(this);
        crouchAttackState = new CrouchAttackState(this);
        specialAttackState = new SpecialAttackState(this);
        shoryukenState = new ShoryukenState(this);
        hitstunState = new HitstunState(this);
        blockstunState = new BlockstunState(this);
        airborneHurtState = new AirborneHurtState(this);
        deathState = new DeathState(this);
    }

    public void EnterIdle() => TransitionTo(idleState);

    public void EnterWalk() => TransitionTo(walkState);

    public void EnterJump() => TransitionTo(jumpState);

    public void EnterCrouch() => TransitionTo(crouchState);

    public void EnterDeath() => TransitionTo(deathState);

    public void EnterDash(bool forward)
    {
        dashState.Configure(forward);
        TransitionTo(dashState);
    }

    public void EnterAttack(AttackData data)
    {
        attackState.Configure(data);
        TransitionTo(attackState);
    }

    public void EnterCrouchAttack(AttackData data)
    {
        crouchAttackState.Configure(data);
        TransitionTo(crouchAttackState);
    }

    public void EnterSpecialAttack(AttackData data)
    {
        specialAttackState.Configure(data);
        TransitionTo(specialAttackState);
    }

    public void EnterShoryuken(AttackData data)
    {
        shoryukenState.Configure(data);
        TransitionTo(shoryukenState);
    }

    public void EnterHitstun(float duration, float pushbackForce, RobotController attacker)
    {
        hitstunState.Configure(duration, pushbackForce, attacker);
        TransitionTo(hitstunState);
    }

    public void EnterBlockstun(float duration, bool crouching, float pushbackForce, RobotController attacker)
    {
        blockstunState.Configure(duration, crouching, pushbackForce, attacker);
        TransitionTo(blockstunState);
    }

    public void EnterAirborneHurt(float launchForce, float driftSpeed, RobotController attacker)
    {
        airborneHurtState.Configure(launchForce, driftSpeed, attacker);
        TransitionTo(airborneHurtState);
    }

    /// <summary>
    /// Exits the current state and enters the next one. The same instance may be entered again:
    /// Exit runs before Enter, so a chained attack or a re-launched juggle restarts cleanly.
    /// </summary>
    private void TransitionTo(RobotState nextState)
    {
        currentState?.Exit();
        currentState = nextState;
        TransitionCount++;
        currentState.Enter();
    }

    /// <summary>
    /// Called by the owner's hitbox or projectile when an attack lands or is blocked. Attack states
    /// read the flag to open their special-cancel window.
    /// </summary>
    public void NotifyAttackConnected()
    {
        AttackConnected = true;
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
    /// Returns +1 when the opponent lies towards +X and -1 towards -X. The match is played along
    /// the world X axis and the camera never rolls around it, so this is the screen-right sign
    /// without any camera dependency. Without an opponent the fighter's own facing is used.
    /// </summary>
    public float GetScreenFacingSign()
    {
        float deltaX = opponentTarget != null
            ? opponentTarget.position.x - transform.position.x
            : transform.forward.x;

        return deltaX < 0f ? -1f : 1f;
    }

    /// <summary>
    /// Snaps the fighter to face the opponent along the combat axis only. The match is played on
    /// the world X axis, so the look direction is clamped to +X or -X and any depth offset between
    /// the two fighters can never tilt them towards or away from the camera.
    /// </summary>
    public void FaceOpponent()
    {
        if (opponentTarget == null) return;

        float deltaX = opponentTarget.position.x - transform.position.x;
        if (Mathf.Abs(deltaX) < FacingDeadZone) return;

        Vector3 facing = deltaX > 0f ? Vector3.right : Vector3.left;
        transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
    }

    /// <summary>
    /// Adjusts the capsule so low attacks can pass over a crouching fighter. The visual pose
    /// belongs to the Animator; this only affects collision.
    /// </summary>
    public void SetCrouchingPosture(bool crouching)
    {
        animator.SetBool(FighterAnimatorParameters.IsCrouching, crouching);

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
        Projectiles?.RecallAll();

        bufferedCommand = FighterCommand.None;
        bufferTimer = 0f;
        IsInvincible = false;
        AttackConnected = false;

        animator.Play(FighterAnimatorParameters.LocomotionState, 0, 0f);
        EnterIdle();
    }

    private void UpdateGroundedState()
    {
        Vector3 capsuleCenter = transform.TransformPoint(characterController.center);
        float rayLength = characterController.bounds.extents.y + groundCheckOffset;
        IsGrounded = Physics.Raycast(capsuleCenter, Vector3.down, rayLength, groundLayer);
    }

    /// <summary>
    /// The buffer only ages while the fighter can act on it. During an attack's startup the
    /// window is held open, so a follow-up pressed early in a jab is still waiting when the
    /// chain window opens on contact instead of expiring mid-swing.
    /// </summary>
    private void TickCommandBuffer()
    {
        if (bufferTimer <= 0f || IsInAttackStartup) return;

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

    /// <summary>
    /// Scene-view overlay of the two volumes the controller owns: the collision capsule that doubles
    /// as the hurt volume, and the horizontal pushbox radius that keeps the fighters apart.
    /// </summary>
    private void OnDrawGizmos()
    {
        CharacterController body = characterController != null ? characterController : GetComponent<CharacterController>();
        float height = body != null ? body.height : 2f;

        if (body != null) CombatGizmos.DrawCollider(body, BodyGizmoColor);
        CombatGizmos.DrawPushbox(transform, pushboxRadius, height, PushboxGizmoColor);
    }
}
