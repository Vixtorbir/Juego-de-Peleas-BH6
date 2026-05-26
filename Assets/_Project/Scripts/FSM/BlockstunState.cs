using UnityEngine;

public class BlockstunState : RobotState
{
    private float duration;
    private float timer;
    private bool startedCrouching;

    private float originalHeight;
    private Vector3 originalCenter;

    private float currentPushbackSpeed;
    private float friction = 25f;
    private RobotController atacante;

    private bool haChocadoPared = false;

    public BlockstunState(RobotController robot, float duration, bool isCrouching, float pushbackForce, RobotController atacante) : base(robot)
    {
        this.duration = duration;
        this.startedCrouching = isCrouching;
        this.currentPushbackSpeed = pushbackForce;
        this.atacante = atacante;
    }

    public override void Enter()
    {
        haChocadoPared = false;
        timer = duration;

        originalHeight = robot.characterController.height;
        originalCenter = robot.characterController.center;

        if (startedCrouching)
        {
            robot.characterController.height = originalHeight / 2f;
            robot.characterController.center = new Vector3(originalCenter.x, originalCenter.y / 2f, originalCenter.z);
            robot.transform.localScale = new Vector3(1f, 0.5f, 1f);
        }

        Renderer renderer = robot.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = Color.blue;
    }

    public override void Tick()
    {
        timer -= Time.deltaTime;

        if (currentPushbackSpeed > 0f)
        {
            currentPushbackSpeed = Mathf.MoveTowards(currentPushbackSpeed, 0f, friction * Time.deltaTime);

            float direccionX = Mathf.Sign(robot.transform.position.x - robot.opponentTarget.position.x);
            Vector3 direccionPushback = new Vector3(direccionX, 0f, 0f);

            if (haChocadoPared)
            {
                direccionPushback = -direccionPushback;
            }

            float posXAntes = robot.transform.position.x;
            robot.characterController.Move(direccionPushback * currentPushbackSpeed * Time.deltaTime);
            float posXDespues = robot.transform.position.x;

            float desplazamientoReal = Mathf.Abs(posXDespues - posXAntes);
            float desplazamientoDeseado = currentPushbackSpeed * Time.deltaTime;

            if (!haChocadoPared && desplazamientoReal < desplazamientoDeseado - 0.001f && atacante != null)
            {
                haChocadoPared = true;

                atacante.characterController.Move(-direccionPushback * currentPushbackSpeed * 0.3f * Time.deltaTime);

                currentPushbackSpeed *= 0.35f;
            }
        }

        if (timer <= 0f)
        {
            if (robot.MovementInput.y < -0.2f)
                robot.ChangeState(new CrouchState(robot));
            else
                robot.ChangeState(new IdleState(robot));
        }
    }

    public override void Exit()
    {
        robot.characterController.height = originalHeight;
        robot.characterController.center = originalCenter;
        robot.transform.localScale = Vector3.one;

        Renderer renderer = robot.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = Color.white;
    }
}