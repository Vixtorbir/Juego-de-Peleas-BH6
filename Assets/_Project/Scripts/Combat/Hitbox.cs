using UnityEngine;

[RequireComponent(typeof(Collider))]
public class Hitbox : MonoBehaviour
{
    private Collider hitboxCollider;
    private AttackData currentAttackData; 

    private void Awake()
    {
        hitboxCollider = GetComponent<Collider>();
        hitboxCollider.enabled = false;
        hitboxCollider.isTrigger = true;
    }

    public void ActivarHitbox(AttackData data)
    {
        currentAttackData = data;
        hitboxCollider.enabled = true;
    }

    public void DesactivarHitbox() => hitboxCollider.enabled = false;

    private void OnTriggerEnter(Collider other)
    {
        Hurtbox objetivo = other.GetComponent<Hurtbox>();

        if (objetivo != null && currentAttackData != null)
        {
            objetivo.RecibirImpacto(currentAttackData);
            DesactivarHitbox();
        }
    }
}