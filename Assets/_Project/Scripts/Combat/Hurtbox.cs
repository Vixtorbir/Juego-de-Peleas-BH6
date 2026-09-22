using System;
using UnityEngine;

public enum HitRejection
{
    None,
    Invulnerable,
    Down,
    Dead,
    HeightWhiff
}

public struct HitFeedback
{
    public int HitNumber;
    public float Damage;
    public bool CounterHit;
    public bool Blocked;
}

public struct ComboState
{
    public int Hits;
    public int JuggleHits;
    public float Damage;
    public float Proration;
    public int WallBounces;
    public int GroundBounces;

    public static ComboState Fresh => new ComboState { Proration = 1f };
}

// Resolves every incoming hit: guard, counter hit, juggles, damage scaling and the reaction state.
public class Hurtbox : MonoBehaviour
{
    private enum GuardResult { Hit, Blocked, Whiffed, GuardBroken }

    private const float ChipDamageRatio = 0.15f;
    private const float CounterHitstunMultiplier = 1.5f;
    private const float CounterPushbackMultiplier = 1.4f;
    private const float CounterLaunchMultiplier = 1.3f;
    private const float BlockInputThreshold = -0.3f;
    private const float LauncherShakeVerticalBias = 1f;
    private const float BounceShakeVerticalBias = 0.6f;

    private static readonly Color GizmoColor = new Color(0f, 1f, 0f, 0.2f);

    private RobotController controller;
    private HealthComponent health;
    private FightingCamera fightingCamera;
    private Collider hurtCollider;
    private ComboState combo = ComboState.Fresh;

    public event Action<int, float> OnComboHit;

    public event Action<HitFeedback> OnHitRegistered;

    public event Action OnComboEnd;

    public RobotController Owner => controller;
    public ComboState Combo => combo;
    public int ComboHitCount => combo.Hits;
    public float TotalComboDamage => combo.Damage;

    public int DeflectCount { get; private set; }

    private CombatRules Rules => controller.Rules;

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

    public bool ReceiveHit(AttackData attack, RobotController attacker)
    {
        Vector3 chest = hurtCollider != null ? hurtCollider.bounds.center : transform.position;
        return ReceiveHit(attack, attacker, chest, true);
    }

    public bool ReceiveHit(AttackData attack, RobotController attacker, Vector3 contactPoint, bool freezeAttacker)
    {
        return TryReceiveHit(attack, attacker, contactPoint, freezeAttacker, out _);
    }

    public bool TryReceiveHit(AttackData attack, RobotController attacker, Vector3 contactPoint, bool freezeAttacker, out HitRejection rejection)
    {
        rejection = HitRejection.None;
        if (controller == null || attack == null) return false;

        if (health != null && health.IsDead)
        {
            rejection = HitRejection.Dead;
            return false;
        }

        if (controller.IsInvincible)
        {
            rejection = HitRejection.Invulnerable;
            return false;
        }

        RobotState posture = controller.CurrentState;
        bool counterHit = controller.IsInAttackStartup;

        if (!controller.IsGrounded)
        {
            PlayImpactFeedback(attack, attacker, counterHit, false, contactPoint, freezeAttacker);
            ApplyDamage(attack.damage, attacker, counterHit);
            ApplyAirborneReaction(attack, counterHit, attacker);
            return true;
        }

        if (posture != null && posture.IgnoresGroundedHits)
        {
            rejection = HitRejection.Down;
            return false;
        }

        bool crouching = posture != null && posture.IsCrouchingPosture;
        bool blocking = !counterHit && posture != null && posture.CanGuard && IsHoldingBack();
        GuardResult result = blocking ? ResolveGuard(attack.attackHeight, attack.isOverhead, crouching) : GuardResult.Hit;

        switch (result)
        {
            case GuardResult.Whiffed:
                rejection = HitRejection.HeightWhiff;
                return false;

            case GuardResult.Blocked:
                PlayImpactFeedback(attack, attacker, false, true, contactPoint, freezeAttacker);
                ApplyChipDamage(attack.damage, attacker);
                controller.EnterBlockstun(attack.blockstunDuration, crouching, attack.blockPushback, attacker);
                return true;

            case GuardResult.GuardBroken:
                PlayImpactFeedback(attack, attacker, false, false, contactPoint, freezeAttacker);
                ApplyDamage(attack.damage, attacker, false);
                ApplyGroundedReaction(attack, false, attacker);
                return true;

            default:
                PlayImpactFeedback(attack, attacker, counterHit, false, contactPoint, freezeAttacker);
                ApplyDamage(attack.damage, attacker, counterHit);
                ApplyGroundedReaction(attack, counterHit, attacker);
                return true;
        }
    }

