using UnityEngine;

[System.Serializable]
public struct HitstopTier
{
    public float defender;
    public float attacker;

    public HitstopTier(float defender, float attacker)
    {
        this.defender = defender;
        this.attacker = attacker;
    }
}

[System.Serializable]
public struct ImpactTier
{
    public HitstopTier hitstop;
    public float shakeDuration;
    public float shakeMagnitude;
    [Tooltip("Translational camera kick along the strike vector.")]
    public float cameraPunch;

    public ImpactTier(HitstopTier hitstop, float shakeDuration, float shakeMagnitude, float cameraPunch)
    {
        this.hitstop = hitstop;
        this.shakeDuration = shakeDuration;
        this.shakeMagnitude = shakeMagnitude;
        this.cameraPunch = cameraPunch;
    }
}

[CreateAssetMenu(fileName = "CombatRules", menuName = "Fighting Game/Combat Rules")]
public class CombatRules : ScriptableObject
{
    private const float Frame = 1f / 60f;

    [Header("Damage Proration")]
    [Tooltip("Multiplier applied per hit: hit n deals factor^(n-1) of its listed damage.")]
    [Range(0.5f, 1f)] public float comboScalingFactor = 0.85f;
    [Range(0.01f, 1f)] public float minComboScaling = 0.1f;
    [Tooltip("Extra proration applied to every hit after a launcher connects.")]
    [Range(0.1f, 1f)] public float launcherProration = 0.8f;
    [Tooltip("Extra proration applied to every hit after a wall or ground bounce.")]
    [Range(0.1f, 1f)] public float bounceProration = 0.7f;

    [Header("Hitstun Decay")]
    [Tooltip("Hitstun lost per combo hit, so grounded links stop being plus after a few reps.")]
    [Range(0f, 0.2f)] public float hitstunDecayPerHit = 0.04f;
    [Range(0.1f, 1f)] public float minHitstunScale = 0.65f;

    [Header("Juggle Physics")]
    [Tooltip("Gravity grows by this fraction per air hit, so each relaunch is shorter than the last.")]
    [Range(0f, 1f)] public float juggleGravityPerHit = 0.12f;
    [Tooltip("Relaunch force multiplier applied per air hit.")]
    [Range(0.5f, 1f)] public float relaunchDecay = 0.9f;
    [Range(0.1f, 1f)] public float minRelaunchScale = 0.55f;
    [Tooltip("Vertical pop given to a non-launcher air hit.")]
    public float airResetLaunchForce = 7f;
    [Tooltip("Horizontal drift of a non-launcher air hit.")]
    public float airHitDrift = 1.5f;
    [Tooltip("Vertical speed added above the attacker's own when an airborne attacker hits an airborne defender, so air chains carry the body along the attacker's arc.")]
    public float airToAirPop = 1f;
    public int maxWallBounces = 1;
    public int maxGroundBounces = 1;
    [Tooltip("Fraction of horizontal speed kept when the body rebounds off a wall.")]
    [Range(0f, 1f)] public float wallBounceRestitution = 0.6f;
    [Tooltip("Minimum vertical velocity after a wall rebound so the body hangs in reach.")]
    public float wallBouncePop = 6f;
    [Tooltip("Horizontal speed a wall-bounce hit sends the body towards the wall with.")]
    public float wallBounceDriftSpeed = 9f;

    [Header("Knockdown")]
    public float knockdownDuration = 0.6f;
    [Tooltip("Strike invulnerability granted on wake-up so a knockdown never chains into itself.")]
    public float wakeUpInvulnerability = 6f * Frame;

    [Header("Hitstop & Camera (frames at 60 Hz)")]
    public ImpactTier lightImpact = new ImpactTier(new HitstopTier(6f * Frame, 5f * Frame), 0.1f, 0.06f, 0.03f);
    public ImpactTier mediumImpact = new ImpactTier(new HitstopTier(8f * Frame, 7f * Frame), 0.16f, 0.14f, 0.05f);
    public ImpactTier heavyImpact = new ImpactTier(new HitstopTier(11f * Frame, 9f * Frame), 0.2f, 0.2f, 0.08f);
    public ImpactTier launcherImpact = new ImpactTier(new HitstopTier(12f * Frame, 10f * Frame), 0.22f, 0.28f, 0.1f);
    public ImpactTier bounceImpact = new ImpactTier(new HitstopTier(8f * Frame, 8f * Frame), 0.18f, 0.22f, 0.08f);
    [Tooltip("Extra defender freeze on a counter hit.")]
    public float counterHitstopBonus = 4f * Frame;
    [Range(0f, 1f)] public float blockedHitstopRatio = 0.6f;
    [Range(0f, 1f)] public float blockedShakeRatio = 0.5f;
    [Tooltip("Field-of-view kick on heavy and counter hits, in degrees (negative tightens).")]
    public float heavyImpactZoomPunch = -1.5f;

