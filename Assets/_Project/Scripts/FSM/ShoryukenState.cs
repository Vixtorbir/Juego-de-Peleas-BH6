using UnityEngine;

public class ShoryukenState : RobotState
{
    private AttackData attackData;
    private float timer;

    // Variables físicas del salto
    private float verticalVelocity;
    private float gravity = -25f;
    private float forwardSpeed = 4f;

    public ShoryukenState(RobotController robot, AttackData data) : base(robot)
    {
        this.attackData = data;
    }

    public override void Enter()
    {
        timer = 0f;
        verticalVelocity = 14f; // Fuerza explosiva hacia arriba

        // Encendemos la hitbox al instante
        if (robot.hitboxBase != null && attackData != null)
        {
            robot.hitboxBase.ActivarHitbox(attackData);
        }

        // TODO: robot.animator.SetTrigger("Shoryuken");

        // EFECTO VISUAL: El robot se vuelve naranja fuego
        Renderer renderer = robot.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = new Color(1f, 0.5f, 0f);
    }

    public override void Tick()
    {
        timer += Time.deltaTime;

        // 1. Matemáticas del salto (Gravedad)
        verticalVelocity += gravity * Time.deltaTime;

        // 2. Dirección hacia el oponente para avanzar en el aire
        float dirX = Mathf.Sign(robot.opponentTarget.position.x - robot.transform.position.x);
        Vector3 moveDir = new Vector3(dirX * forwardSpeed, verticalVelocity, 0f);

        // 3. Movemos el robot
        robot.characterController.Move(moveDir * Time.deltaTime);

        // 4. Apagamos el daño cuando se acaba el "Active Time" del ataque
        if (attackData != null && timer > attackData.activeTime && robot.hitboxBase != null)
        {
            robot.hitboxBase.DesactivarHitbox();
        }

        // 5. Condición de salida: Si estamos cayendo (velocidad negativa) y tocamos el suelo
        if (verticalVelocity < 0f && (robot.IsGrounded || robot.characterController.isGrounded))
        {
            robot.ChangeState(new IdleState(robot));
        }
    }

    public override void Exit()
    {
        // Seguridad por si el estado se interrumpe
        if (robot.hitboxBase != null) robot.hitboxBase.DesactivarHitbox();

        Renderer renderer = robot.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = Color.white;
    }
}