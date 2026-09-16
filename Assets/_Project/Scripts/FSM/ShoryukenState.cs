using UnityEngine;

/// <summary>
/// Rising uppercut special. The hitbox is live from the first frame for the attack's active
/// window while the fighter climbs and drifts towards the opponent; the move ends on landing,
/// leaving a long punishable recovery on whiff.
/// </summary>
public class ShoryukenState : RobotState
{
    private const float RiseVelocity = 14f;
    private const float RiseGravity = -25f;
    private const float ForwardSpeed = 4f;

    private readonly AttackData attackData;
    private float timer;
    private float verticalVelocity;
    private bool hitboxActive;

    public ShoryukenState(RobotController robot, AttackData data) : base(robot)
    {
        attackData = data;
    }

    public override void Enter()
    {
        timer = 0f;
        verticalVelocity = RiseVelocity;
        hitboxActive = false;

        if (robot.hitboxBase != null && attackData != null)
        {
            robot.hitboxBase.Activate(attackData);
            hitboxActive = true;
        }
    }

    public override void Tick()
    {
        timer += Time.deltaTime;
        verticalVelocity += RiseGravity * Time.deltaTime;

        float forwardSign = robot.opponentTarget != null
            ? Mathf.Sign(robot.opponentTarget.position.x - robot.transform.position.x)
            : 0f;

        Vector3 velocity = new Vector3(forwardSign * ForwardSpeed, verticalVelocity, 0f);
        robot.characterController.Move(velocity * Time.deltaTime);

        if (hitboxActive && attackData != null && timer > attackData.activeTime)
        {
            robot.hitboxBase.Deactivate();
            hitboxActive = false;
        }

        if (verticalVelocity < 0f && (robot.IsGrounded || robot.characterController.isGrounded))
        {
            robot.ChangeState(new IdleState(robot));
        }
    }

    public override void Exit()
    {
        if (robot.hitboxBase != null) robot.hitboxBase.Deactivate();
    }
}
