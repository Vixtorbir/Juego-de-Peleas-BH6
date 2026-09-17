/// <summary>
/// Crouching stance. Lowers the collision capsule so High attacks whiff, and routes punches to
/// the crouch jab and kicks to the sweep.
/// </summary>
public class CrouchState : RobotState
{
    private const float StandUpInputThreshold = -0.2f;

    public CrouchState(RobotController robot) : base(robot) { }

    public override void Enter()
    {
        robot.SetCrouchingPosture(true);
        ReturnToStance(FighterAnimatorParameters.CrouchState);
    }

    public override void Tick()
    {
        if (TryConsumeSpecialCommands()) return;

        if (robot.TryConsumeCommand(FighterCommand.LeftPunch) || robot.TryConsumeCommand(FighterCommand.RightPunch))
        {
            robot.ChangeState(new CrouchAttackState(robot, robot.crouchPunchBase));
            return;
        }

        if (robot.TryConsumeCommand(FighterCommand.LeftKick) || robot.TryConsumeCommand(FighterCommand.RightKick))
        {
            robot.ChangeState(new CrouchAttackState(robot, robot.crouchSweepBase));
            return;
        }

        if (robot.MovementInput.y > StandUpInputThreshold)
        {
            robot.ChangeState(new IdleState(robot));
        }
    }

    public override void Exit()
    {
        robot.SetCrouchingPosture(false);
    }
}
