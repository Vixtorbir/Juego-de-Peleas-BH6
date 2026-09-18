using UnityEngine;

/// <summary>
/// Reaction to a clean hit. The fighter is locked out of input for the attack's hitstun
/// duration while knockback (with corner transfer to the attacker) is integrated by
/// <see cref="PushbackSolver"/>.
/// </summary>
public class HitstunState : RobotState
{
    private const float WallTransferRatio = 1f;

    private readonly float duration;
    private float timer;
    private PushbackSolver pushback;

    public HitstunState(RobotController robot, float hitstunDuration, float pushbackForce, RobotController attacker) : base(robot)
    {
        duration = hitstunDuration;
        timer = hitstunDuration;
        pushback = new PushbackSolver(robot, attacker, pushbackForce, WallTransferRatio);
    }

    public override void Enter()
    {
        PlayTimedAnimation(FighterAnimatorParameters.Hitstun, robot.hitReactionClip, duration);
    }

    public override void Tick()
    {
        timer -= Time.deltaTime;
        pushback.Tick();
        ApplyGroundingForce();

        if (timer <= 0f)
        {
            robot.ChangeState(new IdleState(robot));
        }
    }
}
