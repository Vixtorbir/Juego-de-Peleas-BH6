using UnityEngine;

/// <summary>
/// Pending rebound of a juggled body. Resolved by <see cref="AirborneHurtState"/> when the body
/// meets the matching surface, then cleared so a single hit never bounces twice.
/// </summary>
public enum BounceMode
{
    None,
    WallBounce,
    GroundBounce
}

/// <summary>
/// Juggle state entered after a launcher or an air hit. The fighter follows a ballistic arc with
/// no control: the launch sets the vertical velocity and the attack's pushback becomes a slow
/// horizontal drift away from the attacker, so the body hangs in reach for follow-ups instead of
/// flying off. Gravity grows with every air hit of the combo, so each relaunch is shorter than the
/// last and no route stays airborne forever. A pending wall bounce reflects the drift when the
/// capsule reaches the stage boundary; a pending ground bounce pops the body back up on landing.
/// Any hit received while airborne re-enters this state with a fresh launch, and touching the
/// ground without a bounce leads into a hard knockdown.
/// </summary>
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

    /// <summary>Rebound still to be resolved; None once it has happened or was never granted.</summary>
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

    /// <summary>
    /// Reflects the horizontal motion off the stage boundary once the capsule touches it, keeping
    /// the body above a minimum pop so it hangs in the attacker's reach on the way back.
    /// </summary>
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
