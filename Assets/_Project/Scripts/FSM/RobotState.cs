public abstract class RobotState
{
    protected RobotController robot;

    // Constructor
    public RobotState(RobotController robot)
    {
        this.robot = robot;
    }

    public virtual void Enter() { }

    public virtual void Tick() { }

    public virtual void Exit() { }
}