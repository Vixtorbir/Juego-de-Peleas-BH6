using UnityEngine;

// Super art in four beats: freeze, lunge, locked multi-hit string, launching finisher.
public class SuperArtState : RobotState
{
    public const float LungeReach = 4.5f;

    private const float FreezeDuration = 0.4f;
    private const float FreezeZoomPunch = -6f;
    private const float LungeSpeed = 11f;
    private const float BeatInterval = 0.12f;
    private const float CinematicRange = 1.3f;
    private const float AnchorSpeed = 12f;
    private const float LockMargin = 0.4f;
    private const float FinisherShakeDuration = 0.35f;
    private const float FinisherShakeMagnitude = 0.4f;
    private const float FinisherZoomPunch = -3.5f;
    private const float FinisherCameraPunch = 0.14f;
    private const float FinisherFlashDuration = 0.18f;

    private static readonly Color ActivationFlashColor = new Color(1f, 0.85f, 0.35f);
    private static readonly Color FinisherFlashColor = new Color(0.45f, 0.95f, 1f);

    private enum Phase
    {
        Freeze,
        Lunge,
        Cinematic,
        Recovery
    }

    private AttackData lungeData;
    private AttackData[] beats;

    private Phase phase;
    private float phaseTimer;
    private float beatTimer;
    private int beatIndex;
    private float lungeSign;
    private float lungeTravelled;
    private bool hitboxActive;

    public SuperArtState(RobotController robot) : base(robot) { }

    public int BeatsResolved => beatIndex;

    public bool IsCinematic => phase == Phase.Cinematic;

    public void Configure(AttackData lunge, AttackData[] cinematicBeats)
    {
        lungeData = lunge;
        beats = cinematicBeats;
    }

    public override void Enter()
    {
        phase = Phase.Freeze;
        phaseTimer = 0f;
        beatTimer = 0f;
        beatIndex = 0;
        lungeTravelled = 0f;
        hitboxActive = false;

        robot.AttackConnected = false;
        robot.IsInvincible = true;
        robot.FaceOpponent();
        lungeSign = robot.transform.forward.x < 0f ? -1f : 1f;
        robot.animator.speed = 0f;

        RobotController opponent = robot.Opponent;
        if (opponent != null) opponent.FreezeFor(FreezeDuration);
        if (CombatEffectsManager.Instance != null) CombatEffectsManager.Instance.TriggerSlowMotion(0f, FreezeDuration);
        if (robot.CombatCamera != null) robot.CombatCamera.PunchZoom(FreezeZoomPunch);
        if (robot.Flash != null) robot.Flash.Pop(ActivationFlashColor, FreezeDuration);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayShoryukenRise();
    }

    public override void Tick()
    {
        switch (phase)
        {
            case Phase.Freeze:
                TickFreeze();
                return;
            case Phase.Lunge:
                TickLunge();
                return;
            case Phase.Cinematic:
                TickCinematic();
                return;
            default:
                TickRecovery();
                return;
        }
    }

    public override void Exit()
    {
        robot.IsInvincible = false;
        robot.animator.speed = 1f;
        DeactivateHitbox();
    }

    private void TickFreeze()
    {
        phaseTimer += Time.unscaledDeltaTime;
        if (phaseTimer < FreezeDuration) return;

        phase = Phase.Lunge;
        phaseTimer = 0f;
        robot.animator.speed = 1f;
        PlayAttackAnimation(lungeData);
    }

    // The lunge is the only punishable part: block or whiff ends the super, a clean hit starts the cinematic.
    private void TickLunge()
    {
        phaseTimer += Time.deltaTime;

        if (lungeData == null)
        {
            EnterRecovery();
            return;
        }

        if (robot.IsInvincible && phaseTimer >= lungeData.startupTime) robot.IsInvincible = false;

        UpdateLungeHitbox();
        Move();

        if (robot.AttackConnected)
        {
            DeactivateHitbox();
            if (ConnectedClean()) EnterCinematic();
            else EnterRecovery();
            return;
        }

        if (phaseTimer >= lungeData.ActiveEndTime) EnterRecovery();
    }

