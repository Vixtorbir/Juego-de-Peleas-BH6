using UnityEngine;

/// <summary>
/// Trigger volume enabled only during an attack's active frames. On the first valid
/// contact it forwards the attack data to the opponent's <see cref="Hurtbox"/> and disables
/// itself so a single swing cannot hit more than once.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Hitbox : MonoBehaviour
{
    private static readonly Color ActiveColor = new Color(1f, 0f, 0f, 0.35f);
    private static readonly Color InactiveColor = new Color(1f, 0f, 0f, 0.08f);

    private Collider hitboxCollider;
    private AttackData currentAttackData;
    private RobotController attackerController;

    public bool IsActive => hitboxCollider != null && hitboxCollider.enabled;

    private void Awake()
    {
        hitboxCollider = GetComponent<Collider>();
        hitboxCollider.isTrigger = true;
        hitboxCollider.enabled = false;

        attackerController = GetComponentInParent<RobotController>();
    }

    public void Activate(AttackData data)
    {
        currentAttackData = data;
        hitboxCollider.enabled = true;
    }

    public void Deactivate()
    {
        hitboxCollider.enabled = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (currentAttackData == null) return;

        Hurtbox target = other.GetComponent<Hurtbox>();
        if (target == null || target.Owner == attackerController) return;

        target.ReceiveHit(currentAttackData, attackerController);
        Deactivate();
    }

    private void OnDrawGizmos()
    {
        Collider volume = hitboxCollider != null ? hitboxCollider : GetComponent<Collider>();
        if (volume == null) return;

        bool active = Application.isPlaying && volume.enabled;
        CombatGizmos.DrawCollider(volume, active ? ActiveColor : InactiveColor);
    }
}
