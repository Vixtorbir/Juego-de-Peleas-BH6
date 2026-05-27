using UnityEngine;

public class IdleState : RobotState
{
    public IdleState(RobotController robot) : base(robot) { }

    public override void Enter()
    {
        robot.animator.SetFloat("InputX", 0f);
        robot.animator.SetFloat("InputY", 0f);

        Hurtbox hurtbox = robot.GetComponentInChildren<Hurtbox>();
        if (hurtbox != null)
        {
            hurtbox.ResetCombo();
        }

    }

    public override void Tick()
    {
        // --- LEER INPUT BUFFER PARA DASH ---
        if (robot.TryConsumeCommand(FighterCommand.DashForward))
        {
            robot.ChangeState(new DashState(robot, true));
            return;
        }

        if (robot.TryConsumeCommand(FighterCommand.DashBackward))
        {
            robot.ChangeState(new DashState(robot, false)); 
            return;
        }

        // 1. LEER EL INPUT BUFFER PRIMERO (Prioridad de ataque)
        if (robot.TryConsumeCommand(FighterCommand.LeftPunch))
        {
            robot.ChangeState(new AttackState(robot, robot.leftPunchBase));
            return;
        }

        if (robot.TryConsumeCommand(FighterCommand.RightPunch))
        {
            robot.ChangeState(new AttackState(robot, robot.rightPunchBase));
            return;
        }

        if (robot.TryConsumeCommand(FighterCommand.LeftKick))
        {
            robot.ChangeState(new AttackState(robot, robot.leftKickBase));
            return;
        }

        if (robot.TryConsumeCommand(FighterCommand.RightKick))
        {
            robot.ChangeState(new AttackState(robot, robot.rightKickBase));
            return;
        }

        // 2. PRIORIDAD VERTICAL: Salto y Agacharse
        if (robot.MovementInput.y > 0.6f)
        {
            robot.ChangeState(new JumpState(robot));
            return;
        }
        else if (robot.MovementInput.y < -0.6f)
        {
            robot.ChangeState(new CrouchState(robot));
            return;
        }

        // 3. MOVIMIENTO HORIZONTAL: Caminar
        if (Mathf.Abs(robot.MovementInput.x) > 0.01f)
        {
            robot.ChangeState(new WalkState(robot));
        }

        if (!robot.characterController.isGrounded)
        {
            robot.characterController.Move(new Vector3(0, -9.81f, 0) * Time.deltaTime);
        }
        else
        {
            robot.characterController.Move(new Vector3(0, -0.5f, 0) * Time.deltaTime);
        }
    }
}