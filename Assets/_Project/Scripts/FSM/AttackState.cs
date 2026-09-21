using UnityEngine;

/// <summary>
/// Standing normal attack driven by <see cref="AttackData"/> frame data: startup (counter-hit
/// vulnerable), active (hitbox enabled) and recovery. The moment the attack connects, hit or
/// block, the cancel window opens: a buffered special interrupts into the special, and a buffered
/// chain command interrupts into the matching <see cref="ComboBranch"/>. Both windows stay open
/// through the rest of the active and recovery frames, so a slightly late press still chains.
/// </summary>
public class AttackState : RobotState
{
    private AttackData data;
    private float stateTimer;
    private bool hitboxActive;

    public AttackState(RobotController robot) : base(robot) { }

    public AttackData Data => data;

    /// <summary>The buffer only ages while the cancel window is open, so a press during startup or after the window closes waits for the next actionable state.</summary>
    public override bool AcceptsCommands => IsCancelWindowOpen;

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
        ApplyGroundingForce();

        if (data == null)
        {
            ExitToStance();
            return;
        }

        if (stateTimer < data.startupTime) return;

        robot.IsInAttackStartup = false;
        if (stateTimer < data.CancelWindowEnd)
        {
            if (TrySpecialCancel(data)) return;
            if (TryChainCancel(data)) return;
        }

        if (stateTimer < data.ActiveEndTime)
        {
            if (!hitboxActive)
            {
                robot.hitboxBase.Activate(data);
                hitboxActive = true;
            }
            return;
        }

        if (hitboxActive)
        {
            robot.hitboxBase.Deactivate();
            hitboxActive = false;

            if (!robot.AttackConnected && AudioManager.Instance != null) AudioManager.Instance.PlayWhiff();
        }

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
    /// Stance the fighter settles into once the recovery frames elapse. Standing attacks always
    /// return to Idle; crouching attacks override this to honour a held-down stick.
    /// </summary>
    protected virtual void ExitToStance()
    {
        robot.EnterIdle();
    }
}
