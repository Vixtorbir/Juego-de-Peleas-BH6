using UnityEngine;

public class CrouchState : RobotState
{
    private float originalHeight;
    private Vector3 originalCenter;

    public CrouchState(RobotController robot) : base(robot) { }

    public override void Enter()
    {
        originalHeight = robot.characterController.height;
        originalCenter = robot.characterController.center;

        robot.characterController.height = originalHeight / 2f;
        robot.characterController.center = new Vector3(originalCenter.x, originalCenter.y / 2f, originalCenter.z);

        robot.transform.localScale = new Vector3(1f, 0.5f, 1f);
    }

    public override void Tick()
    {
        // 1. LEER INPUT BUFFER (Prioridad de ataque)
        // Agrupamos para que cualquier puñetazo agachado lance el ataque recto
        if (robot.TryConsumeCommand(FighterCommand.LeftPunch) || robot.TryConsumeCommand(FighterCommand.RightPunch))
        {
            robot.ChangeState(new CrouchAttackState(robot, robot.crouchPunchBase));
            return;
        }

        // Agrupamos para que cualquier patada agachada lance el barrido (Low Kick)
        if (robot.TryConsumeCommand(FighterCommand.LeftKick) || robot.TryConsumeCommand(FighterCommand.RightKick))
        {
            robot.ChangeState(new CrouchAttackState(robot, robot.crouchSweepBase));
            return;
        }

        // 2. Si soltamos el joystick hacia abajo, nos levantamos
        if (robot.MovementInput.y > -0.2f)
        {
            robot.ChangeState(new IdleState(robot));
        }
    }

    public override void Exit()
    {
        robot.characterController.height = originalHeight;
        robot.characterController.center = originalCenter;
        robot.transform.localScale = Vector3.one;
    }
}