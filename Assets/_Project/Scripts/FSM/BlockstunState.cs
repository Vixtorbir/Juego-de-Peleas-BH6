using UnityEngine;

/// <summary>
/// Reaction to a blocked attack. Shorter than hitstun and with weaker knockback, it preserves
/// the crouching posture if the block was taken low so the guard is not dropped on exit.
/// A cornered block hands slightly less momentum back to the attacker than a cornered hit,
/// which keeps corner pressure and frame traps viable. The guard stays up throughout, so a
/// string of blocked hits keeps being blocked at the same height.
/// </summary>
public class BlockstunState : RobotState
{
    private const float WallTransferRatio = 0.8f;
    private const float StayCrouchedInputThreshold = -0.2f;

    private bool blockedCrouching;
    private float duration;
    private float pushbackForce;
    private RobotController attacker;
    private float timer;
    private PushbackSolver pushback;

    public BlockstunState(RobotController robot) : base(robot) { }

    public override bool CanGuard => true;

    public override bool IsCrouchingPosture => blockedCrouching;

    public void Configure(float blockstunDuration, bool isCrouching, float initialPushback, RobotController source)
    {
        duration = blockstunDuration;
        blockedCrouching = isCrouching;
        pushbackForce = initialPushback;
        attacker = source;
    }

    public override void Enter()
    {
        timer = duration;
        pushback = new PushbackSolver(robot, attacker, pushbackForce, WallTransferRatio);
        PlayReactionAnimation(FighterAnimatorParameters.Blockstun, robot.blockReactionClip, duration);

        if (blockedCrouching)
        {
            robot.SetCrouchingPosture(true);
        }
    }

    public override void Tick()
    {
        timer -= Time.deltaTime;
        pushback.Tick();
        ApplyGroundingForce();

        if (timer > 0f) return;

        if (robot.MovementInput.y < StayCrouchedInputThreshold)
            robot.EnterCrouch();
        else
            robot.EnterIdle();
    }

    /// <summary>
    /// Always restores the standing capsule: the state is configured for its next entry before
    /// this runs, so the flag can no longer be trusted to say how the previous block was taken.
    /// </summary>
    public override void Exit()
    {
        robot.SetCrouchingPosture(false);
    }
}