    public void ResetCombo()
    {
        if (combo.Hits == 0) return;

        combo = ComboState.Fresh;
        OnComboEnd?.Invoke();
    }

    public void NotifyDeflected(Vector3 contactPoint, RobotController attacker)
    {
        DeflectCount++;

        CombatRules rules = Rules;
        if (controller.Flash != null) controller.Flash.Pop(rules.deflectFlashColor, rules.deflectFlashDuration);

        if (fightingCamera != null)
        {
            fightingCamera.Punch(ComputeKnockbackDirection(null, attacker), rules.deflectCameraPunch);
        }

        if (HitsparkPool.Instance != null) HitsparkPool.Instance.Play(contactPoint, HitsparkKind.Deflect);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayDeflect();
    }

    public void NotifyBounce(BounceMode bounce, RobotController attacker)
    {
        ImpactTier tier = Rules.bounceImpact;
        controller.FreezeFor(tier.hitstop.defender);

        Vector3 direction = bounce == BounceMode.WallBounce
            ? new Vector3(attacker != null ? Mathf.Sign(attacker.transform.position.x - transform.position.x) : 0f, BounceShakeVerticalBias, 0f)
            : Vector3.up;

        if (fightingCamera != null)
        {
            fightingCamera.ShakeCamera(tier.shakeDuration, tier.shakeMagnitude, direction);
            fightingCamera.Punch(direction, tier.cameraPunch);
        }

        if (HitsparkPool.Instance != null)
        {
            Vector3 contact = hurtCollider != null ? hurtCollider.bounds.center : transform.position;
            HitsparkPool.Instance.Play(contact, HitsparkKind.Hit);
        }

        if (AudioManager.Instance != null) AudioManager.Instance.PlayBounce();
    }

