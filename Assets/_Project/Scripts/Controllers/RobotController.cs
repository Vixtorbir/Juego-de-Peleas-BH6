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
    AntiAir,
    SuperArt
}

// Owns the fighter state machine, the command buffer, facing and pushbox separation.
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(SuperMeterComponent))]
[RequireComponent(typeof(CombatFlash))]
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

    private static readonly CombatDirection[] DoubleQuarterCircleForward =
    {
        CombatDirection.Down,
        CombatDirection.DownForward,
        CombatDirection.Forward,
        CombatDirection.Down,
        CombatDirection.DownForward,
        CombatDirection.Forward
    };

    private static readonly CombatDirection[] DoubleQuarterCircleForwardShortcut =
    {
        CombatDirection.Down,
        CombatDirection.Forward,
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
    private const string ModelObjectName = "Model";
    private const float HalfTurnDegrees = 180f;
    private const float SuperMotionWindow = 0.6f;

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

    [Header("Super Art Attack Data Assets")]
    [Tooltip("Rushing strike that opens the super art. Whiffed or blocked, it is the only part the opponent ever sees.")]
    public AttackData superArtLunge;
    [Tooltip("Cinematic beats fired in order once the lunge connects. The last entry is the finisher that launches.")]
    public AttackData[] superArtBeats;

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
    [Tooltip("Pending command at the head of the buffer. Writing it directly counts as a fresh press.")]
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
    public SuperMeterComponent SuperMeter { get; private set; }
    public ProjectilePool Projectiles { get; private set; }
    public CombatFlash Flash { get; private set; }

    public FightingCamera CombatCamera { get; private set; }
    public CombatRules Rules => combatRules;
    public RobotState CurrentState => currentState;

    public FighterCommand QueuedCommand => queuedCommand;

    public float OpponentBodyRadius { get; private set; }

    public RobotController Opponent { get; private set; }

    public float PendingAttackMomentum { get; set; }

    public bool IsInvincible
    {
        get => invincibleByState || invulnerabilityTimer > 0f;
        set => invincibleByState = value;
    }

    public Vector2 AirVelocity { get; set; }

    public int AirDashesUsed { get; set; }

    public bool IsAboveAirActionHeight => transform.position.y >= combatRules.airActionMinHeight;

    public bool IsFrozen => freezeRemaining > 0f;

    public float FreezeRemaining => freezeRemaining;

    public bool IsSimulationPaused { get; private set; }

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
    private SuperArtState superArtState;
    private HitstunState hitstunState;
    private BlockstunState blockstunState;
    private AirborneHurtState airborneHurtState;
    private KnockdownState knockdownState;
    private DeathState deathState;

    private FighterCommand trackedCommand;
    private float pressTime;
    private float bufferTimer;
    private bool bufferWindowOpen;
    private FighterCommand queuedCommand;
    private float queuedPressTime;
    private bool lastPressWasQueued;
    private float lastLeftKickPressTime = float.NegativeInfinity;
    private float lastRightKickPressTime = float.NegativeInfinity;
    private float lastTapTime;
    private int lastTapDirection;
    private bool isDirectionHeld;
    private int rapidChainLinks;

    private Transform modelTransform;
    private Quaternion modelBaseRotation;
    private float modelYawOffset;
    private bool wasInvincible;

    private float freezeRemaining;
    private float invulnerabilityTimer;
    private bool invincibleByState;

    private float standingHeight;
    private Vector3 standingCenter;

    private void Awake()
    {
        if (combatRules == null)
        {
            Debug.LogError("RobotController has no CombatRules asset assigned; the fighter has been disabled.", this);
            enabled = false;
            return;
        }

        if (animator == null) animator = GetComponent<Animator>();
        if (characterController == null) characterController = GetComponent<CharacterController>();
        animator.applyRootMotion = false;

        inputBuffer = GetComponent<InputBuffer>();
        Hurtbox = GetComponentInChildren<Hurtbox>();
        Health = GetComponent<HealthComponent>();
        SuperMeter = GetComponent<SuperMeterComponent>();
        Flash = GetComponent<CombatFlash>();
        if (wallLayer.value == 0) wallLayer = LayerMask.GetMask(WallLayerName);

        Camera mainCamera = Camera.main;
        if (mainCamera != null) CombatCamera = mainCamera.GetComponent<FightingCamera>();

        modelTransform = transform.Find(ModelObjectName);
        if (modelTransform != null) modelBaseRotation = modelTransform.localRotation;

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
            inputReader.SuperArtEvent += HandleSuperArt;
        }

        if (opponentTarget != null && opponentTarget.TryGetComponent(out CharacterController opponentBody))
        {
            OpponentBodyRadius = opponentBody.radius;
        }

        if (opponentTarget != null) Opponent = opponentTarget.GetComponent<RobotController>();

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
        inputReader.SuperArtEvent -= HandleSuperArt;
    }

    private void Update()
    {
        if (IsSimulationPaused) return;

        TickTelegraphs();
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
        superArtState = new SuperArtState(this);
        hitstunState = new HitstunState(this);
        blockstunState = new BlockstunState(this);
        airborneHurtState = new AirborneHurtState(this);
        knockdownState = new KnockdownState(this);
        deathState = new DeathState(this);
    }

    public void EnterIdle() => TransitionTo(idleState);

    public void EnterWalk() => TransitionTo(walkState);

    public void EnterJump() => EnterJump(JumpState.StandardJumpForce);

    public void EnterJump(float verticalForce)
    {
        jumpState.ConfigureTakeoff(verticalForce);
        TransitionTo(jumpState);
    }

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

    public void EnterAttack(AttackData data) => EnterAttack(data, false);

    public void EnterAttack(AttackData data, bool chained)
    {
        if (!chained) rapidChainLinks = 0;
        attackState.Configure(data, chained);
        TransitionTo(attackState);
    }

    public void EnterCrouchAttack(AttackData data) => EnterCrouchAttack(data, false);

    public void EnterCrouchAttack(AttackData data, bool chained)
    {
        if (!chained) rapidChainLinks = 0;
        crouchAttackState.Configure(data, chained);
        TransitionTo(crouchAttackState);
    }

    public bool TryUseRapidLink()
    {
        if (rapidChainLinks >= combatRules.rapidChainLimit) return false;

        rapidChainLinks++;
        return true;
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

    public bool TryEnterSuperArt()
    {
        if (superArtLunge == null || SuperMeter == null) return false;
        if (!SuperMeter.TrySpendExact(SuperMeter.SuperArtCost)) return false;

        superArtState.Configure(superArtLunge, superArtBeats);
        TransitionTo(superArtState);
        return true;
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

    // Hitstop runs on unscaled time; a longer freeze always overrides a shorter one.
    public void FreezeFor(float seconds)
    {
        if (seconds <= 0f) return;

        freezeRemaining = Mathf.Max(freezeRemaining, seconds);
        animator.speed = 0f;
    }

    public void GrantInvulnerability(float seconds)
    {
        invulnerabilityTimer = Mathf.Max(invulnerabilityTimer, seconds);
    }

    public void NotifyAttackConnected()
    {
        AttackConnected = true;
    }

    public bool TryConsumeCommand(FighterCommand expectedCommand)
    {
        if (bufferedCommand != expectedCommand) return false;

        ClearBufferedCommand();
        return true;
    }

    // Command normals (launcher, overhead, anti-air) were already decoded from the stick when the button was pressed.
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

    public float GetScreenFacingSign()
    {
        float deltaX = opponentTarget != null
            ? opponentTarget.position.x - transform.position.x
            : transform.forward.x;

        return deltaX < 0f ? -1f : 1f;
    }

    public void FaceOpponent()
    {
        if (opponentTarget == null) return;

        float deltaX = opponentTarget.position.x - transform.position.x;
        if (Mathf.Abs(deltaX) < FacingDeadZone) return;

        Vector3 facing = deltaX > 0f ? Vector3.right : Vector3.left;
        if (Vector3.Dot(transform.forward, facing) > 0.999f) return;

        float previousYaw = transform.eulerAngles.y;
        transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
        modelYawOffset = Mathf.DeltaAngle(0f, modelYawOffset + Mathf.DeltaAngle(transform.eulerAngles.y, previousYaw));
        ApplyModelYawOffset();
    }

    // Collision only - the crouch pose itself comes from the Animator.
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

    public void SetSimulationPaused(bool paused)
    {
        IsSimulationPaused = paused;
        if (inputReader != null) inputReader.SetButtonsSuppressed(paused);
    }

    public bool IsTouchingWall(float directionSign)
    {
        Vector3 origin = transform.TransformPoint(characterController.center);
        float reach = characterController.radius + characterController.skinWidth + groundCheckOffset;
        return Physics.Raycast(origin, new Vector3(directionSign, 0f, 0f), reach, wallLayer);
    }

    // CharacterController has to be disabled for the teleport or it rejects the move.
    public void ResetForNewRound(Vector3 startPosition)
    {
        characterController.enabled = false;
        transform.position = startPosition;
        characterController.enabled = true;

        if (Health != null) Health.ResetHealth();
        if (SuperMeter != null) SuperMeter.ResetCharge();
        Projectiles?.RecallAll();

        queuedCommand = FighterCommand.None;
        ClearBufferedCommand();
        freezeRemaining = 0f;
        invulnerabilityTimer = 0f;
        invincibleByState = false;
        AttackConnected = false;
        AirDashesUsed = 0;
        AirVelocity = Vector2.zero;
        PendingAttackMomentum = 0f;
        rapidChainLinks = 0;
        animator.speed = 1f;

        animator.Play(FighterAnimatorParameters.LocomotionState, 0, 0f);
        EnterIdle();

        modelYawOffset = 0f;
        ApplyModelYawOffset();
    }

    private void TickTelegraphs()
    {
        bool invincible = IsInvincible;
        if (invincible != wasInvincible)
        {
            wasInvincible = invincible;
            Flash.SetPulse(invincible, combatRules.invulnerableFlashColor, combatRules.invulnerableFlashFrequency, combatRules.invulnerableFlashMinDuration);
        }

        if (modelYawOffset == 0f) return;

        float turnSpeed = HalfTurnDegrees / Mathf.Max(combatRules.turnaroundDuration, 0.0001f);
        modelYawOffset = Mathf.MoveTowardsAngle(modelYawOffset, 0f, turnSpeed * Time.unscaledDeltaTime);
        ApplyModelYawOffset();
    }

    private void ApplyModelYawOffset()
    {
        if (modelTransform == null) return;
        modelTransform.localRotation = modelBaseRotation * Quaternion.Euler(0f, modelYawOffset, 0f);
    }

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

    // A press is held without ageing through startup/recovery/stun; the leniency window only starts once the state can act on it.
    private void TickCommandBuffer()
    {
        if (bufferedCommand == FighterCommand.None)
        {
            trackedCommand = FighterCommand.None;
            if (queuedCommand != FighterCommand.None) PromoteQueuedCommand();
            return;
        }

        if (bufferedCommand != trackedCommand) RegisterPress(bufferedCommand);

        if (currentState == null || !currentState.CanConsume(bufferedCommand))
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

    public void PressCommand(FighterCommand command)
    {
        if (bufferedCommand == FighterCommand.None)
        {
            bufferedCommand = command;
            RegisterPress(command);
            lastPressWasQueued = false;
            return;
        }

        queuedCommand = command;
        queuedPressTime = Time.unscaledTime;
        lastPressWasQueued = true;
    }

    private void ReplaceLastPress(FighterCommand command)
    {
        if (lastPressWasQueued && queuedCommand != FighterCommand.None)
        {
            queuedCommand = command;
            return;
        }

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
        lastPressWasQueued = false;
        if (queuedCommand != FighterCommand.None) PromoteQueuedCommand();
    }

    private void PromoteQueuedCommand()
    {
        bufferedCommand = queuedCommand;
        trackedCommand = queuedCommand;
        pressTime = queuedPressTime;
        bufferWindowOpen = false;
        bufferTimer = 0f;
        queuedCommand = FighterCommand.None;
    }

    private bool IsHoldingForward => MovementInput.x * GetScreenFacingSign() > CommandForwardThreshold;

    private bool IsHoldingDown => MovementInput.y < CommandDownThreshold;

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

    public void SetMovementInput(Vector2 input)
    {
        MovementInput = input;
        if (IsSimulationPaused) return;

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
        PressCommand(towardsOpponent ? FighterCommand.DashForward : FighterCommand.DashBackward);
        lastTapDirection = 0;
    }

    // Also accept the shortcut without the diagonal, since a fast flick can skip it between frames.
    private void HandleLeftPunch()
    {
        if (inputBuffer == null)
        {
            PressCommand(FighterCommand.LeftPunch);
            return;
        }

        if (IsSuperArtReady && MatchesDoubleQuarterCircle())
        {
            PressCommand(FighterCommand.SuperArt);
            return;
        }

        bool motionDetected = inputBuffer.CheckMotionInput(QuarterCircleForward) || inputBuffer.CheckMotionInput(QuarterCircleForwardShortcut);
        PressCommand(motionDetected ? FighterCommand.Hadouken : FighterCommand.LeftPunch);
    }

    private bool IsSuperArtReady => superArtLunge != null && SuperMeter != null && SuperMeter.IsFull;

    private void HandleSuperArt()
    {
        if (IsSuperArtReady) PressCommand(FighterCommand.SuperArt);
    }

    private bool MatchesDoubleQuarterCircle()
    {
        return inputBuffer.CheckMotionInput(DoubleQuarterCircleForward, SuperMotionWindow)
            || inputBuffer.CheckMotionInput(DoubleQuarterCircleForwardShortcut, SuperMotionWindow);
    }

    // forward+RP = overhead, forward+RK = launcher, down-forward+RK = anti-air. A DP motion always wins.
    private void HandleRightPunch()
    {
        bool motionDetected = inputBuffer != null
            && (inputBuffer.CheckMotionInput(DragonPunch) || inputBuffer.CheckMotionInput(DragonPunchShortcut));

        if (motionDetected) PressCommand(FighterCommand.Shoryuken);
        else if (IsHoldingForward && !IsHoldingDown) PressCommand(FighterCommand.Overhead);
        else PressCommand(FighterCommand.RightPunch);
    }

    // Both kicks inside the macro window = dash; the first kick press gets rewritten into it.
    private void HandleLeftKick()
    {
        lastLeftKickPressTime = Time.unscaledTime;
        if (TryDashMacro(lastRightKickPressTime)) return;

        PressCommand(FighterCommand.LeftKick);
    }

    private void HandleRightKick()
    {
        lastRightKickPressTime = Time.unscaledTime;
        if (TryDashMacro(lastLeftKickPressTime)) return;

        if (IsHoldingForward && IsHoldingDown) PressCommand(FighterCommand.AntiAir);
        else if (IsHoldingForward) PressCommand(FighterCommand.Launcher);
        else PressCommand(FighterCommand.RightKick);
    }

    private bool TryDashMacro(float otherKickPressTime)
    {
        if (Time.unscaledTime - otherKickPressTime > combatRules.dashMacroWindow) return false;

        bool holdingBack = MovementInput.x * GetScreenFacingSign() < -CommandForwardThreshold;
        ReplaceLastPress(holdingBack ? FighterCommand.DashBackward : FighterCommand.DashForward);
        lastLeftKickPressTime = float.NegativeInfinity;
        lastRightKickPressTime = float.NegativeInfinity;
        return true;
    }

    private void OnDrawGizmos()
    {
        CharacterController body = characterController != null ? characterController : GetComponent<CharacterController>();
        float height = body != null ? body.height : 2f;

        if (body != null) CombatGizmos.DrawCollider(body, BodyGizmoColor);
        CombatGizmos.DrawPushbox(transform, pushboxRadius, height, PushboxGizmoColor);
    }
}
