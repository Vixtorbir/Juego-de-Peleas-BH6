using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Guard height an attack must be blocked at. High attacks whiff on crouching opponents,
/// Mid attacks are blocked standing only, Low attacks are blocked crouching only.
/// </summary>
public enum AttackHeight
{
    High,
    Mid,
    Low
}

/// <summary>
/// Impact class of an attack. Drives hitstop length and screen shake so light pokes feel crisp
/// and heavy hits feel weighty, independently of the raw damage number.
/// </summary>
public enum AttackWeight
{
    Light,
    Medium,
    Heavy
}

/// <summary>
/// How the timing calibrator reads the strike out of the clip. Extension strikes connect just
/// before the limb is fully out; FullExtension strikes (projectile releases) fire at the furthest
/// point; Rising strikes go live almost immediately and stay live until the limb tops out.
/// </summary>
public enum StrikeTiming
{
    Extension,
    FullExtension,
    Rising
}

/// <summary>
/// Which impact family the attack plays when it lands. Punches additionally split by weight.
/// </summary>
public enum AttackImpactSound
{
    Punch,
    Kick,
    Energy
}

/// <summary>
/// What a connected hit does to the defender's body. Grounded and airborne defenders resolve their
/// own reaction, so a heavy kick can knock a standing opponent back but send an airborne one into
/// the wall.
/// </summary>
public enum HitReaction
{
    /// <summary>Standing stun with knockback; on an airborne defender, a relaunch.</summary>
    Hitstun,
    /// <summary>Launch into a juggle with the attack's launch force.</summary>
    Launch,
    /// <summary>Send the body flying into the nearest wall; it rebounds once per combo.</summary>
    WallBounce,
    /// <summary>Slam the body into the floor; it pops back up once per combo.</summary>
    GroundBounce,
    /// <summary>Hard knockdown: the defender cannot be hit while down and wakes up invulnerable.</summary>
    Knockdown
}

/// <summary>
/// Chain link from one attack to the next. Explicit links are target combos: they take priority
/// over the universal gatling rule and may chain within or below the current tier.
/// </summary>
[System.Serializable]
public struct ComboBranch
{
    public FighterCommand inputCommand;
    public AttackData nextAttack;
}

/// <summary>
/// Designer-authored frame data and hit properties for a single attack.
/// Timings are expressed in seconds so they remain frame-rate independent.
/// </summary>
[CreateAssetMenu(fileName = "NewAttackData", menuName = "Fighting Game/Attack Data")]
public class AttackData : ScriptableObject
{
    [Header("Animation")]
    [Tooltip("Name of the Animator state played for this attack. Also used as the state name when the controller is generated.")]
    public string animationTrigger = "LeftPunch";
    [Tooltip("Clip played for this attack at its authored pace. Frame data below is tuned to the clip, never the other way round.")]
    public AnimationClip animationClip;
    [Tooltip("Playback multiplier kept within a human range; use it for a slightly snappier light attack, not to fit a slow clip.")]
    [Range(1f, 1.15f)] public float animationSpeed = 1f;
    [Tooltip("Where in the swing the calibrator places the active frames.")]
    public StrikeTiming strikeTiming = StrikeTiming.Extension;

    [Header("Frame Data (seconds)")]
    public float startupTime = 0.15f;
    public float activeTime = 0.1f;
    public float recoveryTime = 0.25f;

    [Header("Hitbox Volume")]
    [Tooltip("Centre of the active volume in fighter space: X lateral, Y height from the feet, Z reach towards the opponent.")]
    public Vector3 hitboxOffset = new Vector3(0f, 1.4f, 0.95f);
    [Tooltip("Full extents of the active volume. Z is the depth of the strike along the reach axis.")]
    public Vector3 hitboxSize = new Vector3(0.5f, 0.4f, 0.8f);

    [Header("Combat Stats")]
    public float damage = 10f;
    public float hitstunDuration = 0.4f;
    public AttackWeight weight = AttackWeight.Medium;
    public AttackImpactSound impactSound = AttackImpactSound.Punch;

    [Header("Cancel & Invincibility")]
    [Tooltip("When the attack connects (hit or block), its active and recovery frames can be cancelled into a special move.")]
    public bool specialCancelable = true;
    [Tooltip("Seconds from the attack start after which chain and special cancels close. 0 keeps the window open to the end of recovery.")]
    public float cancelWindowEnd = 0f;
    [Tooltip("The fighter ignores incoming hits during the startup frames. Reserved for reversal specials.")]
    public bool invincibleStartup = false;

    [Header("Guard & Block Settings")]
    public AttackHeight attackHeight = AttackHeight.Mid;
    public float blockstunDuration = 0.25f;

    [Header("Pushback Settings")]
    public float hitPushback = 8f;
    public float blockPushback = 4f;

    [Header("Launcher Settings")]
    [Tooltip("Marks the launcher tier of the gatling ladder (Light > Medium > Heavy > Launcher) and defaults the grounded reaction to a launch.")]
    public bool isLauncher = false;
    public float launchForce = 15f;

    [Header("Hit Reactions")]
    [Tooltip("Reaction of a standing or crouching defender. Hitstun on a launcher resolves to Launch.")]
    public HitReaction groundedReaction = HitReaction.Hitstun;
    [Tooltip("Reaction of an airborne defender. Hitstun and Launch both relaunch, with juggle decay applied.")]
    public HitReaction airborneReaction = HitReaction.Hitstun;
    [Tooltip("Vertical speed of a ground-bounce pop or a wall-bounce rebound triggered by this attack.")]
    public float bounceForce = 9f;

    [Header("Combo Routes")]
    [Tooltip("Chain cancels available as soon as this attack connects.")]
    public List<ComboBranch> comboBranches;

    [System.NonSerialized] private int animationStateHash;
    [System.NonSerialized] private string hashedStateName;

    /// <summary>
    /// Animator state hash for <see cref="animationTrigger"/>, computed once per asset and refreshed
    /// only if the name is edited, so no string hashing happens during the match.
    /// </summary>
    public int AnimationStateHash
    {
        get
        {
            if (!ReferenceEquals(hashedStateName, animationTrigger))
            {
                hashedStateName = animationTrigger;
                animationStateHash = string.IsNullOrEmpty(animationTrigger) ? 0 : Animator.StringToHash(animationTrigger);
            }

            return animationStateHash;
        }
    }

    /// <summary>Furthest point of the hitbox from the fighter root along the facing axis.</summary>
    public float Reach => hitboxOffset.z + hitboxSize.z * 0.5f;

    public float ActiveEndTime => startupTime + activeTime;
    public float TotalDuration => startupTime + activeTime + recoveryTime;

    /// <summary>Time from the attack start at which its cancel window closes.</summary>
    public float CancelWindowEnd => cancelWindowEnd > 0f ? cancelWindowEnd : TotalDuration;

    /// <summary>
    /// Position on the gatling ladder: Light (0), Medium (1), Heavy (2), Launcher (3). A connected
    /// normal may chain into any normal of a strictly higher tier.
    /// </summary>
    public int GatlingTier => isLauncher ? 3 : (int)weight;

    /// <summary>Grounded reaction with the launcher flag folded in for assets authored before reactions existed.</summary>
    public HitReaction ResolvedGroundedReaction => groundedReaction == HitReaction.Hitstun && isLauncher ? HitReaction.Launch : groundedReaction;
}
