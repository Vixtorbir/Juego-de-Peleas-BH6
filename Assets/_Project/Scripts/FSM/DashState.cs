using UnityEngine;

/// <summary>
/// Fixed-duration burst of movement towards or away from the opponent, triggered by a
/// double tap on the horizontal axis. The burst stays glued to the floor so a dash never
/// lifts the capsule off its ground contact.
/// </summary>
public class DashState : RobotState
{
    private const float DashDuration = 0.18f;
    private const float DashSpeed = 14f;

    private bool isForward;
    private float stateTimer;
    private Vector3 dashDirection;

    public DashState(RobotController robot) : base(robot) { }

    public void Configure(bool forward)
    {
        isForward = forward;
    }

    public override void Enter()
    {
        stateTimer = DashDuration;

        robot.FaceOpponent();
        dashDirection = isForward ? robot.transform.forward : -robot.transform.forward;

        if (isForward)
            PlayTimedAnimation(FighterAnimatorParameters.DashForward, robot.dashForwardClip, DashDuration);
        else
            PlayTimedAnimation(FighterAnimatorParameters.DashBackward, robot.dashBackwardClip, DashDuration);
    }

    public override void Tick()
    {
        stateTimer -= Time.deltaTime;

        Vector3 velocity = dashDirection * DashSpeed;
        velocity.y = GroundingVelocity;
        robot.characterController.Move(velocity * Time.deltaTime);

        if (stateTimer <= 0f)
        {
            robot.EnterIdle();
        }
    }
}
