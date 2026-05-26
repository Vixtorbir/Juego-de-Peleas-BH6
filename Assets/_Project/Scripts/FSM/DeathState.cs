using UnityEngine;

public class DeathState : RobotState
{
    public DeathState(RobotController robot) : base(robot) { }

    public override void Enter()
    {
        Debug.Log($"<color=black><b>[K.O.]</b></color> {robot.gameObject.name} ha caído.");

        if (robot.hitboxBase != null) robot.hitboxBase.DesactivarHitbox();

        if (robot.animator != null) robot.animator.SetTrigger("Death");

        Renderer renderer = robot.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = new Color(0.2f, 0.2f, 0.2f);
    }

    public override void Tick()
    {

        if (!robot.IsGrounded)
        {
            robot.characterController.Move(new Vector3(0, -9.81f, 0) * Time.deltaTime);
        }
    }

    public override void Exit()
    {
        Renderer renderer = robot.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = Color.white;
    }
}