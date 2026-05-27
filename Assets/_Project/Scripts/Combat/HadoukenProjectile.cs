using UnityEngine;

public class HadoukenProjectile : MonoBehaviour
{
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

        Destroy(gameObject, 3f);
    }

    private void Update()
    {
        transform.Translate(moveDirection * speed * Time.deltaTime, Space.World);
    }

    private void OnTriggerEnter(Collider other)
    {
        Hurtbox objetivo = other.GetComponent<Hurtbox>();

        if (objetivo != null && owner != null)
        {
            // Regla de oro: No podemos dispararnos a nosotros mismos
            if (objetivo.transform != owner.transform)
            {
                Debug.Log($"<color=orange><b>[PROJECTILE HIT]</b></color> Hadouken impacta a {other.gameObject.name}");

                objetivo.RecibirImpacto(attackData, owner);

                Destroy(gameObject);
            }
        }
    }
}