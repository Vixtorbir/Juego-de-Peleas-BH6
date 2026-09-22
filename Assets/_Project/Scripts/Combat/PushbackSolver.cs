using UnityEngine;

// Knockback slide for hit/blockstun. Momentum left over when the defender hits a wall goes to the attacker.
public struct PushbackSolver
{
    private const float Friction = 18f;
    private const float DragCoefficient = 0.5f;
    private const float WallContactTolerance = 0.001f;
    private const float AttackerTransferRange = 2.5f;

    private readonly RobotController defender;
    private readonly RobotController attacker;
    private readonly float wallTransferRatio;
    private readonly float direction;

    private float speed;
    private bool cornered;

    public bool IsActive => speed > 0f;

    public bool IsCornered => cornered;

    public PushbackSolver(RobotController defender, RobotController attacker, float initialSpeed, float wallTransferRatio)
    {
        this.defender = defender;
        this.attacker = attacker;
        this.wallTransferRatio = wallTransferRatio;
        direction = ResolveAwayDirection(defender, attacker);
        speed = initialSpeed;
        cornered = false;
    }

    public void Tick()
    {
        if (speed <= 0f) return;

        float deceleration = Friction + speed * speed * DragCoefficient;
        speed = Mathf.MoveTowards(speed, 0f, deceleration * Time.deltaTime);
        float displacement = speed * Time.deltaTime;

        if (cornered)
        {
            PushAttacker(displacement);
            return;
        }

        float previousX = defender.transform.position.x;
        defender.characterController.Move(new Vector3(direction * displacement, 0f, 0f));
        float travelled = Mathf.Abs(defender.transform.position.x - previousX);

        if (travelled >= displacement - WallContactTolerance) return;

        cornered = true;
        if (!CanTransferToAttacker())
        {
            speed = 0f;
            return;
        }

        speed *= wallTransferRatio;
        PushAttacker(displacement - travelled);
    }

    private void PushAttacker(float displacement)
    {
        attacker.characterController.Move(new Vector3(-direction * displacement, 0f, 0f));
    }

    private bool CanTransferToAttacker()
    {
        if (attacker == null) return false;
        return Mathf.Abs(attacker.transform.position.x - defender.transform.position.x) <= AttackerTransferRange;
    }

    // No attacker (scripted hit): push back relative to the defender's own facing.
    private static float ResolveAwayDirection(RobotController defender, RobotController attacker)
    {
        float deltaX = attacker != null
            ? defender.transform.position.x - attacker.transform.position.x
            : -defender.transform.forward.x;

        return deltaX < 0f ? -1f : 1f;
    }
}
