using UnityEngine;

public class HitstunState : RobotState
{
    private const float WallTransferRatio = 1f;

    private float duration;
    private float pushbackForce;
    private RobotController attacker;
    private float timer;
    private PushbackSolver pushback;

    public HitstunState(RobotController robot) : base(robot) { }

    public void Configure(float hitstunDuration, float initialPushback, RobotController source)
    {
        duration = hitstunDuration;
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

        if (timer <= 0f)
        {
            robot.EnterIdle();
        }
    }
}
