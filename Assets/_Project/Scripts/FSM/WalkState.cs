using UnityEngine;

public class WalkState : RobotState
{
    private float smoothTime = 0.1f;
    private Vector2 currentVelocity;
    private Vector2 smoothInput;

    private Transform cameraTransform;

    public WalkState(RobotController robot) : base(robot)
    {
        cameraTransform = Camera.main.transform;
    }

    public override void Tick()
    {
        // --- LEER INPUT BUFFER PARA DASH ---
        if (robot.TryConsumeCommand(FighterCommand.DashForward))
        {
            robot.ChangeState(new DashState(robot, true)); // true = Forward
            return;
        }

        if (robot.TryConsumeCommand(FighterCommand.DashBackward))
        {
            robot.ChangeState(new DashState(robot, false)); // false = Backward
            return;
        }

        // 1. LEER EL INPUT BUFFER AL CAMINAR (Cancela la caminata si atacamos)
        if (robot.TryConsumeCommand(FighterCommand.LeftPunch))
        {
            robot.ChangeState(new AttackState(robot, robot.leftPunchBase));
            return;
        }

        if (robot.TryConsumeCommand(FighterCommand.RightPunch))
        {
            Vector3 directionToOpponentAttack = robot.opponentTarget.position - robot.transform.position;
            directionToOpponentAttack.y = 0f;
            float screenSideAttack = Mathf.Sign(Vector3.Dot(directionToOpponentAttack.normalized, cameraTransform.right));
            float forwardMovementAttack = robot.MovementInput.x * screenSideAttack;

            if (forwardMovementAttack > 0.5f)
            {
                robot.ChangeState(new AttackState(robot, robot.launcherBase));
            }
            else
            {
                robot.ChangeState(new AttackState(robot, robot.rightPunchBase));
            }
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

        // 2. GESTIÓN DE SALTO Y AGACHARSE (Eje Y del Joystick)
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

        // 3. CONTROL DE PARADA (Si soltamos el joystick)
        if (Mathf.Abs(robot.MovementInput.x) <= 0.01f)
        {
            robot.ChangeState(new IdleState(robot));
            return;
        }

        smoothInput = Vector2.SmoothDamp(smoothInput, robot.MovementInput, ref currentVelocity, smoothTime);

        Vector3 directionToOpponent = robot.opponentTarget.position - robot.transform.position;
        directionToOpponent.y = 0f;

        if (directionToOpponent != Vector3.zero)
        {
            robot.transform.rotation = Quaternion.LookRotation(directionToOpponent);
        }

        // 4. LA MAGIA DEL INPUT RELATIVO A LA CÁMARA
        float screenSide = Mathf.Sign(Vector3.Dot(directionToOpponent.normalized, cameraTransform.right));

        float forwardMovement = smoothInput.x * screenSide;

        robot.animator.SetFloat("InputY", forwardMovement);
        robot.animator.SetFloat("InputX", 0f);

        // 5. APLICAR EL MOVIMIENTO FÍSICO
        Vector3 moveDirection = robot.transform.forward * forwardMovement;
        float tempSpeed = 3f;
        robot.characterController.Move(moveDirection * tempSpeed * Time.deltaTime);
    }
}