public class CrouchAttackState : AttackState
{
    private const float StayCrouchedInputThreshold = -0.2f;

    public CrouchAttackState(RobotController robot) : base(robot) { }

    public override bool IsCrouchingPosture => true;

    public override void Enter()
    {
        robot.SetCrouchingPosture(true);
        base.Enter();
    }

    public override void Exit()
    {
        base.Exit();
        robot.SetCrouchingPosture(false);
    }

    protected override void ExitToStance()
    {
        if (robot.MovementInput.y < StayCrouchedInputThreshold)
            robot.EnterCrouch();
        else
            robot.EnterIdle();
    }
}
