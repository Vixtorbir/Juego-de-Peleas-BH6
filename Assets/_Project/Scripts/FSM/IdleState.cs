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

    public override bool CanGuard => true;

    public override void Enter()
    {
        robot.animator.SetFloat(FighterAnimatorParameters.InputX, 0f);
        robot.animator.SetFloat(FighterAnimatorParameters.InputY, 0f);
        if (robot.Hurtbox != null) robot.Hurtbox.ResetCombo();
        robot.FaceOpponent();
        ReturnToStance(FighterAnimatorParameters.LocomotionState);
    }

    public override void Tick()
    {
        robot.FaceOpponent();

        if (TryConsumeSpecialCommands()) return;
        if (TryConsumeDashCommands()) return;
        if (TryConsumeNormalCommands()) return;

        if (robot.MovementInput.y > VerticalInputThreshold)
        {
            robot.EnterJump();
            return;
        }

        if (robot.MovementInput.y < -VerticalInputThreshold)
        {
            robot.EnterCrouch();
            return;
        }

        if (Mathf.Abs(robot.MovementInput.x) > WalkInputThreshold)
        {
            robot.EnterWalk();
            return;
        }

        ApplyGroundingForce();
    }

    private bool TryConsumeNormalCommands()
    {
        if (robot.TryConsumeCommand(FighterCommand.LeftPunch))
        {
            robot.EnterAttack(robot.leftPunchBase);
            return true;
        }

        if (robot.TryConsumeCommand(FighterCommand.RightPunch))
        {
            robot.EnterAttack(robot.rightPunchBase);
            return true;
        }

        if (robot.TryConsumeCommand(FighterCommand.LeftKick))
        {
            robot.EnterAttack(robot.leftKickBase);
            return true;
        }

        if (robot.TryConsumeCommand(FighterCommand.RightKick))
        {
            robot.EnterAttack(robot.rightKickBase);
            return true;
        }

        return false;
    }
}
