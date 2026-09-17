using UnityEngine;

/// <summary>
/// Reaction to a clean hit. The fighter is locked out of input for the attack's hitstun
/// duration while knockback (with wall bounce) is integrated by <see cref="PushbackSolver"/>.
/// </summary>
public class HitstunState : RobotState
{
    private const float WallTransferRatio = 0.4f;
    private const float WallDampening = 0.5f;

    private readonly float duration;
    private float timer;
    private PushbackSolver pushback;

    public HitstunState(RobotController robot, float hitstunDuration, float pushbackForce, RobotController attacker) : base(robot)
    {
        duration = hitstunDuration;
        timer = hitstunDuration;
        pushback = new PushbackSolver(pushbackForce, attacker, WallTransferRatio, WallDampening);
    }

    public override void Enter()
    {
        PlayTimedAnimation(FighterAnimatorParameters.Hitstun, robot.hitReactionClip, duration);
    }

    public override void Tick()
    {
        timer -= Time.deltaTime;
        pushback.Tick(robot);

        if (timer <= 0f)
        {
            robot.ChangeState(new IdleState(robot));
        }
    }
}
