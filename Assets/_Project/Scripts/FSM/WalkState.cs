using UnityEngine;

public class WalkState : RobotState
{
    private float smoothTime = 0.1f;
    private Vector2 currentVelocity;
    private Vector2 smoothInput;

    private Transform cameraTransform;

    public WalkState(RobotController robot) : base(robot)
    {
        // Cacheamos la cámara al entrar para no buscarla cada frame
        cameraTransform = Camera.main.transform;
    }

    public override void Tick()
    {
        // 1. GESTIÓN DE SALTO Y AGACHARSE (Eje Y del Joystick)
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

        // Pasamos los valores al Animator
        robot.animator.SetFloat("InputY", forwardMovement);
        robot.animator.SetFloat("InputX", 0f);

        // 5. APLICAR EL MOVIMIENTO FÍSICO
        Vector3 moveDirection = robot.transform.forward * forwardMovement;
        float tempSpeed = 3f;
        robot.characterController.Move(moveDirection * tempSpeed * Time.deltaTime);
    }
}