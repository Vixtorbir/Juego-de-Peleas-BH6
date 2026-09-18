using UnityEngine;

/// <summary>
/// Straight-line projectile fired by <see cref="SpecialAttackState"/> from the owner's
/// <see cref="ProjectilePool"/>. Carries the attack data of the move that launched it and applies
/// it to the first opposing <see cref="Hurtbox"/> it overlaps. A projectile that is dodged
/// (invincible frames, height mismatch) keeps flying until its lifetime runs out; either way it
/// returns to the pool by deactivating instead of being destroyed.
/// </summary>
[RequireComponent(typeof(Collider))]
public class HadoukenProjectile : MonoBehaviour
{
    private const float Lifetime = 3f;

    private float speed;
    private float remainingLifetime;
    private Vector3 moveDirection;
    private AttackData attackData;
    private RobotController owner;

    public bool IsInFlight => remainingLifetime > 0f;

    public void Launch(Vector3 origin, Vector3 direction, float projectileSpeed, AttackData data, RobotController creator)
    {
        transform.SetPositionAndRotation(origin, Quaternion.identity);
        moveDirection = direction.normalized;
        speed = projectileSpeed;
        attackData = data;
        owner = creator;
        remainingLifetime = Lifetime;

        gameObject.SetActive(true);
    }

    /// <summary>
    /// Removes the projectile from play without applying its hit. Safe to call on an idle instance.
    /// </summary>
    public void Recall()
    {
        remainingLifetime = 0f;
        attackData = null;
        owner = null;
        gameObject.SetActive(false);
    }

    private void Update()
    {
        remainingLifetime -= Time.deltaTime;
        if (remainingLifetime <= 0f)
        {
            Recall();
            return;
        }

        transform.Translate(moveDirection * (speed * Time.deltaTime), Space.World);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsInFlight || owner == null || attackData == null) return;

        if (!other.TryGetComponent(out Hurtbox target) || target.Owner == owner) return;

        if (!target.ReceiveHit(attackData, owner, other.bounds.ClosestPoint(transform.position))) return;

        owner.NotifyAttackConnected();
        Recall();
    }
}
