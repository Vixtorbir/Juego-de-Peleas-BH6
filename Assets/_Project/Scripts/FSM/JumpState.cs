using UnityEngine;

public class JumpState : RobotState
{
    private float verticalVelocity;
    private Vector3 horizontalVelocity;

    private float jumpForce = 12f;
    private float gravity = -35f;
    private float horizontalAirSpeed = 4f; 

    private Transform cameraTransform;

    public JumpState(RobotController robot) : base(robot)
    {
        cameraTransform = Camera.main.transform;
    }

    public override void Enter()
    {
        // 1. APLICAMOS LA FUERZA VERTICAL INICIAL
        verticalVelocity = jumpForce;

        // 2. CALCULAMOS LA TRAYECTORIA HORIZONTAL (Diagonal)
        Vector3 directionToOpponent = robot.opponentTarget.position - robot.transform.position;
        directionToOpponent.y = 0f;

        if (directionToOpponent != Vector3.zero)
        {
            robot.transform.rotation = Quaternion.LookRotation(directionToOpponent);
        }

        float screenSide = Mathf.Sign(Vector3.Dot(directionToOpponent.normalized, cameraTransform.right));

        float forwardMovement = robot.MovementInput.x * screenSide;

        horizontalVelocity = robot.transform.forward * (forwardMovement * horizontalAirSpeed);

        Debug.Log(forwardMovement == 0 ? "Salto Neutro" : forwardMovement > 0 ? "Salto hacia Adelante" : "Salto hacia Atrás");
    }

    public override void Tick()
    {
        // 1. MATEMÁTICAS DE GRAVEDAD
        verticalVelocity += gravity * Time.deltaTime;

        // 2. MOVIMIENTO FÍSICO COMBINADO (Diagonal)
        Vector3 finalMove = horizontalVelocity + new Vector3(0, verticalVelocity, 0);
        robot.characterController.Move(finalMove * Time.deltaTime);

        // 3. DETECCIÓN DE ATERRIZAJE
        if (robot.characterController.isGrounded && verticalVelocity < 0f)
        {
            robot.characterController.Move(new Vector3(0, -0.5f, 0));
            robot.ChangeState(new IdleState(robot));
        }
    }
}