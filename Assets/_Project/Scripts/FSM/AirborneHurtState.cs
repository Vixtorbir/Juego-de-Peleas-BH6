using UnityEngine;

/// <summary>
/// Juggle state entered after a launcher or an air hit. The fighter has no control until it
/// lands; any hit received while airborne re-enters this state with a fresh launch velocity.
/// </summary>
public class AirborneHurtState : RobotState
{
    private float verticalVelocity;

    public AirborneHurtState(RobotController robot, float launchForce) : base(robot)
    {
        verticalVelocity = launchForce;
    }

    public override void Tick()
    {
        verticalVelocity += Gravity * Time.deltaTime;
        robot.characterController.Move(Vector3.up * (verticalVelocity * Time.deltaTime));

        if (robot.characterController.isGrounded && verticalVelocity < 0f)
        {
            robot.characterController.Move(Vector3.up * GroundSnapVelocity);
            robot.ChangeState(new IdleState(robot));
        }
    }
}
