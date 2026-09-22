using UnityEngine;

public class WalkState : RobotState
{
    private const float WalkSpeed = 3f;
    private const float SmoothTime = 0.1f;
    private const float VerticalInputThreshold = 0.6f;
    private const float StopInputThreshold = 0.01f;

    private Vector2 smoothInput;
    private Vector2 smoothVelocity;

    public WalkState(RobotController robot) : base(robot) { }

    public override bool CanGuard => true;

    public override bool IsNeutral => true;

    public override void Enter()
    {
        smoothInput = Vector2.zero;
        smoothVelocity = Vector2.zero;
    }

    public override void Tick()
    {
        if (TryConsumeSpecialCommands()) return;
        if (TryConsumeDashCommands()) return;
        if (TryConsumeNormalCommands(false)) return;

        if (robot.MovementInput.y > VerticalInputThreshold)
        {
            robot.EnterJump();
            return;
        }

        if (robot.MovementInput.y < -VerticalInputThreshold)
        {
            robot.EnterCrouch();
            return;
        }

        if (Mathf.Abs(robot.MovementInput.x) <= StopInputThreshold)
        {
            robot.EnterIdle();
            return;
        }

        Move();
    }

    private void Move()
    {
        smoothInput = Vector2.SmoothDamp(smoothInput, robot.MovementInput, ref smoothVelocity, SmoothTime);

        robot.FaceOpponent();

        float forwardAmount = smoothInput.x * robot.GetScreenFacingSign();

        robot.animator.SetFloat(FighterAnimatorParameters.InputY, forwardAmount);
        robot.animator.SetFloat(FighterAnimatorParameters.InputX, 0f);

        Vector3 velocity = robot.transform.forward * (forwardAmount * WalkSpeed);
        robot.characterController.Move(velocity * Time.deltaTime);

        ApplyGroundingForce();
    }
}
