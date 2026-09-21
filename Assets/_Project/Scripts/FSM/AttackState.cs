using UnityEngine;

/// <summary>
/// Standing normal attack driven by <see cref="AttackData"/> frame data: startup (counter-hit
/// vulnerable), active (hitbox enabled) and recovery. The moment the attack connects, hit or
/// block, the cancel window opens: a buffered special interrupts into the special, holding up on a
/// jump-cancelable attack launches a super jump to chase the opponent, a buffered chain command
/// interrupts into the matching <see cref="ComboBranch"/> or the next gatling tier, and a buffered
/// dash on a dash-cancelable attack interrupts into the dash. The window stays open until the
/// attack's cancel point, so a slightly late press still chains. Crouching and jumping variants
/// derive from this class and override only posture, motion and the stance they return to.
/// </summary>
public class AttackState : RobotState
{
    private const float JumpCancelInputThreshold = 0.6f;

    private AttackData data;
    private float stateTimer;
    private bool hitboxActive;

    public AttackState(RobotController robot) : base(robot) { }

    public AttackData Data => data;

    /// <summary>The buffer only ages while the cancel window is open, so a press during startup or after the window closes waits for the next actionable state.</summary>
    public override bool AcceptsCommands => IsCancelWindowOpen;

    protected float StateTimer => stateTimer;

    private bool IsCancelWindowOpen => data != null && robot.AttackConnected && stateTimer >= data.startupTime && stateTimer < data.CancelWindowEnd;

    public void Configure(AttackData attackData)
    {
        data = attackData;
    }

    public override void Enter()
    {
        stateTimer = 0f;
        hitboxActive = false;
        robot.IsInAttackStartup = true;
        robot.AttackConnected = false;
        PlayAttackAnimation(data);
    }

    public override void Tick()
    {
        stateTimer += Time.deltaTime;
        if (IntegrateMotion()) return;

        if (data == null)
        {
            ExitToStance();
            return;
        }

        if (stateTimer < data.startupTime) return;

        robot.IsInAttackStartup = false;
        if (stateTimer < data.CancelWindowEnd && TryCancels()) return;

        if (stateTimer < data.ActiveEndTime)
        {
            if (!hitboxActive)
            {
                robot.hitboxBase.Activate(data);
                hitboxActive = true;
            }
            return;
        }

        DeactivateHitbox(true);

        if (stateTimer >= data.TotalDuration)
        {
            ExitToStance();
        }
    }

    public override void Exit()
    {
        robot.IsInAttackStartup = false;
        robot.hitboxBase.Deactivate();
    }

    /// <summary>
    /// Moves the fighter for this frame. Grounded attacks stay glued to the floor. Returns true when
    /// the motion ended the attack (an air attack touching the ground), so the timeline stops.
    /// </summary>
    protected virtual bool IntegrateMotion()
    {
        ApplyGroundingForce();
        return false;
    }

    /// <summary>
    /// Cancel priority once the attack has connected: special, jump, chain, dash. Returns true on transition.
    /// </summary>
    protected virtual bool TryCancels()
    {
        if (TrySpecialCancel(data)) return true;
        if (TryJumpCancel()) return true;
        if (TryChainCancel(data, false)) return true;
        return TryDashCancel();
    }

    /// <summary>
    /// Stance the fighter settles into once the recovery frames elapse. Standing attacks always
    /// return to Idle; crouching attacks override this to honour a held-down stick.
    /// </summary>
    protected virtual void ExitToStance()
    {
        robot.EnterIdle();
    }

    /// <summary>
    /// Ends the active window. A swing that never touched the opponent plays the whiff sound when
    /// <paramref name="reportWhiff"/> is set.
    /// </summary>
    protected void DeactivateHitbox(bool reportWhiff)
    {
        if (!hitboxActive) return;

        robot.hitboxBase.Deactivate();
        hitboxActive = false;

        if (reportWhiff && !robot.AttackConnected && AudioManager.Instance != null) AudioManager.Instance.PlayWhiff();
    }

    /// <summary>
    /// Jump cancel: a connected jump-cancelable attack with the stick held up launches the super
    /// jump immediately, so the attacker rises with the body it just launched.
    /// </summary>
    private bool TryJumpCancel()
    {
        if (!data.jumpCancelable || !robot.AttackConnected || robot.MovementInput.y <= JumpCancelInputThreshold) return false;

        robot.EnterJump(robot.Rules.jumpCancelForce);
        return true;
    }

    /// <summary>Dash cancel: a connected dash-cancelable attack yields to a buffered dash.</summary>
    private bool TryDashCancel()
    {
        if (!data.dashCancelable || !robot.AttackConnected) return false;
        return TryConsumeDashCommands();
    }
}
