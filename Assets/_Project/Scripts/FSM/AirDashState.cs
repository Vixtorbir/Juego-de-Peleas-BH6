using UnityEngine;

/// <summary>
/// Air dash: a fixed-duration horizontal burst with gravity frozen, so the fighter floats across
/// the arc instead of arcing through it. Granted a limited number of times per jump; the counter
/// lives on the controller and clears on every return to a neutral stance, which includes landing.
/// After a few frames a buffered air normal may interrupt the dash, which is how an instant air
/// dash turns into pressure. When the dash ends, part of its speed is carried into the fall.
/// </summary>
public class AirDashState : RobotState
{
    private bool isForward;
    private float stateTimer;
    private float direction;

    public AirDashState(RobotController robot) : base(robot) { }

    public override bool AcceptsCommands => stateTimer >= robot.Rules.airDashAttackDelay;

    public override bool CanConsume(FighterCommand command) => AcceptsCommands && IsAttackButton(command);

    public void Configure(bool forward)
    {
        isForward = forward;
    }

    public override void Enter()
    {
        stateTimer = 0f;
        direction = (isForward ? 1f : -1f) * robot.transform.forward.x;
        robot.AirDashesUsed++;

        float speed = direction * robot.Rules.airDashSpeed;
        robot.AirVelocity = new Vector2(speed, 0f);

        int stateHash = isForward ? FighterAnimatorParameters.DashForward : FighterAnimatorParameters.DashBackward;
        AnimationClip clip = isForward ? robot.dashForwardClip : robot.dashBackwardClip;
        PlayTimedAnimation(stateHash, clip, robot.Rules.airDashDuration);
    }

    public override void Tick()
    {
        stateTimer += Time.deltaTime;

        if (AcceptsCommands && TryConsumeAirNormalCommands()) return;

        float speed = direction * robot.Rules.airDashSpeed;
        robot.characterController.Move(new Vector3(speed * Time.deltaTime, 0f, 0f));

        if (stateTimer < robot.Rules.airDashDuration) return;

        robot.AirVelocity = new Vector2(speed * robot.Rules.airDashCarryRatio, 0f);
        robot.EnterAirborne();
    }
}
