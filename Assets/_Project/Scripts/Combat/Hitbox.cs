using UnityEngine;

/// <summary>
/// Trigger volume enabled only during an attack's active frames. The box is reshaped on every
/// activation from the attack's authored offset and size (fighter space, +Z towards the opponent),
/// so a jab, a roundhouse and a sweep each project their own reach instead of sharing one volume
/// parked in front of the chest. On the first valid contact it forwards the attack data to the
/// opponent's <see cref="Hurtbox"/> and disables itself so a single swing cannot hit more than once.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class Hitbox : MonoBehaviour
{
    private static readonly Color ActiveColor = new Color(1f, 0f, 0f, 0.35f);
    private static readonly Color InactiveColor = new Color(1f, 0f, 0f, 0.08f);

    [Tooltip("Shape previewed in the Scene view while no attack is active.")]
    [SerializeField] private AttackData previewAttack;

    private BoxCollider hitboxCollider;
    private AttackData currentAttackData;
    private RobotController attackerController;

    public bool IsActive => hitboxCollider != null && hitboxCollider.enabled;

    private void Awake()
    {
        hitboxCollider = GetComponent<BoxCollider>();
        hitboxCollider.isTrigger = true;
        hitboxCollider.enabled = false;

        attackerController = GetComponentInParent<RobotController>();
    }

    public void Activate(AttackData data)
    {
        currentAttackData = data;
        ApplyShape(hitboxCollider, data);
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

        Vector3 contactPoint = ComputeContactPoint(other);
        if (target.ReceiveHit(currentAttackData, attackerController, contactPoint) && attackerController != null)
        {
            attackerController.NotifyAttackConnected();
        }

        Deactivate();
    }

    /// <summary>
    /// Point on the defender's volume nearest the centre of the striking box, so the impact flash
    /// lands on the limb's reach rather than inside either body.
    /// </summary>
    private Vector3 ComputeContactPoint(Collider defender)
    {
        Vector3 strikeCentre = hitboxCollider.bounds.center;
        return defender.bounds.ClosestPoint(strikeCentre);
    }

    private static void ApplyShape(BoxCollider box, AttackData data)
    {
        if (data == null) return;

        box.center = data.hitboxOffset;
        box.size = data.hitboxSize;
    }

    private void OnDrawGizmos()
    {
        BoxCollider volume = hitboxCollider != null ? hitboxCollider : GetComponent<BoxCollider>();
        if (volume == null) return;

        bool active = Application.isPlaying && volume.enabled;
        if (!active && previewAttack != null) ApplyShape(volume, previewAttack);

        CombatGizmos.DrawCollider(volume, active ? ActiveColor : InactiveColor);
    }
}
