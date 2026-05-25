using UnityEngine;

[RequireComponent(typeof(Collider))]
public class Hitbox : MonoBehaviour
{
    private Collider hitboxCollider;

    private void Awake()
    {
        hitboxCollider = GetComponent<Collider>();
        // Nos aseguramos de que empiece desactivado, ¡solo hace daño cuando atacamos!
        hitboxCollider.enabled = false;

        // Es vital que sea un Trigger para que no empuje físicamente al otro personaje
        hitboxCollider.isTrigger = true;
    }

    // Estas funciones las llamaremos desde nuestro AttackState
    public void ActivarHitbox() => hitboxCollider.enabled = true;
    public void DesactivarHitbox() => hitboxCollider.enabled = false;

    private void OnTriggerEnter(Collider other)
    {
        // Si chocamos contra algo que tiene el script Hurtbox...
        Hurtbox objetivo = other.GetComponent<Hurtbox>();

        if (objetivo != null)
        {
            // ¡Hemos conectado el golpe!
            objetivo.RecibirImpacto();

            // Desactivamos la hitbox inmediatamente para no golpear dos veces en el mismo frame
            DesactivarHitbox();
        }
    }
}