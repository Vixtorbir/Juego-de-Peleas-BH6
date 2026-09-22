using System.Collections.Generic;
using UnityEngine;

public enum AttackHeight
{
    High,
    Mid,
    Low
}

public enum AttackWeight
{
    Light,
    Medium,
    Heavy
}

public enum AttackImpactSound
{
    Punch,
    Kick,
    Energy
}

public enum HitReaction
{
    Hitstun,
    Launch,
    WallBounce,
    GroundBounce,
    Knockdown
}

[System.Serializable]
public struct ComboBranch
{
    public FighterCommand inputCommand;
    public AttackData nextAttack;
}

[CreateAssetMenu(fileName = "NewAttackData", menuName = "Fighting Game/Attack Data")]
public class AttackData : ScriptableObject
{
    [Header("Animation")]
    [Tooltip("Name of the Animator state played for this attack.")]
    public string animationTrigger = "LeftPunch";
    [Tooltip("Clip played for this attack at its authored pace. Frame data below is tuned to the clip, never the other way round.")]
    public AnimationClip animationClip;
    [Tooltip("Playback multiplier kept within a human range; use it for a slightly snappier light attack, not to fit a slow clip.")]
    [Range(1f, 1.15f)] public float animationSpeed = 1f;
    [Tooltip("Animator layer that owns this attack's state. Aerial normals use the masked upper-body layer so the jump keeps the legs.")]
    public int animationLayer;

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
    [Tooltip("On connect, holding up cancels the remaining frames into a super jump so the attacker can chase a launched opponent.")]
    public bool jumpCancelable = false;
    [Tooltip("On connect, a buffered dash cancels the remaining frames into the dash.")]
    public bool dashCancelable = false;
    [Tooltip("The fighter ignores incoming hits during the startup frames. Reserved for reversal specials.")]
    public bool invincibleStartup = false;

    [Header("Guard & Block Settings")]
    public AttackHeight attackHeight = AttackHeight.Mid;
    [Tooltip("Must be blocked standing: a crouching guard is broken. Jumping attacks and the overhead command normal set this.")]
    public bool isOverhead = false;
    public float blockstunDuration = 0.25f;

    [Header("Pushback Settings")]
    public float hitPushback = 8f;
    public float blockPushback = 4f;

    [Header("Launcher Settings")]
    [Tooltip("Marks the launcher tier of the gatling ladder (Light > Medium > Heavy > Launcher) and defaults the grounded reaction to a launch.")]
    public bool isLauncher = false;
    public float launchForce = 15f;

    [Header("Aerial")]
    [Tooltip("Performed from a jump: the attacker keeps falling through the swing and lands into a short recovery.")]
    public bool isAirAttack = false;

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

    public float Reach => hitboxOffset.z + hitboxSize.z * 0.5f;

    public float ActiveEndTime => startupTime + activeTime;
    public float TotalDuration => startupTime + activeTime + recoveryTime;

    public float CancelWindowEnd => cancelWindowEnd > 0f ? cancelWindowEnd : TotalDuration;

    // Light 0, Medium 1, Heavy 2, Launcher 3. A normal can chain into any higher tier.
    public int GatlingTier => isLauncher ? 3 : (int)weight;

    public HitReaction ResolvedGroundedReaction => groundedReaction == HitReaction.Hitstun && isLauncher ? HitReaction.Launch : groundedReaction;
}
