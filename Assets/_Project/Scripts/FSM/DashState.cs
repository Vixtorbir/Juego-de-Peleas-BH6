using UnityEngine;

/// <summary>
/// Fixed-duration burst of movement towards or away from the opponent, triggered by a double tap
/// on the horizontal axis or the two-kick macro. The burst stays glued to the floor so a dash never
/// lifts the capsule off its ground contact. After a few frames a buffered normal or special may
/// interrupt it; a normal started from a forward dash inherits a share of the dash speed.
/// </summary>
public class DashState : RobotState
{
    private const float DashDuration = 0.18f;
    private const float DashSpeed = 14f;

    private bool isForward;
    private float stateTimer;
    private Vector3 dashDirection;

    public DashState(RobotController robot) : base(robot) { }

    public override bool AcceptsCommands => DashDuration - stateTimer >= robot.Rules.dashAttackDelay;

    public override bool CanConsume(FighterCommand command) => AcceptsCommands && (IsAttackButton(command) || IsSpecialCommand(command));

    public void Configure(bool forward)
    {
        isForward = forward;
    }

    public override void Enter()
    {
        stateTimer = DashDuration;

        robot.FaceOpponent();
        dashDirection = isForward ? robot.transform.forward : -robot.transform.forward;

        if (isForward)
            PlayTimedAnimation(FighterAnimatorParameters.DashForward, robot.dashForwardClip, DashDuration);
        else
            PlayTimedAnimation(FighterAnimatorParameters.DashBackward, robot.dashBackwardClip, DashDuration);
    }

    public override void Tick()
    {
        stateTimer -= Time.deltaTime;

        if (AcceptsCommands && TryConsumeDashAttack()) return;

        Vector3 velocity = dashDirection * DashSpeed;
        velocity.y = GroundingVelocity;
        robot.characterController.Move(velocity * Time.deltaTime);

        if (stateTimer <= 0f)
        {
            robot.EnterIdle();
        }
    }

    private bool TryConsumeDashAttack()
    {
        if (TryConsumeSpecialCommands()) return true;

        robot.PendingAttackMomentum = isForward ? DashSpeed * robot.Rules.dashAttackCarryRatio : 0f;
        if (TryConsumeNormalCommands(false)) return true;

        robot.PendingAttackMomentum = 0f;
        return false;
    }
}
