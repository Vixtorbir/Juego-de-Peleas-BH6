using UnityEngine;

/// <summary>
/// Hard knockdown after a juggle lands or a sweep connects. The body slides out with the hit's
/// remaining pushback and cannot be struck while down, so a landing never restarts the juggle it
/// came from; control returns through Idle with a short strike invulnerability so the wake-up is
/// never a free hit either.
/// </summary>
public class KnockdownState : RobotState
{
    private const float WallTransferRatio = 1f;

    private float duration;
    private float pushbackForce;
    private RobotController attacker;
    private float timer;
    private PushbackSolver pushback;

    public KnockdownState(RobotController robot) : base(robot) { }

    public override bool IgnoresGroundedHits => true;

    public void Configure(float knockdownDuration, float initialPushback, RobotController source)
    {
        duration = knockdownDuration;
        pushbackForce = initialPushback;
        attacker = source;
    }

    public override void Enter()
    {
        timer = duration;
        pushback = new PushbackSolver(robot, attacker, pushbackForce, WallTransferRatio);
        PlayReactionAnimation(FighterAnimatorParameters.Hitstun, robot.hitReactionClip, duration);
    }

    public override void Tick()
    {
        timer -= Time.deltaTime;
        pushback.Tick();
        ApplyGroundingForce();

        if (timer > 0f) return;

        robot.GrantInvulnerability(robot.Rules.wakeUpInvulnerability);
        robot.EnterIdle();
    }
}
