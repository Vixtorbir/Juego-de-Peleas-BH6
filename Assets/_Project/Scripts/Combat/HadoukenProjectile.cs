using UnityEngine;

/// <summary>
/// Straight-line projectile spawned by <see cref="SpecialAttackState"/>. Carries the attack data
/// of the move that fired it and applies it to the first opposing <see cref="Hurtbox"/> it overlaps.
/// </summary>
[RequireComponent(typeof(Collider))]
public class HadoukenProjectile : MonoBehaviour
{
    private const float Lifetime = 3f;

    private float speed;
    private Vector3 moveDirection;
    private AttackData attackData;
    private RobotController owner;

    public void Initialize(Vector3 direction, float projectileSpeed, AttackData data, RobotController creator)
    {
        moveDirection = direction.normalized;
        speed = projectileSpeed;
        attackData = data;
        owner = creator;

        Destroy(gameObject, Lifetime);
    }

    private void Update()
    {
        transform.Translate(moveDirection * (speed * Time.deltaTime), Space.World);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (owner == null || attackData == null) return;

        Hurtbox target = other.GetComponent<Hurtbox>();
        if (target == null || target.Owner == owner) return;

        target.ReceiveHit(attackData, owner);
        Destroy(gameObject);
    }
}
