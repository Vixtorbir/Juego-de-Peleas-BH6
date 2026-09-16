using UnityEngine;

/// <summary>
/// Fixed-duration burst of movement towards or away from the opponent, triggered by a
/// double tap on the horizontal axis.
/// </summary>
public class DashState : RobotState
{
    private const float DashDuration = 0.18f;
    private const float DashSpeed = 14f;

    private readonly bool isForward;
    private float stateTimer;
    private Vector3 dashDirection;

    public DashState(RobotController robot, bool isForward) : base(robot)
    {
        this.isForward = isForward;
    }

    public override void Enter()
    {
        stateTimer = DashDuration;

        robot.FaceOpponent();
        dashDirection = isForward ? robot.transform.forward : -robot.transform.forward;
    }

    public override void Tick()
    {
        stateTimer -= Time.deltaTime;
        robot.characterController.Move(dashDirection * (DashSpeed * Time.deltaTime));

        if (stateTimer <= 0f)
        {
            robot.ChangeState(new IdleState(robot));
        }
    }
}
