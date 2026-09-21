/// <summary>
/// Terminal state for a knocked-out fighter. Disables offense and lets the body settle on the
/// ground; <see cref="MatchManager"/> is responsible for leaving this state via a round reset.
/// </summary>
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
