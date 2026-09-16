using UnityEngine;

/// <summary>
/// Base class for every fighter state. States are self-contained behaviours that read from
/// the owning <see cref="RobotController"/> and request transitions through
/// <see cref="RobotController.ChangeState"/>.
/// </summary>
public abstract class RobotState
{
    protected const float Gravity = -35f;
    protected const float GroundSnapVelocity = -0.5f;
    protected const float FallVelocity = -9.81f;

    protected readonly RobotController robot;

    protected RobotState(RobotController robot)
    {
        this.robot = robot;
    }

    public virtual void Enter() { }

    public virtual void Tick() { }

    public virtual void Exit() { }

    /// <summary>
    /// Keeps a grounded fighter pressed against the floor so CharacterController.isGrounded
    /// stays reliable, and lets it fall if the ground disappears underneath.
    /// </summary>
    protected void ApplyGroundingForce()
    {
        float verticalVelocity = robot.characterController.isGrounded ? GroundSnapVelocity : FallVelocity;
        robot.characterController.Move(new Vector3(0f, verticalVelocity * Time.deltaTime, 0f));
    }

    /// <summary>
    /// Special moves take priority over normals in every neutral state. Returns true if a transition occurred.
    /// </summary>
    protected bool TryConsumeSpecialCommands()
    {
        if (robot.TryConsumeCommand(FighterCommand.Hadouken))
        {
            robot.ChangeState(new SpecialAttackState(robot, robot.hadoukenData, robot.hadoukenPrefab, robot.hadoukenSpawnPoint));
            return true;
        }

        if (robot.TryConsumeCommand(FighterCommand.Shoryuken))
        {
            robot.ChangeState(new ShoryukenState(robot, robot.shoryukenData));
            return true;
        }

        return false;
    }

    protected bool TryConsumeDashCommands()
    {
        if (robot.TryConsumeCommand(FighterCommand.DashForward))
        {
            robot.ChangeState(new DashState(robot, true));
            return true;
        }

        if (robot.TryConsumeCommand(FighterCommand.DashBackward))
        {
            robot.ChangeState(new DashState(robot, false));
            return true;
        }

        return false;
    }
}
