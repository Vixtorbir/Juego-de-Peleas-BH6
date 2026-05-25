using UnityEngine;

public class WalkState : RobotState
{
    private float smoothTime = 0.1f;
    private Vector2 currentVelocity;
    private Vector2 smoothInput;

    public WalkState(RobotController robot) : base(robot) { }

    public override void Tick()
    {
        if (robot.MovementInput.sqrMagnitude <= 0.01f)
        {
            robot.ChangeState(new IdleState(robot));
            return; 
        }

        smoothInput = Vector2.SmoothDamp(smoothInput, robot.MovementInput, ref currentVelocity, smoothTime);

        robot.animator.SetFloat("InputX", smoothInput.x);
        robot.animator.SetFloat("InputY", smoothInput.y);

        // --- MOVIMIENTO PROVISIONAL (Hasta que tengamos Root Motion) ---
        Vector3 moveDirection = new Vector3(smoothInput.x, 0f, smoothInput.y);
        float tempSpeed = 3f;
        robot.characterController.Move(moveDirection * tempSpeed * Time.deltaTime);
    }
}