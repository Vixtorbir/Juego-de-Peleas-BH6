/// <summary>
/// Crouching normal attack. Shares the startup/active/recovery timeline and the on-connect special
/// and chain cancel rules of <see cref="AttackState"/>, but keeps the collision capsule crouched
/// for the whole duration and returns to the crouch when the stick is still held down.
/// </summary>
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
