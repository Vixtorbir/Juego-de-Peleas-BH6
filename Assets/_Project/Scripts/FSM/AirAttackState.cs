using UnityEngine;

/// <summary>
/// Jumping normal. Shares the startup/active/recovery timeline and the on-connect chain rules of
/// <see cref="AttackState"/>, but keeps falling through the swing with the momentum inherited from
/// the jump: gravity integrates every frame and the horizontal speed is carried unchanged. Cancels
/// resolve on the aerial ladder (j.L > j.M > j.H) and into an air dash while one is left this
/// jump; there are no aerial specials or jump cancels. Touching the floor mid-swing cuts the attack:
/// a swing that connected land-cancels straight into the stance, so a jump-in flows into a ground
/// string or a guard, while a swing that missed pays a short landing recovery (trip guard) and
/// stays punishable. A swing that finishes in the air returns to free fall with the current velocity.
/// </summary>
public class AirAttackState : AttackState
{
    private float verticalVelocity;
    private float horizontalVelocity;
    private bool landed;
    private float landingTimer;
    private int activeAttackLayer = BaseAnimationLayer;

    public AirAttackState(RobotController robot) : base(robot) { }

    /// <summary>Landing recovery accepts nothing; the cancel window rule applies while airborne.</summary>
    public override bool AcceptsCommands => !landed && base.AcceptsCommands;

    /// <summary>Air chains and the air-dash cancel; a special pressed here waits for the landing.</summary>
    public override bool CanConsume(FighterCommand command) => AcceptsCommands && (IsAttackButton(command) || IsDashCommand(command));

    public override void Enter()
    {
        verticalVelocity = robot.AirVelocity.y;
        horizontalVelocity = robot.AirVelocity.x;
        landed = false;
        landingTimer = 0f;
        base.Enter();
        HoldAttackLayer();
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
        ReleaseAttackLayer();
        robot.AirVelocity = new Vector2(horizontalVelocity, verticalVelocity);
    }

    protected override bool IntegrateMotion()
    {
        verticalVelocity += Gravity * Time.deltaTime;

        Vector3 velocity = new Vector3(horizontalVelocity, verticalVelocity, 0f);
        robot.characterController.Move(velocity * Time.deltaTime);
        robot.AirVelocity = new Vector2(horizontalVelocity, verticalVelocity);

        if (!HasLanded(verticalVelocity)) return false;

        if (robot.AttackConnected)
        {
            LandCancel();
            return true;
        }

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
        ReleaseAttackLayer();
        ReturnToStance(FighterAnimatorParameters.LocomotionState);
    }

    /// <summary>
    /// A connected air normal lands with no recovery: control returns at once, and the buffered
    /// ground normal fires on the stance's first tick.
    /// </summary>
    private void LandCancel()
    {
        verticalVelocity = 0f;
        horizontalVelocity = 0f;
        robot.EnterIdle();
    }

    /// <summary>
    /// The swing plays on a masked layer over the jump the base layer holds, so the striking limbs
    /// come from the attack clip while the rest of the body keeps its airborne pose. The layer is
    /// silent outside the swing: its weight is raised here and dropped the moment the attack ends,
    /// whether it ran out, was cancelled or was cut short by the floor.
    /// </summary>
    private void HoldAttackLayer()
    {
        activeAttackLayer = Data != null ? Data.animationLayer : BaseAnimationLayer;
        if (robot.animator == null || activeAttackLayer == BaseAnimationLayer) return;

        robot.animator.SetLayerWeight(activeAttackLayer, 1f);
    }

    /// <summary>
    /// Silences the layer the swing was playing on. The index is the one raised on entry rather than
    /// the one the attack data carries now: a chained aerial configures the next attack before the
    /// current one exits, and a punch chained into a kick moves to a different layer.
    /// </summary>
    private void ReleaseAttackLayer()
    {
        if (activeAttackLayer == BaseAnimationLayer) return;

        if (robot.animator != null) robot.animator.SetLayerWeight(activeAttackLayer, 0f);
        activeAttackLayer = BaseAnimationLayer;
    }

    private void TickLandingRecovery()
    {
        landingTimer -= Time.deltaTime;
        ApplyGroundingForce();

        if (landingTimer <= 0f) robot.EnterIdle();
    }
}
