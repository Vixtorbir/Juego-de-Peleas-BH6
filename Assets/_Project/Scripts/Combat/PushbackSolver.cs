using UnityEngine;

/// <summary>
/// Shared knockback integration for hit and block reactions. Pushes the defender away from
/// the attacker with combined dry friction and velocity-squared drag, so a strong hit produces a
/// short violent shove that settles quickly instead of a long slide. When the defender is pinned
/// against a wall, part of the remaining momentum transfers back to the attacker so corner
/// pressure still creates spacing.
/// </summary>
public struct PushbackSolver
{
    private const float Friction = 18f;
    private const float DragCoefficient = 0.5f;
    private const float WallContactTolerance = 0.001f;

    private readonly RobotController attacker;
    private readonly float wallTransferRatio;
    private readonly float wallDampening;

    private float speed;
    private bool hasHitWall;

    public bool IsActive => speed > 0f;

    public PushbackSolver(float initialSpeed, RobotController attacker, float wallTransferRatio, float wallDampening)
    {
        this.attacker = attacker;
        this.wallTransferRatio = wallTransferRatio;
        this.wallDampening = wallDampening;
        speed = initialSpeed;
        hasHitWall = false;
    }

    public void Tick(RobotController defender)
    {
        if (speed <= 0f || defender.opponentTarget == null) return;

        float deceleration = Friction + speed * speed * DragCoefficient;
        speed = Mathf.MoveTowards(speed, 0f, deceleration * Time.deltaTime);

        float awayFromAttacker = Mathf.Sign(defender.transform.position.x - defender.opponentTarget.position.x);
        Vector3 direction = new Vector3(hasHitWall ? -awayFromAttacker : awayFromAttacker, 0f, 0f);
        float desiredDisplacement = speed * Time.deltaTime;

        float previousX = defender.transform.position.x;
        defender.characterController.Move(direction * desiredDisplacement);
        float actualDisplacement = Mathf.Abs(defender.transform.position.x - previousX);

        bool blockedByWall = actualDisplacement < desiredDisplacement - WallContactTolerance;
        if (hasHitWall || !blockedByWall || attacker == null) return;

        hasHitWall = true;
        attacker.characterController.Move(-direction * (speed * wallTransferRatio * Time.deltaTime));
        speed *= wallDampening;
    }
}
