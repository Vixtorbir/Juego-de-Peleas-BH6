using UnityEngine;

/// <summary>
/// Trigger volume enabled only during an attack's active frames. The box is reshaped on every
/// activation from the attack's authored offset and size (fighter space, +Z towards the opponent),
/// so a jab, a roundhouse and a sweep each project their own reach instead of sharing one volume
/// parked in front of the chest. On the first valid contact it forwards the attack data to the
/// opponent's <see cref="Hurtbox"/> and disables itself so a single swing cannot hit more than once.
/// The owner's own capsule is excluded from the pair on every activation, so the physics step never
/// reports the swing overlapping the body it belongs to. The holder carries a kinematic Rigidbody
/// so the volume is a kinematic trigger that PhysX tracks as a moving actor, rather than a static
/// shape dragged around by its parent transform.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(Rigidbody))]
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

        if (!TryGetComponent(out Rigidbody body)) body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        attackerController = GetComponentInParent<RobotController>();
    }

    /// <summary>
    /// Enables the volume for the attack's active window. Activation happens on the render frame
    /// that crosses the startup threshold, while the overlap itself is resolved by the next physics
    /// step: with the fixed step pinned to 60 Hz and the frame rate locked to match, the contact is
    /// reported at most one frame after the hitbox goes live, the same tick on which the animation
    /// reaches its extension pose.
    /// </summary>
    public void Activate(AttackData data)
    {
        currentAttackData = data;
        ApplyShape(hitboxCollider, data);
        hitboxCollider.enabled = true;
        IgnoreOwnerBody();
    }

    public void Deactivate()
    {
        hitboxCollider.enabled = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (currentAttackData == null) return;

        if (!other.TryGetComponent(out Hurtbox target) || target.Owner == attackerController) return;

        Vector3 contactPoint = ComputeContactPoint(other);
        if (target.ReceiveHit(currentAttackData, attackerController, contactPoint, true) && attackerController != null)
        {
            attackerController.NotifyAttackConnected();
        }

        Deactivate();
    }

    /// <summary>
    /// The ignore pair is re-applied after each enable because PhysX drops it whenever either
    /// collider is disabled, which happens on every attack and on round resets.
    /// </summary>
    private void IgnoreOwnerBody()
    {
        if (attackerController == null) return;

        CharacterController ownerBody = attackerController.characterController;
        if (ownerBody != null && ownerBody.enabled) Physics.IgnoreCollision(hitboxCollider, ownerBody, true);
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
