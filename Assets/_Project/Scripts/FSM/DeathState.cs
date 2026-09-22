public class DeathState : RobotState
{
    public DeathState(RobotController robot) : base(robot) { }

    public override void Enter()
    {
        robot.IsInAttackStartup = false;
        robot.hitboxBase.Deactivate();
        PlayAnimationState(FighterAnimatorParameters.Death);
    }

    public override void Tick()
    {
        ApplyGroundingForce();
    }
}
