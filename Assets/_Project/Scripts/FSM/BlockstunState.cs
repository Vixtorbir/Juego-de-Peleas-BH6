using UnityEngine;

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

    // The flag may already be set up for the next entry here, so always stand back up.
    public override void Exit()
    {
        robot.SetCrouchingPosture(false);
    }
}
