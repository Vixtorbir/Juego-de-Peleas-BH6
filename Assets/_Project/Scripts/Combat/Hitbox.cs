using UnityEngine;

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

        Rigidbody body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        attackerController = GetComponentInParent<RobotController>();
    }

    // Box is reshaped per attack from its authored offset/size (fighter space, +Z towards the opponent).
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
        if (target.TryReceiveHit(currentAttackData, attackerController, contactPoint, true, out HitRejection rejection))
        {
            if (attackerController != null) attackerController.NotifyAttackConnected();
            Deactivate();
            return;
        }

        ResolveRejection(target, rejection, contactPoint);
    }

    // Deflected strikes are spent like hitting armour; a height whiff keeps the box live.
    private void ResolveRejection(Hurtbox target, HitRejection rejection, Vector3 contactPoint)
    {
        switch (rejection)
        {
            case HitRejection.Invulnerable:
            case HitRejection.Down:
                target.NotifyDeflected(contactPoint, attackerController);
                Deactivate();
                break;
            case HitRejection.Dead:
                Deactivate();
                break;
        }
    }

    // PhysX drops ignore pairs when a collider is disabled, so this has to be re-applied on every activation.
    private void IgnoreOwnerBody()
    {
        if (attackerController == null) return;

        CharacterController ownerBody = attackerController.characterController;
        if (ownerBody != null && ownerBody.enabled) Physics.IgnoreCollision(hitboxCollider, ownerBody, true);
    }

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
