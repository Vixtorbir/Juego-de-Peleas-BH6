using UnityEngine;

public class IdleState : RobotState
{
    public IdleState(RobotController robot) : base(robot) { }

    public override void Enter()
    {
        robot.animator.SetFloat("InputX", 0f);
        robot.animator.SetFloat("InputY", 0f);
    }

    public override void Tick()
    {
        if (robot.MovementInput.sqrMagnitude > 0.01f)
        {
            robot.ChangeState(new WalkState(robot));
        }
    }
}