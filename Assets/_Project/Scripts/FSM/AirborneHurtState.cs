using UnityEngine;

public class AirborneHurtState : RobotState
{
    private float verticalVelocity;
    private float gravity = -35f;

    public AirborneHurtState(RobotController robot, float launchForce) : base(robot)
    {
        verticalVelocity = launchForce;
    }

    public override void Enter()
    {
        Debug.Log("¡JUGGLE! El rival está en el aire.");

        robot.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
    }

    public override void Tick()
    {
        verticalVelocity += gravity * Time.deltaTime;
        robot.characterController.Move(new Vector3(0, verticalVelocity, 0) * Time.deltaTime);

        if (robot.characterController.isGrounded && verticalVelocity < 0f)
        {
            robot.characterController.Move(new Vector3(0, -0.5f, 0));

            robot.ChangeState(new IdleState(robot));
        }
    }

    public override void Exit()
    {
        robot.transform.localScale = Vector3.one;
    }
}