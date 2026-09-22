using UnityEngine;

/// <summary>
/// Straight-line projectile fired by <see cref="SpecialAttackState"/> from the owner's
/// <see cref="ProjectilePool"/>. Carries the attack data of the move that launched it and applies
/// it to the first opposing <see cref="Hurtbox"/> it overlaps. A projectile that is dodged
/// (invincible frames, height mismatch) keeps flying until it reaches a boundary wall or its
/// lifetime runs out; either way it returns to the pool by deactivating instead of being destroyed.
/// The collision matrix limits its contacts to fighter bodies and walls, and the kinematic body on
/// the prefab makes it a true moving trigger: it advances with <see cref="Rigidbody.MovePosition"/>
/// on the physics step, so PhysX sweeps the shape between poses and the renderer interpolates it,
/// instead of teleporting a collider through its transform every frame.
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class HadoukenProjectile : MonoBehaviour
{
    private const float Lifetime = 3f;

    private Rigidbody body;
    private float speed;
    private float remainingLifetime;
    private Vector3 moveDirection;
    private AttackData attackData;
    private RobotController owner;

    public bool IsInFlight => remainingLifetime > 0f;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
    }

    public void Launch(Vector3 origin, Vector3 direction, float projectileSpeed, AttackData data, RobotController creator)
    {
        transform.SetPositionAndRotation(origin, Quaternion.identity);
        moveDirection = direction.normalized;
        speed = projectileSpeed;
        attackData = data;
        owner = creator;
        remainingLifetime = Lifetime;

        gameObject.SetActive(true);
        body.position = origin;
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

    private void FixedUpdate()
    {
        remainingLifetime -= Time.fixedDeltaTime;
        if (remainingLifetime <= 0f)
        {
            Recall();
            return;
        }

        body.MovePosition(body.position + moveDirection * (speed * Time.fixedDeltaTime));
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsInFlight || owner == null || attackData == null) return;

        if (!other.TryGetComponent(out Hurtbox target))
        {
            Recall();
            return;
        }

        if (target.Owner == owner) return;

        Vector3 contactPoint = other.bounds.ClosestPoint(transform.position);
        if (target.TryReceiveHit(attackData, owner, contactPoint, false, out HitRejection rejection))
        {
            owner.NotifyAttackConnected();
            Recall();
            return;
        }

        if (rejection != HitRejection.Invulnerable && rejection != HitRejection.Down) return;

        target.NotifyDeflected(contactPoint, owner);
        Recall();
    }
}
