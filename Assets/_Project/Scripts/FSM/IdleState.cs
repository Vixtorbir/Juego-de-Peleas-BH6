using UnityEngine;

/// <summary>
/// Neutral standing state. Resets combo scaling on the defender side and dispatches buffered
/// commands with the priority specials > dashes > normals > jump/crouch > walk.
/// </summary>
public class IdleState : RobotState
{
    private const float VerticalInputThreshold = 0.6f;
    private const float WalkInputThreshold = 0.01f;

    public IdleState(RobotController robot) : base(robot) { }

    public override void Enter()
    {
        robot.animator.SetFloat("InputX", 0f);
        robot.animator.SetFloat("InputY", 0f);
        if (robot.Hurtbox != null) robot.Hurtbox.ResetCombo();
    }

    public override void Tick()
    {
        if (TryConsumeSpecialCommands()) return;
        if (TryConsumeDashCommands()) return;
        if (TryConsumeNormalCommands()) return;

        if (robot.MovementInput.y > VerticalInputThreshold)
        {
            robot.ChangeState(new JumpState(robot));
            return;
        }

        if (robot.MovementInput.y < -VerticalInputThreshold)
        {
            robot.ChangeState(new CrouchState(robot));
            return;
        }

        if (Mathf.Abs(robot.MovementInput.x) > WalkInputThreshold)
        {
            robot.ChangeState(new WalkState(robot));
            return;
        }

        ApplyGroundingForce();
    }

    private bool TryConsumeNormalCommands()
    {
        if (robot.TryConsumeCommand(FighterCommand.LeftPunch))
        {
            robot.ChangeState(new AttackState(robot, robot.leftPunchBase));
            return true;
        }

        if (robot.TryConsumeCommand(FighterCommand.RightPunch))
        {
            robot.ChangeState(new AttackState(robot, robot.rightPunchBase));
            return true;
        }

        if (robot.TryConsumeCommand(FighterCommand.LeftKick))
        {
            robot.ChangeState(new AttackState(robot, robot.leftKickBase));
            return true;
        }

        if (robot.TryConsumeCommand(FighterCommand.RightKick))
        {
            robot.ChangeState(new AttackState(robot, robot.rightKickBase));
            return true;
        }

        return false;
    }
}
