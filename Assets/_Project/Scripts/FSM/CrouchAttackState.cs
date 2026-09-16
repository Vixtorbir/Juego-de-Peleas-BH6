using UnityEngine;

/// <summary>
/// Crouching normal attack. Shares the startup/active/recovery timeline of <see cref="AttackState"/>
/// but keeps the collision capsule in its crouched configuration for the whole duration.
/// </summary>
public class CrouchAttackState : RobotState
{
    private const float StayCrouchedInputThreshold = -0.2f;

    private readonly AttackData data;
    private float stateTimer;
    private bool hitboxActive;

    public CrouchAttackState(RobotController robot, AttackData attackData) : base(robot)
    {
        data = attackData;
    }

    public override void Enter()
    {
        stateTimer = 0f;
        hitboxActive = false;
        robot.IsInAttackStartup = true;
        robot.SetCrouchingPosture(true);

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

        if (stateTimer < data.TotalDuration) return;

        if (robot.MovementInput.y < StayCrouchedInputThreshold)
            robot.ChangeState(new CrouchState(robot));
        else
            robot.ChangeState(new IdleState(robot));
    }

    public override void Exit()
    {
        robot.IsInAttackStartup = false;
        robot.hitboxBase.Deactivate();
        robot.SetCrouchingPosture(false);
    }
}
