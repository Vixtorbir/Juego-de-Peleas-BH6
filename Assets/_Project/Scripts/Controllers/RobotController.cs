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
    Shoryuken,
    Launcher,
    Overhead,
    AntiAir
}

/// <summary>
/// Central fighter controller. Owns the state machine, translates raw input events into a
/// short-lived command buffer, resolves facing and pushbox separation against the opponent,
/// and exposes cached dependencies to the FSM states so they never query components at runtime.
/// Every state is allocated once in Awake and entered through a typed method that configures
/// the cached instance, so a full match runs without a single state allocation. Hitstop is a
/// per-fighter freeze on unscaled time: a frozen fighter skips its state tick and pauses its
/// animator while the rest of the scene keeps running, so each side of a hit can be held for a
/// different number of frames. Command normals are decoded from the stick at press time, and the
/// airborne velocity is shared between the jump, air dash and air attack states so control can
/// pass between them mid-air without losing momentum.
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

    private static readonly CombatDirection[] QuarterCircleForwardShortcut =
    {
        CombatDirection.Down,
        CombatDirection.Forward
    };

    private static readonly CombatDirection[] DragonPunch =
    {
        CombatDirection.Forward,
        CombatDirection.Down,
        CombatDirection.DownForward
    };

    private static readonly CombatDirection[] DragonPunchShortcut =
    {
        CombatDirection.Forward,
        CombatDirection.Down,
        CombatDirection.Forward
    };

    private static readonly Color PushboxGizmoColor = new Color(0f, 0.6f, 1f, 0.9f);
    private static readonly Color BodyGizmoColor = new Color(0f, 1f, 0f, 0.12f);

    private const float DirectionPressThreshold = 0.6f;
    private const float DirectionReleaseThreshold = 0.2f;
    private const float CrouchHeightRatio = 0.5f;
    private const float FacingDeadZone = 0.001f;
    private const float CommandForwardThreshold = 0.5f;
    private const float CommandDownThreshold = -0.5f;
    private const int ProjectilePoolCapacity = 2;
    private const string WallLayerName = "Wall";

    [Header("Dependencies")]
    public InputReader inputReader;
    public Animator animator;
    public CharacterController characterController;
    public Hitbox hitboxBase;
    [Tooltip("Match-wide tuning: proration, hitstun decay, juggle gravity, hitstop tiers and buffer leniency.")]
    public CombatRules combatRules;

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
    public AttackData overheadBase;
    public AttackData antiAirBase;
    public AttackData shoryukenData;

    [Header("Aerial Attack Data Assets")]
    public AttackData airLeftPunchBase;
    public AttackData airRightPunchBase;
    public AttackData airLeftKickBase;
    public AttackData airRightKickBase;

    [Header("Projectile Settings")]
    public AttackData hadoukenData;
    [Tooltip("Projectile prefab pre-instantiated into this fighter's pool at startup; never spawned during the match.")]
    public GameObject hadoukenPrefab;
    public Transform hadoukenSpawnPoint;

    [Header("Input Buffer")]
    [Tooltip("Pending command. Writing it directly counts as a fresh press.")]
    public FighterCommand bufferedCommand = FighterCommand.None;
    [SerializeField] private float doubleTapWindow = 0.25f;

    [Header("Physics & Grounding")]
    public LayerMask groundLayer;
    [Tooltip("Stage boundary layer a wall-bounce reaction rebounds off.")]
    public LayerMask wallLayer;
    public float groundCheckOffset = 0.1f;
    [SerializeField] private float pushboxRadius = 1.2f;
    [SerializeField] private float pushboxSeparationSpeed = 10f;

    public bool IsGrounded { get; private set; }
    public bool IsInAttackStartup { get; set; }
    public bool AttackConnected { get; set; }
    public Vector2 MovementInput { get; private set; }
    public Hurtbox Hurtbox { get; private set; }
    public HealthComponent Health { get; private set; }
    public ProjectilePool Projectiles { get; private set; }
    public CombatRules Rules => combatRules;
    public RobotState CurrentState => currentState;

    /// <summary>Set by reversal specials for their startup; wake-up invulnerability is added on top.</summary>
    public bool IsInvincible
    {
        get => invincibleByState || invulnerabilityTimer > 0f;
        set => invincibleByState = value;
    }

    /// <summary>
    /// Velocity carried while airborne (x along the combat axis, y vertical). Written by whichever
    /// air state is active so the next one continues the motion instead of restarting it.
    /// </summary>
    public Vector2 AirVelocity { get; set; }

    /// <summary>Air dashes spent since the last takeoff; cleared on every return to a neutral stance.</summary>
    public int AirDashesUsed { get; set; }

    /// <summary>True once the capsule is high enough for an air action to start.</summary>
    public bool IsAboveAirActionHeight => transform.position.y >= combatRules.airActionMinHeight;

    /// <summary>True while this fighter is held in hitstop.</summary>
    public bool IsFrozen => freezeRemaining > 0f;

    /// <summary>Unscaled seconds of hitstop still to serve.</summary>
    public float FreezeRemaining => freezeRemaining;

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
    private AirDashState airDashState;
    private AttackState attackState;
    private CrouchAttackState crouchAttackState;
    private AirAttackState airAttackState;
    private SpecialAttackState specialAttackState;
    private ShoryukenState shoryukenState;
    private HitstunState hitstunState;
    private BlockstunState blockstunState;
    private AirborneHurtState airborneHurtState;
    private KnockdownState knockdownState;
    private DeathState deathState;

    private FighterCommand trackedCommand;
    private float pressTime;
    private float bufferTimer;
    private bool bufferWindowOpen;
    private float lastTapTime;
    private int lastTapDirection;
    private bool isDirectionHeld;

    private float freezeRemaining;
    private float invulnerabilityTimer;
    private bool invincibleByState;

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
        if (combatRules == null) combatRules = ScriptableObject.CreateInstance<CombatRules>();
        if (wallLayer.value == 0) wallLayer = LayerMask.GetMask(WallLayerName);
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
        if (TickFreeze()) return;

        TickInvulnerability();
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
        airDashState = new AirDashState(this);
        attackState = new AttackState(this);
        crouchAttackState = new CrouchAttackState(this);
        airAttackState = new AirAttackState(this);
        specialAttackState = new SpecialAttackState(this);
        shoryukenState = new ShoryukenState(this);
        hitstunState = new HitstunState(this);
        blockstunState = new BlockstunState(this);
        airborneHurtState = new AirborneHurtState(this);
        knockdownState = new KnockdownState(this);
        deathState = new DeathState(this);
    }

    public void EnterIdle() => TransitionTo(idleState);

    public void EnterWalk() => TransitionTo(walkState);

    /// <summary>Takes off with the standard jump.</summary>
    public void EnterJump() => EnterJump(JumpState.StandardJumpForce);

    /// <summary>Takes off with a custom vertical speed; jump cancels use the rules' super-jump force.</summary>
    public void EnterJump(float verticalForce)
    {
        jumpState.ConfigureTakeoff(verticalForce);
        TransitionTo(jumpState);
    }

    /// <summary>Resumes free fall from the current <see cref="AirVelocity"/> after an air dash or air attack.</summary>
    public void EnterAirborne()
    {
        jumpState.ConfigureMidair();
        TransitionTo(jumpState);
    }

    public void EnterAirDash(bool forward)
    {
        airDashState.Configure(forward);
        TransitionTo(airDashState);
    }

    public void EnterAirAttack(AttackData data)
    {
        airAttackState.Configure(data);
        TransitionTo(airAttackState);
    }

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

    public void EnterAirborneHurt(float launchForce, float driftSpeed, RobotController attacker, BounceMode bounce, float bounceForce)
    {
        airborneHurtState.Configure(launchForce, driftSpeed, attacker, bounce, bounceForce);
        TransitionTo(airborneHurtState);
    }

    public void EnterKnockdown(float duration, float pushbackForce, RobotController attacker)
    {
        knockdownState.Configure(duration, pushbackForce, attacker);
        TransitionTo(knockdownState);
    }

    /// <summary>
    /// Exits the current state and enters the next one. The same instance may be entered again:
    /// Exit runs before Enter, so a chained attack or a re-launched juggle restarts cleanly.
    /// Entering a neutral stance ends any combo the opponent had running on this fighter.
    /// </summary>
    private void TransitionTo(RobotState nextState)
    {
        currentState?.Exit();
        currentState = nextState;
        TransitionCount++;
        if (nextState.IsNeutral)
        {
            AirDashesUsed = 0;
            if (Hurtbox != null) Hurtbox.ResetCombo();
        }
        currentState.Enter();
    }

    /// <summary>
    /// Holds this fighter in hitstop for <paramref name="seconds"/> of unscaled time. A longer
    /// freeze always extends a shorter one. The animator is paused on its current pose so the
    /// contact frame stays on screen.
    /// </summary>
    public void FreezeFor(float seconds)
    {
        if (seconds <= 0f) return;

        freezeRemaining = Mathf.Max(freezeRemaining, seconds);
        animator.speed = 0f;
    }

    /// <summary>Grants strike invulnerability for <paramref name="seconds"/> of gameplay time.</summary>
    public void GrantInvulnerability(float seconds)
    {
        invulnerabilityTimer = Mathf.Max(invulnerabilityTimer, seconds);
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

        ClearBufferedCommand();
        return true;
    }

    /// <summary>
    /// Grounded normal bound to a buffered command for the given posture, or null for non-attack
    /// commands. Command normals (launcher, overhead, anti-air) were decoded from the stick when the
    /// button was pressed, so they resolve regardless of the current posture.
    /// </summary>
    public AttackData ResolveNormal(FighterCommand command, bool crouching)
    {
        switch (command)
        {
            case FighterCommand.LeftPunch:
                return crouching ? crouchPunchBase : leftPunchBase;
            case FighterCommand.RightPunch:
                return crouching ? crouchPunchBase : rightPunchBase;
            case FighterCommand.LeftKick:
                return crouching ? crouchSweepBase : leftKickBase;
            case FighterCommand.RightKick:
                return crouching ? crouchSweepBase : rightKickBase;
            case FighterCommand.Launcher:
                return launcherBase;
            case FighterCommand.Overhead:
                return overheadBase;
            case FighterCommand.AntiAir:
                return antiAirBase;
            default:
                return null;
        }
    }

    /// <summary>
    /// Jumping normal bound to a buffered command, or null for non-attack commands. Direction-decoded
    /// commands fold back onto their button so a forward press in the air still swings.
    /// </summary>
    public AttackData ResolveAirNormal(FighterCommand command)
    {
        switch (command)
        {
            case FighterCommand.LeftPunch:
                return airLeftPunchBase;
            case FighterCommand.RightPunch:
            case FighterCommand.Overhead:
                return airRightPunchBase;
            case FighterCommand.LeftKick:
                return airLeftKickBase;
            case FighterCommand.RightKick:
            case FighterCommand.Launcher:
            case FighterCommand.AntiAir:
                return airRightKickBase;
            default:
                return null;
        }
    }

    public bool IsCrouchingNormal(AttackData attack)
    {
        return attack != null && (attack == crouchPunchBase || attack == crouchSweepBase);
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
    /// True when the capsule touches the stage boundary on the side it is travelling towards.
    /// Only walls count: the opponent's body never reads as a wall.
    /// </summary>
    public bool IsTouchingWall(float directionSign)
    {
        Vector3 origin = transform.TransformPoint(characterController.center);
        float reach = characterController.radius + characterController.skinWidth + groundCheckOffset;
        return Physics.Raycast(origin, new Vector3(directionSign, 0f, 0f), reach, wallLayer);
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

        ClearBufferedCommand();
        freezeRemaining = 0f;
        invulnerabilityTimer = 0f;
        invincibleByState = false;
        AttackConnected = false;
        AirDashesUsed = 0;
        AirVelocity = Vector2.zero;
        animator.speed = 1f;

        animator.Play(FighterAnimatorParameters.LocomotionState, 0, 0f);
        EnterIdle();
    }

    /// <summary>Returns true while the fighter must stay frozen this frame.</summary>
    private bool TickFreeze()
    {
        if (freezeRemaining <= 0f) return false;

        freezeRemaining -= Time.unscaledDeltaTime;
        if (freezeRemaining > 0f) return true;

        freezeRemaining = 0f;
        animator.speed = 1f;
        return false;
    }

    private void TickInvulnerability()
    {
        if (invulnerabilityTimer <= 0f) return;
        invulnerabilityTimer = Mathf.Max(0f, invulnerabilityTimer - Time.deltaTime);
    }

    private void UpdateGroundedState()
    {
        Vector3 capsuleCenter = transform.TransformPoint(characterController.center);
        float rayLength = characterController.bounds.extents.y + groundCheckOffset;
        IsGrounded = Physics.Raycast(capsuleCenter, Vector3.down, rayLength, groundLayer);
    }

    /// <summary>
    /// First-actionable-frame buffering. A press is held, without ageing, through startup,
    /// recovery, stun and hitstop; once the state accepts commands the leniency window opens and
    /// the state ticking right after this consumes it. A press older than the hold-through limit
    /// when the fighter becomes actionable is dropped, so mashing through a long stun stores nothing.
    /// </summary>
    private void TickCommandBuffer()
    {
        if (bufferedCommand == FighterCommand.None)
        {
            trackedCommand = FighterCommand.None;
            return;
        }

        if (bufferedCommand != trackedCommand) RegisterPress(bufferedCommand);

        if (currentState == null || !currentState.AcceptsCommands)
        {
            if (Time.unscaledTime - pressTime > combatRules.holdThrough) ClearBufferedCommand();
            return;
        }

        if (!bufferWindowOpen)
        {
            bufferWindowOpen = true;
            bufferTimer = combatRules.linkLeniency;
            return;
        }

        bufferTimer -= Time.deltaTime;
        if (bufferTimer <= 0f) ClearBufferedCommand();
    }

    private void BufferCommand(FighterCommand command)
    {
        bufferedCommand = command;
        RegisterPress(command);
    }

    private void RegisterPress(FighterCommand command)
    {
        trackedCommand = command;
        pressTime = Time.unscaledTime;
        bufferWindowOpen = false;
        bufferTimer = 0f;
    }

    private void ClearBufferedCommand()
    {
        bufferedCommand = FighterCommand.None;
        trackedCommand = FighterCommand.None;
        bufferWindowOpen = false;
        bufferTimer = 0f;
    }

    private bool IsHoldingForward => MovementInput.x * GetScreenFacingSign() > CommandForwardThreshold;

    private bool IsHoldingDown => MovementInput.y < CommandDownThreshold;

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

    private void HandleMove(Vector2 input) => SetMovementInput(input);

    /// <summary>
    /// Applies a stick vector as if the player had moved it: feeds the states, and detects the
    /// double tap that buffers a dash. Public so automated tests and AI drivers share the exact path
    /// the input reader uses.
    /// </summary>
    public void SetMovementInput(Vector2 input)
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

    /// <summary>
    /// Motion inputs accept the strict sequence and a lenient shortcut that skips the diagonal,
    /// so a fast stick flick sampled once per frame still reads as the motion.
    /// </summary>
    private void HandleLeftPunch()
    {
        bool motionDetected = inputBuffer != null
            && (inputBuffer.CheckMotionInput(QuarterCircleForward) || inputBuffer.CheckMotionInput(QuarterCircleForwardShortcut));
        BufferCommand(motionDetected ? FighterCommand.Hadouken : FighterCommand.LeftPunch);
    }

    /// <summary>
    /// Heavy buttons carry the command normals, decoded from the stick on the press: forward +
    /// RightPunch is the overhead, forward + RightKick the launcher and down-forward + RightKick the
    /// anti-air. A dragon punch motion always wins over the direction.
    /// </summary>
    private void HandleRightPunch()
    {
        bool motionDetected = inputBuffer != null
            && (inputBuffer.CheckMotionInput(DragonPunch) || inputBuffer.CheckMotionInput(DragonPunchShortcut));

        if (motionDetected) BufferCommand(FighterCommand.Shoryuken);
        else if (IsHoldingForward && !IsHoldingDown) BufferCommand(FighterCommand.Overhead);
        else BufferCommand(FighterCommand.RightPunch);
    }

    private void HandleLeftKick() => BufferCommand(FighterCommand.LeftKick);

    private void HandleRightKick()
    {
        if (IsHoldingForward && IsHoldingDown) BufferCommand(FighterCommand.AntiAir);
        else if (IsHoldingForward) BufferCommand(FighterCommand.Launcher);
        else BufferCommand(FighterCommand.RightKick);
    }

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
