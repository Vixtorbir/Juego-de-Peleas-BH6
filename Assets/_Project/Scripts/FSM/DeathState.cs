using UnityEngine;

/// <summary>
/// Terminal state for a knocked-out fighter. Disables offense and lets the body settle on the
/// ground; <see cref="MatchManager"/> is responsible for leaving this state via a round reset.
/// </summary>
public class DeathState : RobotState
{
    private const string DeathTrigger = "Death";

    public DeathState(RobotController robot) : base(robot) { }

    public override void Enter()
    {
        robot.IsInAttackStartup = false;

        if (robot.hitboxBase != null) robot.hitboxBase.Deactivate();
        if (robot.animator != null) robot.animator.SetTrigger(DeathTrigger);
    }

    public override void Tick()
    {
        if (!robot.IsGrounded)
        {
            robot.characterController.Move(Vector3.up * (FallVelocity * Time.deltaTime));
        }
    }
}
