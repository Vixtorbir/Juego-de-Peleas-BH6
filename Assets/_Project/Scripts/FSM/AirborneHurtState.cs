using UnityEngine;

/// <summary>
/// Juggle state entered after a launcher or an air hit. The fighter follows a ballistic arc with
/// no control: the launch sets the vertical velocity and the attack's pushback becomes a slow
/// horizontal drift away from the attacker, so the body hangs in reach for follow-ups instead of
/// flying off. Any hit received while airborne re-enters this state with a fresh launch. Touching
/// the ground leads into a short knockdown recovery before control returns.
/// </summary>
public class AirborneHurtState : RobotState
{
    private const float LandingRecoveryDuration = 0.45f;
    private const float DriftDamping = 1.2f;

    private readonly float driftSpeed;
    private readonly RobotController attacker;
    private float verticalVelocity;
    private float horizontalVelocity;

    public AirborneHurtState(RobotController robot, float launchForce, float drift, RobotController attacker) : base(robot)
    {
        verticalVelocity = launchForce;
        driftSpeed = drift;
        this.attacker = attacker;
    }

    public override void Enter()
    {
        float awayFromAttacker = attacker != null
            ? Mathf.Sign(robot.transform.position.x - attacker.transform.position.x)
            : -robot.transform.forward.x;
        horizontalVelocity = awayFromAttacker * driftSpeed;

        PlayTimedAnimation(FighterAnimatorParameters.Hitstun, null, 0f);
    }

    public override void Tick()
    {
        verticalVelocity += Gravity * Time.deltaTime;
        horizontalVelocity = Mathf.MoveTowards(horizontalVelocity, 0f, DriftDamping * Time.deltaTime);

        Vector3 velocity = new Vector3(horizontalVelocity, verticalVelocity, 0f);
        robot.characterController.Move(velocity * Time.deltaTime);

        if (robot.characterController.isGrounded && verticalVelocity < 0f)
        {
            robot.characterController.Move(Vector3.up * GroundSnapVelocity);
            robot.ChangeState(new HitstunState(robot, LandingRecoveryDuration, 0f, attacker));
        }
    }
}
