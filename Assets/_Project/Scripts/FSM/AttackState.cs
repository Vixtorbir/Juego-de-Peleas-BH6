using UnityEngine;

public class AttackState : RobotState
{
    private float attackDuration = 0.5f;
    private float stateTimer;

    public AttackState(RobotController robot) : base(robot) { }

    public override void Enter()
    {
        Debug.Log("¡Gancho izquierdo! (Movimiento bloqueado)");
        stateTimer = attackDuration;

        // TODO: robot.animator.SetTrigger("LeftPunch");

        robot.transform.localScale = new Vector3(1f, 1f, 1.2f);
    }

    public override void Tick()
    {
        stateTimer -= Time.deltaTime;

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