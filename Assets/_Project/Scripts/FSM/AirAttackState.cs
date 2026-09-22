using UnityEngine;

public class AirAttackState : AttackState
{
    private float verticalVelocity;
    private float horizontalVelocity;
    private bool landed;
    private float landingTimer;
    private int activeAttackLayer = BaseAnimationLayer;

    public AirAttackState(RobotController robot) : base(robot) { }

    public override bool AcceptsCommands => !landed && base.AcceptsCommands;

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

    private void LandCancel()
    {
        verticalVelocity = 0f;
        horizontalVelocity = 0f;
        robot.EnterIdle();
    }

    // Swing plays on a masked layer so the arms/legs come from the attack while the body keeps the jump pose.
    private void HoldAttackLayer()
    {
        activeAttackLayer = Data != null ? Data.animationLayer : BaseAnimationLayer;
        if (robot.animator == null || activeAttackLayer == BaseAnimationLayer) return;

        robot.animator.SetLayerWeight(activeAttackLayer, 1f);
    }

    // Use the layer raised on entry: a chained aerial may already have configured the next attack.
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
