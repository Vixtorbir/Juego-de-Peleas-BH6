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
    private readonly AttackData data;
    private float stateTimer;
    private bool hitboxActive;

    public AttackState(RobotController robot, AttackData attackData) : base(robot)
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

        if (stateTimer < data.startupTime) return;

        robot.IsInAttackStartup = false;
        if (TrySpecialCancel(data)) return;
        if (TryChainCancel(data)) return;

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
            robot.ChangeState(new IdleState(robot));
        }
    }

    public override void Exit()
    {
        robot.IsInAttackStartup = false;
        robot.hitboxBase.Deactivate();
    }
}
