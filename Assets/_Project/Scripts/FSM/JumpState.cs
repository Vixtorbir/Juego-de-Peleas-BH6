using UnityEngine;

public class JumpState : RobotState
{
    public const float StandardJumpForce = 12f;

    private const float HorizontalAirSpeed = 4f;

    private float takeoffForce;
    private bool takeoff;
    private float verticalVelocity;
    private float horizontalVelocity;

    public JumpState(RobotController robot) : base(robot) { }

    public override bool AcceptsCommands => robot.IsAboveAirActionHeight;

    public override bool CanConsume(FighterCommand command) => AcceptsCommands && (IsAttackButton(command) || IsDashCommand(command));

    public void ConfigureTakeoff(float verticalForce)
    {
        takeoff = true;
        takeoffForce = verticalForce;
    }

    public void ConfigureMidair()
    {
        takeoff = false;
    }

    public override void Enter()
    {
        if (takeoff)
        {
            verticalVelocity = takeoffForce;
            robot.FaceOpponent();

            float forwardAmount = robot.MovementInput.x * robot.GetScreenFacingSign();
            horizontalVelocity = robot.transform.forward.x * (forwardAmount * HorizontalAirSpeed);
        }
        else
        {
            verticalVelocity = robot.AirVelocity.y;
            horizontalVelocity = robot.AirVelocity.x;
        }

        PlayAnimationState(FighterAnimatorParameters.Jump);
        robot.AirVelocity = new Vector2(horizontalVelocity, verticalVelocity);
    }

    public override void Tick()
    {
        if (AcceptsCommands)
        {
            if (TryConsumeAirDashCommands()) return;
            if (TryConsumeAirNormalCommands()) return;
        }

        verticalVelocity += Gravity * Time.deltaTime;

        Vector3 velocity = new Vector3(horizontalVelocity, verticalVelocity, 0f);
        robot.characterController.Move(velocity * Time.deltaTime);
        robot.AirVelocity = new Vector2(horizontalVelocity, verticalVelocity);

        if (HasLanded(verticalVelocity))
        {
            robot.EnterIdle();
        }
    }
}
