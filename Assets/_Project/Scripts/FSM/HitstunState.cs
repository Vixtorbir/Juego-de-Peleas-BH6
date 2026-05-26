using UnityEngine;

public class HitstunState : RobotState
{
    private float timer;
    private float currentPushbackSpeed;
    private float friction = 25f;
    private bool isCounterHit;

    public HitstunState(RobotController robot, float hitstunDuration, float pushbackForce, bool isCounterHit) : base(robot)
    {
        this.timer = hitstunDuration;
        this.currentPushbackSpeed = pushbackForce;
        this.isCounterHit = isCounterHit;
    }

    public override void Enter()
    {
        Renderer renderer = robot.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = isCounterHit ? Color.yellow : Color.red;
        }

        Debug.Log($"<color=orange>¡HITSTUN!</color> Counter: {isCounterHit}. Pushback: {currentPushbackSpeed}");
    }

    public override void Tick()
    {
        timer -= Time.deltaTime;

        if (currentPushbackSpeed > 0f)
        {
            currentPushbackSpeed = Mathf.MoveTowards(currentPushbackSpeed, 0f, friction * Time.deltaTime);

            float direccionX = Mathf.Sign(robot.transform.position.x - robot.opponentTarget.position.x);
            Vector3 direccionPushback = new Vector3(direccionX, 0f, 0f);

            robot.characterController.Move(direccionPushback * currentPushbackSpeed * Time.deltaTime);
        }

        if (timer <= 0f)
        {
            robot.ChangeState(new IdleState(robot));
        }
    }

    public override void Exit()
    {
        Renderer renderer = robot.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = Color.white;
    }
}