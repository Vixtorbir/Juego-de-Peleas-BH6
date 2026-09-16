using UnityEngine;

/// <summary>
/// Grounded horizontal locomotion. Movement is interpreted relative to the camera so pressing
/// towards the opponent always advances, and forward + RightPunch is routed to the launcher.
/// </summary>
public class WalkState : RobotState
{
    private const float WalkSpeed = 3f;
    private const float SmoothTime = 0.1f;
    private const float VerticalInputThreshold = 0.6f;
    private const float StopInputThreshold = 0.01f;
    private const float LauncherForwardThreshold = 0.5f;

    private Vector2 smoothInput;
    private Vector2 smoothVelocity;

    public WalkState(RobotController robot) : base(robot) { }

    public override void Enter()
    {
        if (robot.Hurtbox != null) robot.Hurtbox.ResetCombo();
    }

    public override void Tick()
    {
        if (TryConsumeSpecialCommands()) return;
        if (TryConsumeDashCommands()) return;
        if (TryConsumeNormalCommands()) return;

        if (robot.MovementInput.y > VerticalInputThreshold)
        {
            robot.ChangeState(new JumpState(robot));
            return;
        }

        if (robot.MovementInput.y < -VerticalInputThreshold)
        {
            robot.ChangeState(new CrouchState(robot));
            return;
        }

        if (Mathf.Abs(robot.MovementInput.x) <= StopInputThreshold)
        {
            robot.ChangeState(new IdleState(robot));
            return;
        }

        Move();
    }

    private void Move()
    {
        smoothInput = Vector2.SmoothDamp(smoothInput, robot.MovementInput, ref smoothVelocity, SmoothTime);

        robot.FaceOpponent();

        float forwardAmount = smoothInput.x * robot.GetScreenFacingSign();

        robot.animator.SetFloat("InputY", forwardAmount);
        robot.animator.SetFloat("InputX", 0f);

        Vector3 velocity = robot.transform.forward * (forwardAmount * WalkSpeed);
        robot.characterController.Move(velocity * Time.deltaTime);

        ApplyGroundingForce();
    }

    private bool TryConsumeNormalCommands()
    {
        if (robot.TryConsumeCommand(FighterCommand.LeftPunch))
        {
            robot.ChangeState(new AttackState(robot, robot.leftPunchBase));
            return true;
        }

        if (robot.TryConsumeCommand(FighterCommand.RightPunch))
        {
            bool advancing = robot.MovementInput.x * robot.GetScreenFacingSign() > LauncherForwardThreshold;
            robot.ChangeState(new AttackState(robot, advancing ? robot.launcherBase : robot.rightPunchBase));
            return true;
        }

        if (robot.TryConsumeCommand(FighterCommand.LeftKick))
        {
            robot.ChangeState(new AttackState(robot, robot.leftKickBase));
            return true;
        }

        if (robot.TryConsumeCommand(FighterCommand.RightKick))
        {
            robot.ChangeState(new AttackState(robot, robot.rightKickBase));
            return true;
        }

        return false;
    }
}
