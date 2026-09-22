using UnityEngine;

public enum BounceMode
{
    None,
    WallBounce,
    GroundBounce
}

// Juggle state: ballistic arc with no control until the body lands or bounces.
public class AirborneHurtState : RobotState
{
    private const float DriftDamping = 1.2f;
    private const float BounceDriftDamping = 0.4f;

    private float launchForce;
    private float driftSpeed;
    private float bounceForce;
    private RobotController attacker;
    private BounceMode pendingBounce;
    private float verticalVelocity;
    private float horizontalVelocity;
    private float gravityScale;

    public AirborneHurtState(RobotController robot) : base(robot) { }

    public BounceMode PendingBounce => pendingBounce;

    public void Configure(float launch, float drift, RobotController source, BounceMode bounce, float reboundForce)
    {
        launchForce = launch;
        driftSpeed = drift;
        attacker = source;
        pendingBounce = bounce;
        bounceForce = reboundForce;
    }

    public override void Enter()
    {
        verticalVelocity = launchForce;
        gravityScale = robot.Rules.GravityScale(robot.Hurtbox != null ? robot.Hurtbox.Combo.JuggleHits : 0);

        float awayFromAttacker = attacker != null
            ? Mathf.Sign(robot.transform.position.x - attacker.transform.position.x)
            : -robot.transform.forward.x;
        horizontalVelocity = awayFromAttacker * driftSpeed;

        PlayReactionAnimation(FighterAnimatorParameters.Hitstun, null, 0f);
    }

    public override void Tick()
    {
        verticalVelocity += Gravity * gravityScale * Time.deltaTime;

        float damping = pendingBounce == BounceMode.WallBounce ? BounceDriftDamping : DriftDamping;
        horizontalVelocity = Mathf.MoveTowards(horizontalVelocity, 0f, damping * Time.deltaTime);

        Vector3 velocity = new Vector3(horizontalVelocity, verticalVelocity, 0f);
        robot.characterController.Move(velocity * Time.deltaTime);

        if (pendingBounce == BounceMode.WallBounce && TryWallBounce()) return;

        if (!HasLanded(verticalVelocity)) return;

        if (pendingBounce == BounceMode.GroundBounce)
        {
            GroundBounce();
            return;
        }

        robot.EnterKnockdown(robot.Rules.knockdownDuration, 0f, attacker);
    }

    private bool TryWallBounce()
    {
        if (Mathf.Abs(horizontalVelocity) < 0.01f) return false;

        float direction = Mathf.Sign(horizontalVelocity);
        if (!robot.IsTouchingWall(direction)) return false;

        horizontalVelocity = -horizontalVelocity * robot.Rules.wallBounceRestitution;
        verticalVelocity = Mathf.Max(verticalVelocity, robot.Rules.wallBouncePop);
        pendingBounce = BounceMode.None;

        if (robot.Hurtbox != null) robot.Hurtbox.NotifyBounce(BounceMode.WallBounce, attacker);
        return true;
    }

    private void GroundBounce()
    {
        verticalVelocity = bounceForce;
        pendingBounce = BounceMode.None;

        if (robot.Hurtbox != null) robot.Hurtbox.NotifyBounce(BounceMode.GroundBounce, attacker);
    }
}
