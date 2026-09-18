using UnityEngine;

/// <summary>
/// Shared knockback integration for hit and block reactions. Pushes the defender away from the
/// attacker with combined dry friction and velocity-squared drag, so a strong hit produces a short
/// violent shove that settles quickly instead of a long slide. The push direction is locked on the
/// frame the reaction starts, matching how fighting games resolve knockback: a defender never
/// reverses mid-reaction even if the two bodies cross during the stun.
/// <para>
/// Corner rule: once the defender is stopped by a wall it stays pinned there, and the remaining
/// momentum is handed to the attacker for the rest of the reaction, so cornering an opponent still
/// costs the attacker their spacing. The transfer only applies to attackers within melee reach; a
/// projectile fired from full screen never shoves its owner.
/// </para>
/// </summary>
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

    /// <summary>True once the defender has been stopped by a wall for the rest of this reaction.</summary>
    public bool IsCornered => cornered;

    /// <param name="wallTransferRatio">
    /// Fraction of the momentum still in flight that carries over to the attacker when the defender
    /// reaches a wall. 1 preserves the full spacing of an open-field hit.
    /// </param>
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

    /// <summary>
    /// Knockback runs along the combat axis away from the attacker. With no attacker (environmental
    /// or scripted hits) the defender is pushed backwards relative to its own facing.
    /// </summary>
    private static float ResolveAwayDirection(RobotController defender, RobotController attacker)
    {
        float deltaX = attacker != null
            ? defender.transform.position.x - attacker.transform.position.x
            : -defender.transform.forward.x;

        return deltaX < 0f ? -1f : 1f;
    }
}
