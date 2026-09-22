using UnityEngine;

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
