using UnityEngine;

/// <summary>
/// Single source of truth for the fighter Animator parameter and state names. The editor graph
/// builder consumes the string names; runtime code uses the pre-hashed ids so no string hashing
/// happens while the match is running. Trigger-style states (jump, dash, reactions, attacks) are
/// entered by cross-fading to the state hash, so each state name below is also its entry key.
/// </summary>
public static class FighterAnimatorParameters
{
    public const string InputXName = "InputX";
    public const string InputYName = "InputY";
    public const string IsGroundedName = "IsGrounded";
    public const string IsCrouchingName = "IsCrouching";
    public const string AttackSpeedName = "AttackSpeed";
    public const string TimedSpeedName = "TimedSpeed";

    public const string JumpTrigger = "Jump";
    public const string DashForwardTrigger = "DashForward";
    public const string DashBackwardTrigger = "DashBackward";
    public const string HitstunTrigger = "Hitstun";
    public const string BlockstunTrigger = "Blockstun";
    public const string DeathTrigger = "Death";

    public const string LocomotionStateName = "Locomotion";

    public const string CrouchStateName = "Crouch";

    /// <summary>Fixed cross-fade length used when entering any attack, movement or reaction state.</summary>
    public const float EntryCrossFadeDuration = 0.05f;

    /// <summary>Cross-fade used when control returns and the fighter settles back into its stance.</summary>
    public const float ReturnCrossFadeDuration = 0.1f;

    /// <summary>Upper bound for reaction and dash clips; attacks always play at their authored speed.</summary>
    public const float MaxReactionSpeed = 1.5f;

    public static readonly int InputX = Animator.StringToHash(InputXName);
    public static readonly int InputY = Animator.StringToHash(InputYName);
    public static readonly int IsGrounded = Animator.StringToHash(IsGroundedName);
    public static readonly int IsCrouching = Animator.StringToHash(IsCrouchingName);
    public static readonly int AttackSpeed = Animator.StringToHash(AttackSpeedName);
    public static readonly int TimedSpeed = Animator.StringToHash(TimedSpeedName);

    public static readonly int Jump = Animator.StringToHash(JumpTrigger);
    public static readonly int DashForward = Animator.StringToHash(DashForwardTrigger);
    public static readonly int DashBackward = Animator.StringToHash(DashBackwardTrigger);
    public static readonly int Hitstun = Animator.StringToHash(HitstunTrigger);
    public static readonly int Blockstun = Animator.StringToHash(BlockstunTrigger);
    public static readonly int Death = Animator.StringToHash(DeathTrigger);

    public static readonly int LocomotionState = Animator.StringToHash(LocomotionStateName);
    public static readonly int CrouchState = Animator.StringToHash(CrouchStateName);

    /// <summary>
    /// Playback speed for a reaction or dash clip so it covers the gameplay window without ever
    /// slowing below natural speed or racing past <see cref="MaxReactionSpeed"/>. A clip longer
    /// than the window is simply cut when control returns.
    /// </summary>
    public static float FitPlaybackSpeed(AnimationClip clip, float gameplayDuration)
    {
        if (clip == null || gameplayDuration <= 0f || clip.length <= 0f) return 1f;
        return Mathf.Clamp(clip.length / gameplayDuration, 1f, MaxReactionSpeed);
    }
}
