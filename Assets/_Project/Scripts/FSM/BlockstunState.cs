using UnityEngine;

/// <summary>
/// Reaction to a blocked attack. Shorter than hitstun and with weaker knockback, it preserves
/// the crouching posture if the block was taken low so the guard is not dropped on exit.
/// </summary>
public class BlockstunState : RobotState
{
    private const float WallTransferRatio = 0.3f;
    private const float WallDampening = 0.35f;
    private const float StayCrouchedInputThreshold = -0.2f;

    private readonly bool blockedCrouching;
    private readonly float duration;
    private float timer;
    private PushbackSolver pushback;

    public BlockstunState(RobotController robot, float duration, bool isCrouching, float pushbackForce, RobotController attacker) : base(robot)
    {
        this.duration = duration;
        timer = duration;
        blockedCrouching = isCrouching;
        pushback = new PushbackSolver(pushbackForce, attacker, WallTransferRatio, WallDampening);
    }

    public override void Enter()
    {
        PlayTimedAnimation(FighterAnimatorParameters.Blockstun, robot.blockReactionClip, duration);

        if (blockedCrouching)
        {
            robot.SetCrouchingPosture(true);
        }
    }

    public override void Tick()
    {
        timer -= Time.deltaTime;
        pushback.Tick(robot);

        if (timer > 0f) return;

        if (robot.MovementInput.y < StayCrouchedInputThreshold)
            robot.ChangeState(new CrouchState(robot));
        else
            robot.ChangeState(new IdleState(robot));
    }

    public override void Exit()
    {
        if (blockedCrouching)
        {
            robot.SetCrouchingPosture(false);
        }
    }
}
