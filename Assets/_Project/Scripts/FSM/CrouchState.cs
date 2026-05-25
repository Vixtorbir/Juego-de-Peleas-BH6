using UnityEngine;

public class CrouchState : RobotState
{
    private float originalHeight;
    private Vector3 originalCenter;

    public CrouchState(RobotController robot) : base(robot) { }

    public override void Enter()
    {
        Debug.Log("¡Agachado! (Colisionador a la mitad)");

        // Guardamos proporciones
        originalHeight = robot.characterController.height;
        originalCenter = robot.characterController.center;

        // Comprimimos la cápsula física
        robot.characterController.height = originalHeight / 2f;
        robot.characterController.center = new Vector3(originalCenter.x, originalCenter.y / 2f, originalCenter.z);

        // EFECTO VISUAL TEMPORAL: Aplastamos el modelo 3D a la mitad en el eje Y
        robot.transform.localScale = new Vector3(1f, 0.5f, 1f);
    }

    public override void Tick()
    {
        // Si soltamos el joystick hacia abajo, nos levantamos
        if (robot.MovementInput.y > -0.2f)
        {
            robot.ChangeState(new IdleState(robot));
        }
    }

    public override void Exit()
    {
        Debug.Log("¡Levantándose! Volviendo a Idle...");

        // Restauramos físicas
        robot.characterController.height = originalHeight;
        robot.characterController.center = originalCenter;

        // Restauramos el modelo 3D a su tamaño original
        robot.transform.localScale = new Vector3(1f, 1f, 1f);
    }
}