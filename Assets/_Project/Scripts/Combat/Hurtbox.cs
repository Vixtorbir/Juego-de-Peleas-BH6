using System;
using UnityEngine;

/// <summary>
/// Why an incoming strike did not connect. Deflections (the body could not be hit) consume the
/// strike and get their own feedback; a height whiff is a genuine miss and leaves the strike live.
/// </summary>
public enum HitRejection
{
    None,
    Invulnerable,
    Down,
    Dead,
    HeightWhiff
}

/// <summary>
/// What a single registered strike did to this body, for presentation. Hit numbers count from 1,
/// so the HUD can show a per-hit readout on the very first contact and promote to a combo counter
/// from the second. Chip damage arrives with <see cref="Blocked"/> set and <see cref="HitNumber"/> 0.
/// </summary>
public struct HitFeedback
{
    public int HitNumber;
    public float Damage;
    public bool CounterHit;
    public bool Blocked;
}

/// <summary>
/// Running state of the combo being landed on a fighter. A plain struct on the hurtbox so combo
/// tracking, bounce limits and route proration never touch the heap.
/// </summary>
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

/// <summary>
/// Damage receiver for a fighter. Resolves the guard matrix (attack height versus posture),
/// counter hits, aerial juggles, chip damage, combo damage scaling and hitstun decay, then hands
/// the resulting reaction to the owner's state machine. Whether a guard can be raised and at which
/// height is read from the current state rather than from the stick: a fighter in hitstun or
/// mid-attack cannot block, so a chain that connects is a true combo, and a standing fighter
/// holding down is still hit high. Every impact freezes the two fighters for their own tier
/// durations from <see cref="CombatRules"/>, so the defender is always held a little longer than
/// the attacker. Combo progress is broadcast so the HUD can present the hit count and accumulated
/// damage without polling.
/// </summary>
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

    /// <summary>Raised after every clean hit of a combo with the running hit count and scaled damage total.</summary>
    public event Action<int, float> OnComboHit;

    /// <summary>Raised for every strike that registered on this body, hits and blocked chip alike, before the reaction state changes.</summary>
    public event Action<HitFeedback> OnHitRegistered;

    /// <summary>Raised once when a combo in progress ends because the defender regained control.</summary>
    public event Action OnComboEnd;

    public RobotController Owner => controller;
    public ComboState Combo => combo;
    public int ComboHitCount => combo.Hits;
    public float TotalComboDamage => combo.Damage;

    /// <summary>Strikes deflected by this body since startup; exposed for tests and telemetry.</summary>
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

    /// <summary>
    /// Entry point for every incoming attack, whether from a melee <see cref="Hitbox"/> or a projectile.
    /// Returns true when the attack connected (clean hit or block) so the attacker can open its
    /// cancel window; false when it was dodged, whiffed by height or the defender is already down.
    /// </summary>
    public bool ReceiveHit(AttackData attack, RobotController attacker)
    {
        Vector3 chest = hurtCollider != null ? hurtCollider.bounds.center : transform.position;
        return ReceiveHit(attack, attacker, chest, true);
    }

    /// <summary>
    /// Overload taking the world-space contact point so the impact flash can be placed where the
    /// strike actually met the body. <paramref name="freezeAttacker"/> is false for projectiles:
    /// a fireball landing from full screen never stalls the fighter who threw it.
    /// </summary>
    public bool ReceiveHit(AttackData attack, RobotController attacker, Vector3 contactPoint, bool freezeAttacker)
    {
        return TryReceiveHit(attack, attacker, contactPoint, freezeAttacker, out _);
    }

    /// <summary>
    /// Same as <see cref="ReceiveHit(AttackData, RobotController, Vector3, bool)"/> but reports why a
    /// strike was refused, so the striker can tell a deflection from a whiff.
    /// </summary>
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
                ApplyChipDamage(attack.damage);
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

    /// <summary>
    /// Feedback for a strike this body deflected: a silver spark at the contact point, the deflect
    /// sound, a body pop and a small camera kick. No hitstop on either side, nothing connected.
    /// </summary>
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

    /// <summary>
    /// Impact feedback for a rebound resolved by the juggle state: the bounce tier freezes the
    /// body, shakes the camera along the rebound and pays the route proration.
    /// </summary>
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

    /// <summary>
    /// Guard matrix. Overheads (jumping attacks, the overhead command normal) must be blocked
    /// standing and break a crouching guard. Otherwise High attacks pass over a crouching guard,
    /// Mid attacks are blocked in either posture, and Low attacks must be blocked crouching.
    /// </summary>
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

    /// <summary>
    /// Hitstop, shake and camera punch come from the attack's <see cref="CombatRules"/> tier. Both
    /// fighters freeze on their own timers: the defender for the tier plus any counter-hit bonus, the
    /// attacker for the shorter attacker time. Launchers and counter hits step up to the launcher tier
    /// with a shake along the knockback and a lens punch; blocks soften the freeze and the shake and
    /// never punch the lens. The flash is spawned at the contact point and keeps animating through
    /// the freeze.
    /// </summary>
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

    /// <summary>
    /// Damage is scaled by the combo position and by the route proration paid on launchers and
    /// bounces, so a long juggle route deals less than a short grounded string of the same hits.
    /// </summary>
    private void ApplyDamage(float rawDamage, RobotController attacker, bool counterHit)
    {
        if (health == null) return;

        combo.Hits++;

        float scaledDamage = rawDamage * Rules.ComboScaling(combo.Hits) * combo.Proration;
        combo.Damage += scaledDamage;
        health.TakeDamage(scaledDamage);
        OnHitRegistered?.Invoke(new HitFeedback { HitNumber = combo.Hits, Damage = scaledDamage, CounterHit = counterHit, Blocked = false });
        OnComboHit?.Invoke(combo.Hits, combo.Damage);

        if (health.IsDead)
        {
            PlayKnockoutFeedback(attacker);
            controller.EnterDeath();
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
            OnHitRegistered?.Invoke(new HitFeedback { HitNumber = 0, Damage = chipDamage, CounterHit = false, Blocked = true });
        }
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

    /// <summary>
    /// Air hits re-launch the defender with juggle decay: every air hit lowers the next launch and
    /// steepens gravity, so the body sinks out of reach after a few reps. A wall bounce is granted
    /// once per combo and falls back to a plain relaunch; a spent ground bounce still slams the body
    /// down, straight into the knockdown.
    /// </summary>
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

    /// <summary>
    /// Launchers relaunch with their own force. A grounded attacker's normal pops the body by the
    /// air-reset force; an airborne attacker's normal instead carries the body along the attacker's
    /// current vertical speed plus a small pop, so an air chain keeps both fighters on the same arc
    /// instead of firing the defender away from a falling attacker. Juggle decay scales the pop.
    /// </summary>
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