    // Overheads must be blocked standing, lows crouching, mids either way; highs whiff on a crouching body.
    private static GuardResult ResolveGuard(AttackHeight height, bool overhead, bool crouching)
    {
        if (overhead) return crouching ? GuardResult.GuardBroken : GuardResult.Blocked;

        switch (height)
        {
            case AttackHeight.High:
                return crouching ? GuardResult.Whiffed : GuardResult.Blocked;
            case AttackHeight.Mid:
                return GuardResult.Blocked;
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

    // Defender freezes for the full tier (+ counter-hit bonus), attacker for the shorter attacker time.
    private void PlayImpactFeedback(AttackData attack, RobotController attacker, bool counterHit, bool blocked, Vector3 contactPoint, bool freezeAttacker)
    {
        CombatRules rules = Rules;
        ImpactTier tier = rules.ImpactFor(attack.weight);

        bool heavyImpact = !blocked && (counterHit || attack.isLauncher);
        if (heavyImpact)
        {
            ImpactTier launcher = rules.launcherImpact;
            tier.hitstop.defender = Mathf.Max(tier.hitstop.defender, launcher.hitstop.defender);
            tier.hitstop.attacker = Mathf.Max(tier.hitstop.attacker, launcher.hitstop.attacker);
            tier.shakeDuration = Mathf.Max(tier.shakeDuration, launcher.shakeDuration);
            tier.shakeMagnitude = Mathf.Max(tier.shakeMagnitude, launcher.shakeMagnitude);
            tier.cameraPunch = Mathf.Max(tier.cameraPunch, launcher.cameraPunch);
        }

        float defenderFreeze = tier.hitstop.defender + (counterHit ? rules.counterHitstopBonus : 0f);
        float attackerFreeze = tier.hitstop.attacker;
        if (blocked)
        {
            defenderFreeze *= rules.blockedHitstopRatio;
            attackerFreeze *= rules.blockedHitstopRatio;
            tier.shakeMagnitude *= rules.blockedShakeRatio;
            tier.cameraPunch *= rules.blockedShakeRatio;
        }

        controller.FreezeFor(defenderFreeze);
        if (freezeAttacker && attacker != null) attacker.FreezeFor(attackerFreeze);

        if (fightingCamera != null)
        {
            Vector3 knockback = ComputeKnockbackDirection(attack, attacker);
            fightingCamera.ShakeCamera(tier.shakeDuration, tier.shakeMagnitude, knockback);
            fightingCamera.Punch(knockback, tier.cameraPunch);
            if (heavyImpact || (!blocked && attack.weight == AttackWeight.Heavy)) fightingCamera.PunchZoom(rules.heavyImpactZoomPunch);
        }

        if (HitsparkPool.Instance != null)
        {
            HitsparkKind kind = blocked ? HitsparkKind.Block : counterHit ? HitsparkKind.CounterHit : HitsparkKind.Hit;
            HitsparkPool.Instance.Play(contactPoint, kind);
        }

        if (AudioManager.Instance != null)
        {
            if (blocked) AudioManager.Instance.PlayBlock();
            else AudioManager.Instance.PlayHit(attack, counterHit);
        }
    }

    private Vector3 ComputeKnockbackDirection(AttackData attack, RobotController attacker)
    {
        float horizontal = attacker != null ? Mathf.Sign(transform.position.x - attacker.transform.position.x) : 0f;
        float vertical = attack != null && attack.isLauncher ? LauncherShakeVerticalBias : 0f;
        return new Vector3(horizontal, vertical, 0f);
    }

    private void PlayKnockoutFeedback(RobotController attacker)
    {
        CombatRules rules = Rules;
        controller.FreezeFor(rules.knockoutHitstop.defender);
        if (attacker != null) attacker.FreezeFor(rules.knockoutHitstop.attacker);

        if (CombatEffectsManager.Instance != null)
        {
            CombatEffectsManager.Instance.TriggerSlowMotion(rules.knockoutSlowMotionScale, rules.knockoutSlowMotionDuration);
        }

        if (fightingCamera != null)
        {
            fightingCamera.ShakeCamera(rules.knockoutShakeDuration, rules.knockoutShakeMagnitude, Vector3.zero);
            fightingCamera.PunchZoom(rules.knockoutZoomPunch);
        }

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayKnockout();
        }
    }

    // Scaled by combo position and by the proration paid on launchers/bounces.
    private void ApplyDamage(float rawDamage, RobotController attacker, bool counterHit)
    {
        if (health == null) return;

        combo.Hits++;

        float scaledDamage = rawDamage * Rules.ComboScaling(combo.Hits) * combo.Proration;
        combo.Damage += scaledDamage;
        health.TakeDamage(scaledDamage);
        AwardSuperMeter(scaledDamage, attacker, false);
        OnHitRegistered?.Invoke(new HitFeedback { HitNumber = combo.Hits, Damage = scaledDamage, CounterHit = counterHit, Blocked = false });
        OnComboHit?.Invoke(combo.Hits, combo.Damage);

        if (health.IsDead)
        {
            PlayKnockoutFeedback(attacker);
            controller.EnterDeath();
        }
    }

    // Chip damage can never finish a round.
    private void ApplyChipDamage(float rawDamage, RobotController attacker)
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
            AwardSuperMeter(chipDamage, attacker, true);
            OnHitRegistered?.Invoke(new HitFeedback { HitNumber = 0, Damage = chipDamage, CounterHit = false, Blocked = true });
        }
    }

    // Attacker gets meter at a higher rate than the defender so pressure is rewarded.
    private void AwardSuperMeter(float damage, RobotController attacker, bool blocked)
    {
        if (controller != null && controller.SuperMeter != null) controller.SuperMeter.AwardForDamageTaken(damage, blocked);
        if (attacker != null && attacker.SuperMeter != null) attacker.SuperMeter.AwardForDamageDealt(damage, blocked);
    }

    private void ApplyGroundedReaction(AttackData attack, bool counterHit, RobotController attacker)
    {
        if (health != null && health.IsDead) return;

        CombatRules rules = Rules;
        float launchMultiplier = counterHit ? CounterLaunchMultiplier : 1f;
        float pushbackMultiplier = counterHit ? CounterPushbackMultiplier : 1f;

        switch (attack.ResolvedGroundedReaction)
        {
            case HitReaction.Launch:
                combo.JuggleHits = 0;
                combo.Proration *= rules.launcherProration;
                controller.EnterAirborneHurt(attack.launchForce * launchMultiplier, attack.hitPushback, attacker, BounceMode.None, attack.bounceForce);
                return;

            case HitReaction.WallBounce:
                combo.JuggleHits = 0;
                if (TryConsumeWallBounce())
                {
                    controller.EnterAirborneHurt(rules.wallBouncePop, rules.wallBounceDriftSpeed, attacker, BounceMode.WallBounce, attack.bounceForce);
                    return;
                }
                controller.EnterAirborneHurt(attack.launchForce * launchMultiplier, attack.hitPushback * pushbackMultiplier, attacker, BounceMode.None, attack.bounceForce);
                return;

            case HitReaction.GroundBounce:
                combo.JuggleHits = 0;
                controller.EnterAirborneHurt(TryConsumeGroundBounce() ? attack.bounceForce : rules.airResetLaunchForce, attack.hitPushback, attacker, BounceMode.None, attack.bounceForce);
                return;

            case HitReaction.Knockdown:
                controller.EnterKnockdown(rules.knockdownDuration, attack.hitPushback * pushbackMultiplier, attacker);
                return;

            default:
                float hitstunMultiplier = (counterHit ? CounterHitstunMultiplier : 1f) * rules.HitstunScale(combo.Hits);
                controller.EnterHitstun(attack.hitstunDuration * hitstunMultiplier, attack.hitPushback * pushbackMultiplier, attacker);
                return;
        }
    }

    // Each air hit lowers the next relaunch and raises gravity, so juggles run out after a few reps.
    private void ApplyAirborneReaction(AttackData attack, bool counterHit, RobotController attacker)
    {
        if (health != null && health.IsDead) return;

        CombatRules rules = Rules;
        combo.JuggleHits++;
        float launchMultiplier = (counterHit ? CounterLaunchMultiplier : 1f) * rules.RelaunchScale(combo.JuggleHits);

        switch (attack.airborneReaction)
        {
            case HitReaction.WallBounce:
                if (TryConsumeWallBounce())
                {
                    controller.EnterAirborneHurt(rules.wallBouncePop, rules.wallBounceDriftSpeed, attacker, BounceMode.WallBounce, attack.bounceForce);
                    return;
                }
                break;

            case HitReaction.GroundBounce:
                if (TryConsumeGroundBounce())
                {
                    controller.EnterAirborneHurt(-attack.bounceForce, rules.airHitDrift, attacker, BounceMode.GroundBounce, attack.bounceForce);
                    return;
                }
                controller.EnterAirborneHurt(-attack.bounceForce, rules.airHitDrift, attacker, BounceMode.None, attack.bounceForce);
                return;

            case HitReaction.Knockdown:
                controller.EnterAirborneHurt(0f, rules.airHitDrift, attacker, BounceMode.None, attack.bounceForce);
                return;
        }

        float launchForce = ResolveRelaunchForce(attack, attacker, launchMultiplier);
        float drift = attack.isLauncher ? attack.hitPushback : rules.airHitDrift;
        controller.EnterAirborneHurt(launchForce, drift, attacker, BounceMode.None, attack.bounceForce);
    }

    private float ResolveRelaunchForce(AttackData attack, RobotController attacker, float launchMultiplier)
    {
        if (attack.isLauncher) return attack.launchForce * launchMultiplier;

        bool airToAir = attacker != null && !attacker.IsGrounded;
        if (airToAir) return attacker.AirVelocity.y + Rules.airToAirPop * launchMultiplier;

        return Rules.airResetLaunchForce * launchMultiplier;
    }

    private bool TryConsumeWallBounce()
    {
        if (combo.WallBounces >= Rules.maxWallBounces) return false;

        combo.WallBounces++;
        combo.Proration *= Rules.bounceProration;
        return true;
    }

    private bool TryConsumeGroundBounce()
    {
        if (combo.GroundBounces >= Rules.maxGroundBounces) return false;

        combo.GroundBounces++;
        combo.Proration *= Rules.bounceProration;
        return true;
    }

    private void OnDrawGizmos()
    {
        Collider volume = hurtCollider != null ? hurtCollider : GetComponent<Collider>();
        if (volume == null) return;

        CombatGizmos.DrawCollider(volume, GizmoColor);
    }
}
