using UnityEngine;

public class CrouchAttackState : RobotState
{
    private AttackData data;
    private float stateTimer;
    private bool hasActivatedHitbox;

    private float originalHeight;
    private Vector3 originalCenter;

    public CrouchAttackState(RobotController robot, AttackData attackData) : base(robot)
    {
        this.data = attackData;
    }

    public override void Enter()
    {
        stateTimer = 0f;
        hasActivatedHitbox = false;

        robot.IsInAttackStartup = true;

        originalHeight = robot.characterController.height;
        originalCenter = robot.characterController.center;

        robot.characterController.height = originalHeight / 2f;
        robot.characterController.center = new Vector3(originalCenter.x, originalCenter.y / 2f, originalCenter.z);

        if (!string.IsNullOrEmpty(data.animationTrigger))
            robot.animator.SetTrigger(data.animationTrigger);

        robot.transform.localScale = data.visualScale;
    }

    public override void Tick()
    {
        stateTimer += Time.deltaTime;

        if (stateTimer >= data.startupTime)
        {
            robot.IsInAttackStartup = false;
        }

        if (stateTimer >= data.startupTime && stateTimer < (data.startupTime + data.activeTime) && !hasActivatedHitbox)
        {
            robot.hitboxBase.ActivarHitbox(data);
            hasActivatedHitbox = true;
        }
        else if (stateTimer >= (data.startupTime + data.activeTime))
        {
            if (hasActivatedHitbox)
            {
                robot.hitboxBase.DesactivarHitbox();
                hasActivatedHitbox = false;
            }

            if (stateTimer >= (data.startupTime + data.activeTime + data.recoveryTime))
            {
                if (robot.MovementInput.y < -0.2f)
                    robot.ChangeState(new CrouchState(robot));
                else
                    robot.ChangeState(new IdleState(robot));
            }
        }
    }

    public override void Exit()
    {
        robot.IsInAttackStartup = false;
        robot.hitboxBase.DesactivarHitbox();
        robot.characterController.height = originalHeight;
        robot.characterController.center = originalCenter;
        robot.transform.localScale = Vector3.one;
    }
}