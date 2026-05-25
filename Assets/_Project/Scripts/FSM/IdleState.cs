using UnityEngine;

public class IdleState : RobotState
{
    public IdleState(RobotController robot) : base(robot) { }

    public override void Enter()
    {
        // Reseteamos las variables de movimiento en el Animator
        robot.animator.SetFloat("InputX", 0f);
        robot.animator.SetFloat("InputY", 0f);
    }

    public override void Tick()
    {
        // 1. PRIORIDAD VERTICAL: Salto y Agacharse
        if (robot.MovementInput.y > 0.6f)
        {
            robot.ChangeState(new JumpState(robot));
            return; // Cortamos aquí para que no evalúe el resto
        }
        else if (robot.MovementInput.y < -0.6f)
        {
            robot.ChangeState(new CrouchState(robot));
            return;
        }

        // 2. MOVIMIENTO HORIZONTAL: Caminar
        // Usamos Mathf.Abs para ignorar si el valor es positivo (derecha) o negativo (izquierda)
        if (Mathf.Abs(robot.MovementInput.x) > 0.01f)
        {
            robot.ChangeState(new WalkState(robot));
        }
    }
}