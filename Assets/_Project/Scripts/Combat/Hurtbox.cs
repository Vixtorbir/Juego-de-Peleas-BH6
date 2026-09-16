using UnityEngine;

/// <summary>
/// Damage receiver for a fighter. Resolves the guard matrix (attack height versus stance),
/// counter hits, aerial juggles, chip damage and combo damage scaling, then hands the
/// resulting reaction to the owner's state machine.
/// </summary>
public class Hurtbox : MonoBehaviour
{
    private enum GuardResult { Hit, Blocked, Whiffed, GuardBroken }

    private const float ComboScalingFactor = 0.85f;
    private const float MinComboScaling = 0.1f;
    private const float ChipDamageRatio = 0.15f;
    private const float CounterHitstunMultiplier = 1.5f;
    private const float CounterPushbackMultiplier = 1.4f;
    private const float CounterLaunchMultiplier = 1.3f;
    private const float CounterEffectsMultiplier = 1.5f;
    private const float AirResetLaunchForce = 5f;
    private const float HitstopPerDamagePoint = 0.005f;
    private const float ShakePerDamagePoint = 0.003f;
    private const float ShakeDuration = 0.1f;
    private const float CrouchInputThreshold = -0.5f;
    private const float BlockInputThreshold = -0.3f;

    private static readonly Color GizmoColor = new Color(0f, 1f, 0f, 0.2f);

    private RobotController controller;
    private HealthComponent health;
    private FightingCamera fightingCamera;
    private Collider hurtCollider;
    private int comboHitCount;

    public RobotController Owner => controller;

    private void Awake()
    {
        controller = GetComponentInParent<RobotController>();
        health = GetComponentInParent<HealthComponent>();
        hurtCollider = GetComponent<Collider>();

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            fightingCamera = mainCamera.GetComponent<FightingCamera>();
        }
    }

    /// <summary>
    /// Entry point for every incoming attack, whether from a melee <see cref="Hitbox"/> or a projectile.
    /// </summary>
    public void ReceiveHit(AttackData attack, RobotController attacker)
    {
        if (controller == null || attack == null) return;
        if (health != null && health.IsDead) return;

        bool counterHit = controller.IsInAttackStartup;
        PlayImpactFeedback(attack.damage, counterHit);

        if (!controller.IsGrounded)
        {
            ApplyDamage(attack.damage);
            ApplyAirborneReaction(attack, counterHit);
            return;
        }

        bool crouching = controller.MovementInput.y < CrouchInputThreshold;
        bool blocking = !counterHit && IsHoldingBack();
        GuardResult result = blocking ? ResolveGuard(attack.attackHeight, crouching) : GuardResult.Hit;

        switch (result)
        {
            case GuardResult.Whiffed:
                return;

            case GuardResult.Blocked:
                ApplyChipDamage(attack.damage);
                controller.ChangeState(new BlockstunState(controller, attack.blockstunDuration, crouching, attack.blockPushback, attacker));
                return;

            case GuardResult.GuardBroken:
                ApplyDamage(attack.damage);
                ApplyGroundedReaction(attack, false, attacker);
                return;

            default:
                ApplyDamage(attack.damage);
                ApplyGroundedReaction(attack, counterHit, attacker);
                return;
        }
    }

    public void ResetCombo()
    {
        comboHitCount = 0;
    }

    /// <summary>
    /// High attacks pass over a crouching guard, Mid attacks require a standing guard and
    /// Low attacks require a crouching guard.
    /// </summary>
    private static GuardResult ResolveGuard(AttackHeight height, bool crouching)
    {
        switch (height)
        {
            case AttackHeight.High:
                return crouching ? GuardResult.Whiffed : GuardResult.Blocked;
            case AttackHeight.Mid:
                return crouching ? GuardResult.GuardBroken : GuardResult.Blocked;
            case AttackHeight.Low:
                return crouching ? GuardResult.Blocked : GuardResult.GuardBroken;
            default:
                return GuardResult.Hit;
        }
    }

    private bool IsHoldingBack()
    {
        if (controller.opponentTarget == null) return false;

        float towardsOpponent = controller.MovementInput.x * controller.GetScreenFacingSign();
        return towardsOpponent < BlockInputThreshold;
    }

    private void PlayImpactFeedback(float damage, bool counterHit)
    {
        float multiplier = counterHit ? CounterEffectsMultiplier : 1f;
        float hitstop = damage * HitstopPerDamagePoint * multiplier;
        float shakeMagnitude = damage * ShakePerDamagePoint * multiplier;

        if (CombatEffectsManager.Instance != null)
        {
            CombatEffectsManager.Instance.TriggerHitstop(hitstop);
        }

        if (fightingCamera != null)
        {
            fightingCamera.ShakeCamera(ShakeDuration * multiplier, shakeMagnitude);
        }
    }

    private void ApplyDamage(float rawDamage)
    {
        if (health == null) return;

        comboHitCount++;

        float scaling = Mathf.Max(Mathf.Pow(ComboScalingFactor, comboHitCount - 1), MinComboScaling);
        health.TakeDamage(rawDamage * scaling);

        if (health.IsDead)
        {
            controller.ChangeState(new DeathState(controller));
        }
    }

    /// <summary>
    /// Blocked attacks still deal a fraction of their damage, but chip can never finish a round.
    /// </summary>
    private void ApplyChipDamage(float rawDamage)
    {
        if (health == null || health.IsDead) return;

        float chipDamage = rawDamage * ChipDamageRatio;

        if (health.CurrentHealth - chipDamage <= 0f)
        {
            chipDamage = health.CurrentHealth - 1f;
        }

        if (chipDamage > 0f)
        {
            health.TakeDamage(chipDamage);
        }
    }

    private void ApplyGroundedReaction(AttackData attack, bool counterHit, RobotController attacker)
    {
        if (health != null && health.IsDead) return;

        if (attack.isLauncher)
        {
            float launchMultiplier = counterHit ? CounterLaunchMultiplier : 1f;
            controller.ChangeState(new AirborneHurtState(controller, attack.launchForce * launchMultiplier));
            return;
        }

        float hitstunMultiplier = counterHit ? CounterHitstunMultiplier : 1f;
        float pushbackMultiplier = counterHit ? CounterPushbackMultiplier : 1f;

        controller.ChangeState(new HitstunState(
            controller,
            attack.hitstunDuration * hitstunMultiplier,
            attack.hitPushback * pushbackMultiplier,
            attacker));
    }

    private void ApplyAirborneReaction(AttackData attack, bool counterHit)
    {
        if (health != null && health.IsDead) return;

        float launchMultiplier = counterHit ? CounterLaunchMultiplier : 1f;
        float launchForce = attack.isLauncher ? attack.launchForce : AirResetLaunchForce;

        controller.ChangeState(new AirborneHurtState(controller, launchForce * launchMultiplier));
    }

    private void OnDrawGizmos()
    {
        Collider volume = hurtCollider != null ? hurtCollider : GetComponent<Collider>();
        if (volume == null) return;

        CombatGizmos.DrawCollider(volume, GizmoColor);
    }
}
