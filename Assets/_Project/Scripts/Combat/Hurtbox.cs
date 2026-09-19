using System;
using UnityEngine;

/// <summary>
/// Damage receiver for a fighter. Resolves the guard matrix (attack height versus stance),
/// counter hits, aerial juggles, chip damage and combo damage scaling, then hands the
/// resulting reaction to the owner's state machine. Combo progress is broadcast so the HUD can
/// present the hit count and accumulated damage without polling.
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
    private const float AirResetLaunchForce = 7f;
    private const float AirHitDrift = 1.5f;
    private const float CrouchInputThreshold = -0.5f;
    private const float BlockInputThreshold = -0.3f;

    private const float HeavyImpactHitstop = 0.2f;
    private const float HeavyImpactShakeDuration = 0.22f;
    private const float HeavyImpactShakeMagnitude = 0.28f;
    private const float BlockedHitstopRatio = 0.6f;
    private const float BlockedShakeRatio = 0.5f;
    private const float KnockoutHitstop = 0.45f;
    private const float KnockoutShakeDuration = 0.4f;
    private const float KnockoutShakeMagnitude = 0.35f;
    private const float LauncherShakeVerticalBias = 1f;
    private const float HeavyImpactZoomPunch = -1.5f;
    private const float KnockoutZoomPunch = -2.5f;

    private static readonly float[] HitstopByWeight = { 0.08f, 0.14f, 0.16f };
    private static readonly float[] ShakeDurationByWeight = { 0.1f, 0.16f, 0.2f };
    private static readonly float[] ShakeMagnitudeByWeight = { 0.06f, 0.14f, 0.2f };

    private static readonly Color GizmoColor = new Color(0f, 1f, 0f, 0.2f);

    private RobotController controller;
    private HealthComponent health;
    private FightingCamera fightingCamera;
    private Collider hurtCollider;
    private int comboHitCount;
    private float totalComboDamage;

    /// <summary>Raised after every clean hit of a combo with the running hit count and scaled damage total.</summary>
    public event Action<int, float> OnComboHit;

    /// <summary>Raised once when a combo in progress ends because the defender regained control.</summary>
    public event Action OnComboEnd;

    public RobotController Owner => controller;
    public int ComboHitCount => comboHitCount;
    public float TotalComboDamage => totalComboDamage;

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
    /// Returns true when the attack connected (clean hit or block) so the attacker can open its
    /// cancel window; false when it was dodged, whiffed by height or the defender is already down.
    /// </summary>
    public bool ReceiveHit(AttackData attack, RobotController attacker)
    {
        Vector3 chest = hurtCollider != null ? hurtCollider.bounds.center : transform.position;
        return ReceiveHit(attack, attacker, chest);
    }

    /// <summary>
    /// Overload taking the world-space contact point so the impact flash can be placed where the
    /// strike actually met the body.
    /// </summary>
    public bool ReceiveHit(AttackData attack, RobotController attacker, Vector3 contactPoint)
    {
        if (controller == null || attack == null) return false;
        if (health != null && health.IsDead) return false;
        if (controller.IsInvincible) return false;

        bool counterHit = controller.IsInAttackStartup;

        if (!controller.IsGrounded)
        {
            PlayImpactFeedback(attack, attacker, counterHit, false, contactPoint);
            ApplyDamage(attack.damage);
            ApplyAirborneReaction(attack, counterHit, attacker);
            return true;
        }

        bool crouching = controller.MovementInput.y < CrouchInputThreshold;
        bool blocking = !counterHit && IsHoldingBack();
        GuardResult result = blocking ? ResolveGuard(attack.attackHeight, crouching) : GuardResult.Hit;

        switch (result)
        {
            case GuardResult.Whiffed:
                return false;

            case GuardResult.Blocked:
                PlayImpactFeedback(attack, attacker, false, true, contactPoint);
                ApplyChipDamage(attack.damage);
                controller.ChangeState(new BlockstunState(controller, attack.blockstunDuration, crouching, attack.blockPushback, attacker));
                return true;

            case GuardResult.GuardBroken:
                PlayImpactFeedback(attack, attacker, false, false, contactPoint);
                ApplyDamage(attack.damage);
                ApplyGroundedReaction(attack, false, attacker);
                return true;

            default:
                PlayImpactFeedback(attack, attacker, counterHit, false, contactPoint);
                ApplyDamage(attack.damage);
                ApplyGroundedReaction(attack, counterHit, attacker);
                return true;
        }
    }

    public void ResetCombo()
    {
        if (comboHitCount == 0) return;

        comboHitCount = 0;
        totalComboDamage = 0f;
        OnComboEnd?.Invoke();
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

    /// <summary>
    /// Hitstop and screen shake are tiered by <see cref="AttackWeight"/>: light attacks freeze for
    /// five frames, medium and heavy ones for eight to ten. Launchers and counter hits step up to
    /// the heavy-impact tier with a directional shake along the knockback and a lens punch; blocks
    /// soften the freeze and the shake and never punch the lens. The flash is spawned at the
    /// contact point and keeps animating through the freeze.
    /// </summary>
    private void PlayImpactFeedback(AttackData attack, RobotController attacker, bool counterHit, bool blocked, Vector3 contactPoint)
    {
        int tier = (int)attack.weight;
        float hitstop = HitstopByWeight[tier];
        float shakeDuration = ShakeDurationByWeight[tier];
        float shakeMagnitude = ShakeMagnitudeByWeight[tier];

        bool heavyImpact = !blocked && (counterHit || attack.isLauncher);
        if (heavyImpact)
        {
            hitstop = Mathf.Max(hitstop, HeavyImpactHitstop);
            shakeDuration = Mathf.Max(shakeDuration, HeavyImpactShakeDuration);
            shakeMagnitude = Mathf.Max(shakeMagnitude, HeavyImpactShakeMagnitude);
        }

        if (blocked)
        {
            hitstop *= BlockedHitstopRatio;
            shakeMagnitude *= BlockedShakeRatio;
        }

        if (CombatEffectsManager.Instance != null)
        {
            CombatEffectsManager.Instance.TriggerHitstop(hitstop);
        }

        if (fightingCamera != null)
        {
            fightingCamera.ShakeCamera(shakeDuration, shakeMagnitude, ComputeKnockbackDirection(attack, attacker));
            if (heavyImpact || (!blocked && attack.weight == AttackWeight.Heavy)) fightingCamera.PunchZoom(HeavyImpactZoomPunch);
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
        float vertical = attack.isLauncher ? LauncherShakeVerticalBias : 0f;
        return new Vector3(horizontal, vertical, 0f);
    }

    private void PlayKnockoutFeedback()
    {
        if (CombatEffectsManager.Instance != null)
        {
            CombatEffectsManager.Instance.TriggerHitstop(KnockoutHitstop);
        }

        if (fightingCamera != null)
        {
            fightingCamera.ShakeCamera(KnockoutShakeDuration, KnockoutShakeMagnitude, Vector3.zero);
            fightingCamera.PunchZoom(KnockoutZoomPunch);
        }

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayKnockout();
        }
    }

    private void ApplyDamage(float rawDamage)
    {
        if (health == null) return;

        comboHitCount++;

        float scaling = Mathf.Max(Mathf.Pow(ComboScalingFactor, comboHitCount - 1), MinComboScaling);
        float scaledDamage = rawDamage * scaling;
        totalComboDamage += scaledDamage;
        health.TakeDamage(scaledDamage);
        OnComboHit?.Invoke(comboHitCount, totalComboDamage);

        if (health.IsDead)
        {
            PlayKnockoutFeedback();
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
            controller.ChangeState(new AirborneHurtState(controller, attack.launchForce * launchMultiplier, attack.hitPushback, attacker));
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

    /// <summary>
    /// Air hits re-launch the defender: launchers with their full force, everything else with a
    /// short pop that keeps the juggle alive and drifts the body slightly away from the attacker.
    /// </summary>
    private void ApplyAirborneReaction(AttackData attack, bool counterHit, RobotController attacker)
    {
        if (health != null && health.IsDead) return;

        float launchMultiplier = counterHit ? CounterLaunchMultiplier : 1f;
        float launchForce = attack.isLauncher ? attack.launchForce : AirResetLaunchForce;
        float drift = attack.isLauncher ? attack.hitPushback : AirHitDrift;

        controller.ChangeState(new AirborneHurtState(controller, launchForce * launchMultiplier, drift, attacker));
    }

    private void OnDrawGizmos()
    {
        Collider volume = hurtCollider != null ? hurtCollider : GetComponent<Collider>();
        if (volume == null) return;

        CombatGizmos.DrawCollider(volume, GizmoColor);
    }
}