    private bool ConnectedClean()
    {
        RobotController opponent = robot.Opponent;
        return opponent != null && opponent.CurrentState is not BlockstunState;
    }

    private void UpdateLungeHitbox()
    {
        if (!hitboxActive && phaseTimer >= lungeData.startupTime && phaseTimer < lungeData.ActiveEndTime)
        {
            robot.hitboxBase.Activate(lungeData);
            hitboxActive = true;
        }
        else if (hitboxActive && phaseTimer >= lungeData.ActiveEndTime)
        {
            DeactivateHitbox();
        }
    }

    private void Move()
    {
        float forward = Mathf.Min(LungeSpeed * Time.deltaTime, LungeReach - lungeTravelled);

        RobotController opponent = robot.Opponent;
        if (opponent != null)
        {
            float gap = Mathf.Abs(opponent.transform.position.x - robot.transform.position.x) - CinematicRange;
            forward = Mathf.Min(forward, gap);
        }

        forward = Mathf.Max(forward, 0f);
        lungeTravelled += forward;

        Vector3 velocity = new Vector3(lungeSign * forward, GroundingVelocity * Time.deltaTime, 0f);
        robot.characterController.Move(velocity);
    }

    private void EnterCinematic()
    {
        if (beats == null || beats.Length == 0)
        {
            EnterRecovery();
            return;
        }

        phase = Phase.Cinematic;
        beatTimer = 0f;
        beatIndex = 0;

        RobotController opponent = robot.Opponent;
        if (opponent != null) opponent.EnterHitstun(beats.Length * BeatInterval + LockMargin, 0f, robot);
    }

    private void EnterRecovery()
    {
        phase = Phase.Recovery;
        phaseTimer = 0f;
        DeactivateHitbox();
        robot.IsInvincible = false;
    }

    private void TickCinematic()
    {
        RobotController opponent = robot.Opponent;
        if (opponent == null || (opponent.Health != null && opponent.Health.IsDead))
        {
            EnterRecovery();
            return;
        }

        ApplyGroundingForce();
        HoldDefenderInReach(opponent);

        beatTimer -= Time.deltaTime;
        if (beatTimer > 0f) return;

        bool finisher = beatIndex == beats.Length - 1;
        ResolveBeat(opponent, beats[beatIndex], finisher);
        beatIndex++;

        if (beatIndex >= beats.Length) EnterRecovery();
        else beatTimer = BeatInterval;
    }

    // Anchor sits just outside the pushbox radius so the separation solver never fights it.
    private void HoldDefenderInReach(RobotController opponent)
    {
        float anchorX = robot.transform.position.x + lungeSign * CinematicRange;
        float currentX = opponent.transform.position.x;
        float step = Mathf.MoveTowards(currentX, anchorX, AnchorSpeed * Time.deltaTime) - currentX;
        if (step == 0f) return;

        opponent.characterController.Move(new Vector3(step, 0f, 0f));
    }

    private void ResolveBeat(RobotController opponent, AttackData beat, bool finisher)
    {
        if (beat == null || opponent.Hurtbox == null) return;

        Vector3 contactPoint = opponent.characterController.bounds.center;
        opponent.Hurtbox.ReceiveHit(beat, robot, contactPoint, false);

        if (!finisher) return;

        if (robot.Flash != null) robot.Flash.Pop(FinisherFlashColor, FinisherFlashDuration);

        FightingCamera camera = robot.CombatCamera;
        if (camera == null) return;

        Vector3 impactDirection = new Vector3(lungeSign, 1f, 0f);
        camera.ShakeCamera(FinisherShakeDuration, FinisherShakeMagnitude, impactDirection);
        camera.Punch(impactDirection, FinisherCameraPunch);
        camera.PunchZoom(FinisherZoomPunch);
    }

    private void TickRecovery()
    {
        phaseTimer += Time.deltaTime;
        ApplyGroundingForce();

        float recovery = lungeData != null ? lungeData.recoveryTime : 0f;
        if (phaseTimer >= recovery) robot.EnterIdle();
    }

    private void DeactivateHitbox()
    {
        if (!hitboxActive) return;

        robot.hitboxBase.Deactivate();
        hitboxActive = false;
    }
}
