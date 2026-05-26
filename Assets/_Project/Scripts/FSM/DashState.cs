using UnityEngine;

public class DashState : RobotState
{
    private bool isForward;
    private float stateTimer;

    private float dashDuration = 0.18f;
    private float dashSpeed = 14f;
    private Vector3 dashDirection;

    public DashState(RobotController robot, bool isForward) : base(robot)
    {
        this.isForward = isForward;
    }

    public override void Enter()
    {
        stateTimer = dashDuration;

        Vector3 directionToOpponent = robot.opponentTarget.position - robot.transform.position;
        directionToOpponent.y = 0f;
        if (directionToOpponent != Vector3.zero)
        {
            robot.transform.rotation = Quaternion.LookRotation(directionToOpponent);
        }

        dashDirection = isForward ? robot.transform.forward : -robot.transform.forward;

        if (isForward)
        {
            Debug.Log("¡FORWARD DASH!");
            robot.transform.localScale = new Vector3(0.8f, 1f, 1.2f);
        }
        else
        {
            Debug.Log("¡BACKDASH!");
            robot.transform.localScale = new Vector3(1.2f, 1f, 0.8f);
        }
    }

    public override void Tick()
    {
        stateTimer -= Time.deltaTime;

        robot.characterController.Move(dashDirection * dashSpeed * Time.deltaTime);

        if (stateTimer <= 0f)
        {
            robot.ChangeState(new IdleState(robot));
        }
    }

    public override void Exit()
    {
        robot.transform.localScale = Vector3.one;
    }
}