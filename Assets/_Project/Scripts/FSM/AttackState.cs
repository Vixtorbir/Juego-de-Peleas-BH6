using UnityEngine;

public class AttackState : RobotState
{
    private AttackData data;
    private float stateTimer;
    private bool hasActivatedHitbox;

    public AttackState(RobotController robot, AttackData attackData) : base(robot)
    {
        this.data = attackData;
    }

    public override void Enter()
    {
        stateTimer = 0f;
        hasActivatedHitbox = false;

        if (!string.IsNullOrEmpty(data.animationTrigger))
        {
            robot.animator.SetTrigger(data.animationTrigger);
        }

        robot.transform.localScale = data.visualScale;

        Debug.Log($"Iniciando ataque: {data.name}");
    }

    public override void Tick()
    {
        stateTimer += Time.deltaTime;

        // FASE 1: ACTIVE FRAMES (Encendemos la caja de daño)
        if (stateTimer >= data.startupTime && stateTimer < (data.startupTime + data.activeTime) && !hasActivatedHitbox)
        {
            robot.hitboxBase.ActivarHitbox(data);
            hasActivatedHitbox = true;
        }
        // FASE 2: RECOVERY Y VENTANA DE CANCELACIÓN
        else if (stateTimer >= (data.startupTime + data.activeTime))
        {
            if (hasActivatedHitbox)
            {
                robot.hitboxBase.DesactivarHitbox();
                hasActivatedHitbox = false;
            }

            // --- ÁRBOL DE COMBOS RAMIFICADO ---
            if (data.comboBranches != null && data.comboBranches.Count > 0)
            {
                foreach (ComboBranch branch in data.comboBranches)
                {
                    if (robot.TryConsumeCommand(branch.inputCommand))
                    {
                        robot.ChangeState(new AttackState(robot, branch.nextAttack));
                        return; 
                    }
                }
            }

            // FASE 3: FIN DEL ATAQUE
            if (stateTimer >= (data.startupTime + data.activeTime + data.recoveryTime))
            {
                robot.ChangeState(new IdleState(robot));
            }
        }
    }

    public override void Exit()
    {
        robot.hitboxBase.DesactivarHitbox();
        robot.transform.localScale = Vector3.one;
    }
}