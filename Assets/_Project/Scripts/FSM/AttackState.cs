using UnityEngine;

/// <summary>
/// Standing normal attack driven by <see cref="AttackData"/> frame data: startup (counter-hit
/// vulnerable), active (hitbox enabled) and recovery, during which buffered commands can cancel
/// into any configured <see cref="ComboBranch"/>.
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

        if (!string.IsNullOrEmpty(data.animationTrigger))
        {
            robot.animator.SetTrigger(data.animationTrigger);
        }
    }

    public override void Tick()
    {
        stateTimer += Time.deltaTime;

        if (stateTimer < data.startupTime) return;

        robot.IsInAttackStartup = false;

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
        }

        if (TryCancelIntoComboBranch()) return;

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

    private bool TryCancelIntoComboBranch()
    {
        if (data.comboBranches == null) return false;

        for (int i = 0; i < data.comboBranches.Count; i++)
        {
            ComboBranch branch = data.comboBranches[i];
            if (branch.nextAttack == null || !robot.TryConsumeCommand(branch.inputCommand)) continue;

            robot.ChangeState(new AttackState(robot, branch.nextAttack));
            return true;
        }

        return false;
    }
}
