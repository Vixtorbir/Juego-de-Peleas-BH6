using UnityEngine;

/// <summary>
/// Ballistic jump. The horizontal component is locked at takeoff from the stick direction
/// relative to the opponent, so the arc cannot be steered mid-air.
/// </summary>
public class JumpState : RobotState
{
    private const float JumpForce = 12f;
    private const float HorizontalAirSpeed = 4f;

    private float verticalVelocity;
    private Vector3 horizontalVelocity;

    public JumpState(RobotController robot) : base(robot) { }

    public override void Enter()
    {
        verticalVelocity = JumpForce;

        robot.FaceOpponent();

        float forwardAmount = robot.MovementInput.x * robot.GetScreenFacingSign();
        horizontalVelocity = robot.transform.forward * (forwardAmount * HorizontalAirSpeed);
    }

    public override void Tick()
    {
        verticalVelocity += Gravity * Time.deltaTime;

        Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;
        robot.characterController.Move(velocity * Time.deltaTime);

        if (robot.characterController.isGrounded && verticalVelocity < 0f)
        {
            robot.characterController.Move(Vector3.up * GroundSnapVelocity);
            robot.ChangeState(new IdleState(robot));
        }
    }
}