    [Header("Knockout")]
    public HitstopTier knockoutHitstop = new HitstopTier(27f * Frame, 27f * Frame);
    public float knockoutShakeDuration = 0.4f;
    public float knockoutShakeMagnitude = 0.35f;
    public float knockoutZoomPunch = -2.5f;
    [Tooltip("Game speed during the knockout beat.")]
    [Range(0.05f, 1f)] public float knockoutSlowMotionScale = 0.25f;
    public float knockoutSlowMotionDuration = 0.6f;

    [Header("Air Mobility")]
    [Tooltip("Horizontal speed of an air dash. Gravity is frozen for its duration.")]
    public float airDashSpeed = 9f;
    public float airDashDuration = 0.2f;
    [Tooltip("Frames into an air dash before an air normal may interrupt it.")]
    public float airDashAttackDelay = 3f * Frame;
    [Tooltip("Fraction of the dash speed carried into the fall once the dash ends.")]
    [Range(0f, 1f)] public float airDashCarryRatio = 0.5f;
    public int maxAirDashesPerJump = 1;
    [Tooltip("Height above the floor below which no air action may start, so a jump never dashes off the ground.")]
    public float airActionMinHeight = 0.3f;
    [Tooltip("Vertical speed of the super jump a jump cancel grants, tuned to follow a launched body.")]
    public float jumpCancelForce = 15f;
    [Tooltip("Recovery after landing during an air attack before control returns.")]
    public float airLandingRecovery = 4f * Frame;

    [Header("Combo Ergonomics")]
    [Tooltip("Furthest the attacker steps forward during the startup of a chained normal to bring the target back into reach.")]
    public float comboMicroStepMax = 0.7f;
    [Tooltip("Depth inside the attack's reach the micro-step aims for, so the strike lands on the body rather than on its edge.")]
    public float comboMicroStepMargin = 0.15f;
    [Tooltip("No micro-step when the target is further than this; a dropped combo at range stays dropped.")]
    public float comboMicroStepRange = 2.5f;
    [Tooltip("Times the same normal may chain into itself in one string (rapid jabs).")]
    public int rapidChainLimit = 2;

    [Header("Dash Attacks")]
    [Tooltip("Frames into a ground dash before a normal may interrupt it.")]
    public float dashAttackDelay = 3f * Frame;
    [Tooltip("Fraction of the forward dash speed carried into a normal started during the dash, decaying to zero by the end of its active frames.")]
    [Range(0f, 1f)] public float dashAttackCarryRatio = 0.6f;

    [Header("Telegraphs")]
    [Tooltip("Body tint pulsed while the fighter cannot be hit (wake-up, reversal startup).")]
    public Color invulnerableFlashColor = new Color(0.92f, 0.96f, 1f);
    [Tooltip("Pulses per second of the invulnerability tint.")]
    public float invulnerableFlashFrequency = 12f;
    [Tooltip("Shortest time the tint stays visible, so a six-frame wake-up still reads.")]
    public float invulnerableFlashMinDuration = 0.15f;
    [Tooltip("Body tint popped when a strike is deflected by an invulnerable or downed body.")]
    public Color deflectFlashColor = new Color(0.8f, 0.85f, 0.9f);
    public float deflectFlashDuration = 0.12f;
    [Tooltip("Camera kick on a deflected strike; no hitstop is applied since nothing connected.")]
    public float deflectCameraPunch = 0.04f;
    [Tooltip("Seconds the model takes to visually turn around after the gameplay facing snaps on a cross-up.")]
    public float turnaroundDuration = 0.12f;

    [Header("Input Buffer")]
    [Tooltip("Frames an input stays valid once the fighter becomes actionable.")]
    public float linkLeniency = 5f * Frame;
    [Tooltip("A press older than this when the fighter becomes actionable is discarded, so mashing through a long stun never stores a stale input.")]
    public float holdThrough = 0.5f;
    [Tooltip("Both kicks pressed within this window buffer a dash instead of a kick.")]
    public float dashMacroWindow = 2f * Frame;

    public float ComboScaling(int hitNumber)
    {
        return Mathf.Max(Mathf.Pow(comboScalingFactor, hitNumber - 1), minComboScaling);
    }

    public float HitstunScale(int hitNumber)
    {
        return Mathf.Max(1f - hitstunDecayPerHit * (hitNumber - 1), minHitstunScale);
    }

    public float GravityScale(int juggleHits)
    {
        return 1f + juggleGravityPerHit * juggleHits;
    }

    public float RelaunchScale(int juggleHits)
    {
        return Mathf.Max(Mathf.Pow(relaunchDecay, juggleHits), minRelaunchScale);
    }

    public ImpactTier ImpactFor(AttackWeight weight)
    {
        switch (weight)
        {
            case AttackWeight.Light: return lightImpact;
            case AttackWeight.Heavy: return heavyImpact;
            default: return mediumImpact;
        }
    }
}
