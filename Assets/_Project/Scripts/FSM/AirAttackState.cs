using UnityEngine;

/// <summary>
/// Jumping normal. Shares the startup/active/recovery timeline and the on-connect chain rules of
/// <see cref="AttackState"/>, but keeps falling through the swing with the momentum inherited from
/// the jump: gravity integrates every frame and the horizontal speed is carried unchanged. Cancels
/// resolve on the aerial ladder (j.L > j.M > j.H) and into an air dash while one is left this
/// jump; there are no aerial specials or jump cancels. Touching the floor mid-swing cuts the attack
/// into a short landing recovery, so a jump-in that lands early is not stuck in its recovery frames.
/// A swing that finishes in the air returns to free fall with the current velocity.
/// </summary>
public class AirAttackState : AttackState
{
    private float verticalVelocity;
    private float horizontalVelocity;
    private bool landed;
    private float landingTimer;

    public AirAttackState(RobotController robot) : base(robot) { }

    /// <summary>Landing recovery accepts nothing; the cancel window rule applies while airborne.</summary>
    public override bool AcceptsCommands => !landed && base.AcceptsCommands;

    public override void Enter()
    {
        verticalVelocity = robot.AirVelocity.y;
        horizontalVelocity = robot.AirVelocity.x;
        landed = false;
        landingTimer = 0f;
        base.Enter();
    }

    public override void Tick()
    {
        if (landed)
        {
            TickLandingRecovery();
            return;
        }

        base.Tick();
    }

    public override void Exit()
    {
        base.Exit();
        robot.AirVelocity = new Vector2(horizontalVelocity, verticalVelocity);
    }

    protected override bool IntegrateMotion()
    {
        verticalVelocity += Gravity * Time.deltaTime;

        Vector3 velocity = new Vector3(horizontalVelocity, verticalVelocity, 0f);
        robot.characterController.Move(velocity * Time.deltaTime);
        robot.AirVelocity = new Vector2(horizontalVelocity, verticalVelocity);

        if (!HasLanded(verticalVelocity)) return false;

        BeginLandingRecovery();
        return true;
    }

    protected override bool TryCancels()
    {
        if (TryChainCancel(Data, true)) return true;
        return TryConsumeAirDashCommands();
    }

    protected override void ExitToStance()
    {
        robot.EnterAirborne();
    }

    /// <summary>
    /// Landing ends the swing where it is: the hitbox closes without a whiff sound (the attack was
    /// cut, not missed), the animator settles into the stance and a few frames of recovery pass
    /// before control returns.
    /// </summary>
    private void BeginLandingRecovery()
    {
        landed = true;
        landingTimer = robot.Rules.airLandingRecovery;
        verticalVelocity = 0f;
        horizontalVelocity = 0f;
        robot.IsInAttackStartup = false;
        DeactivateHitbox(false);
        ReturnToStance(FighterAnimatorParameters.LocomotionState);
    }

    private void TickLandingRecovery()
    {
        landingTimer -= Time.deltaTime;
        ApplyGroundingForce();

        if (landingTimer <= 0f) robot.EnterIdle();
    }
}
