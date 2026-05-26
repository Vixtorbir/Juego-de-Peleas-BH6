using UnityEngine;

public class HitstunState : RobotState
{

    private float stunDuration = 0.4f;
    private float timer;

    public HitstunState(RobotController robot) : base(robot) { }

    public override void Enter()
    {
        Debug.Log("¡HITSTUN! Robot paralizado.");
        timer = stunDuration;

        // TODO: robot.animator.SetTrigger("TakeDamage");

        Renderer renderer = robot.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = Color.red;
        }
    }

    public override void Tick()
    {
        timer -= Time.deltaTime;

        if (timer <= 0f)
        {
            robot.ChangeState(new IdleState(robot));
        }
    }

    public override void Exit()
    {
        Renderer renderer = robot.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = Color.white;
        }
    }
}