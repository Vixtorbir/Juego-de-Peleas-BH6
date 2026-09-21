using UnityEngine;

/// <summary>
/// Base class for every fighter state. States are allocated once by the owning
/// <see cref="RobotController"/> and re-used for the whole match: parameters are written through
/// a state's Configure method and transitions go through the controller's typed entry points, so
/// no state object is ever created while the match is running.
/// </summary>
public abstract class RobotState
{
    protected const float Gravity = -35f;
    protected const float GroundSnapVelocity = -0.5f;
    protected const float FallVelocity = -9.81f;
    protected const float CrouchInputThreshold = -0.5f;

    protected readonly RobotController robot;

    protected RobotState(RobotController robot)
    {
        this.robot = robot;
    }

    /// <summary>
    /// True while the fighter can raise a guard. Reactions and attacks return false, so a hit
    /// confirmed during hitstun or recovery can never be blocked by holding back.
    /// </summary>
    public virtual bool CanGuard => false;

    /// <summary>
    /// True while the collision capsule is lowered. Guard height is resolved from this posture
    /// rather than from the raw stick, so a standing fighter holding down is still hit high.
    /// </summary>
    public virtual bool IsCrouchingPosture => false;

    /// <summary>
    /// True in the neutral stances. Entering a neutral state ends the opponent's combo, and only a
    /// neutral state may start a fresh action from the buffer.
    /// </summary>
    public virtual bool IsNeutral => false;

    /// <summary>
    /// True while a buffered command could be consumed this frame: the neutral stances, and an
    /// attack whose cancel window is open. The input buffer only ages during these frames, so a
    /// press made during recovery or stun executes on the first actionable frame.
    /// </summary>
    public virtual bool AcceptsCommands => IsNeutral;

    /// <summary>True while grounded hits pass through the body (knockdown lockout).</summary>
    public virtual bool IgnoresGroundedHits => false;

    public virtual void Enter() { }

    public virtual void Tick() { }

    public virtual void Exit() { }

    /// <summary>
    /// Plays an Animator state directly with a short fixed cross-fade. Attack and reaction states
    /// are entered this way instead of through trigger parameters: a trigger can be swallowed by a
    /// transition already in flight or by interruption rules, whereas a cross-fade always starts
    /// the requested state from its first frame, including when re-entering the same state.
    /// </summary>
    protected void PlayAnimationState(int stateHash)
    {
        if (robot.animator == null) return;
        robot.animator.CrossFadeInFixedTime(stateHash, FighterAnimatorParameters.EntryCrossFadeDuration, 0, 0f);
    }

    /// <summary>
    /// Plays the attack's state at its authored pace. The hitbox timing comes from the frame data,
    /// which is calibrated to the clip's real extension frame, so the swing and the hit line up
    /// without stretching or compressing the motion.
    /// </summary>
    protected void PlayAttackAnimation(AttackData data)
    {
        if (robot.animator == null || data == null || data.AnimationStateHash == 0) return;

        robot.animator.SetFloat(FighterAnimatorParameters.AttackSpeed, data.animationSpeed);
        PlayAnimationState(data.AnimationStateHash);
    }

    /// <summary>
    /// Settles the fighter back into a stance state when control returns from a one-shot clip.
    /// Skipped when the animator is already there so an idle never restarts its own blend.
    /// </summary>
    protected void ReturnToStance(int stanceStateHash)
    {
        if (robot.animator == null) return;
        if (robot.animator.GetCurrentAnimatorStateInfo(0).shortNameHash == stanceStateHash) return;
        if (robot.animator.GetNextAnimatorStateInfo(0).shortNameHash == stanceStateHash) return;

        robot.animator.CrossFadeInFixedTime(stanceStateHash, FighterAnimatorParameters.ReturnCrossFadeDuration, 0, 0f);
    }

    /// <summary>
    /// Plays a state whose clip is rescaled to end exactly when the gameplay window (stun, dash)
    /// ends. A null clip or non-positive duration plays the clip at its authored speed.
    /// </summary>
    protected void PlayTimedAnimation(int stateHash, AnimationClip clip, float duration)
    {
        if (robot.animator == null) return;

        robot.animator.SetFloat(FighterAnimatorParameters.TimedSpeed, FighterAnimatorParameters.FitPlaybackSpeed(clip, duration));
        PlayAnimationState(stateHash);
    }

    /// <summary>
    /// Snaps to a reaction state and evaluates the graph at once, so the hit pose is on screen for
    /// the whole hitstop even though the animator is paused the same frame. A cross-fade would never
    /// start under a paused animator and the defender would freeze in its pre-hit pose.
    /// </summary>
    protected void PlayReactionAnimation(int stateHash, AnimationClip clip, float duration)
    {
        if (robot.animator == null) return;

        robot.animator.SetFloat(FighterAnimatorParameters.TimedSpeed, FighterAnimatorParameters.FitPlaybackSpeed(clip, duration));
        robot.animator.Play(stateHash, 0, 0f);
        robot.animator.Update(0f);
    }

    /// <summary>
    /// Special cancel: once a cancelable normal has connected (hit or block), a buffered special
    /// command interrupts the remaining active and recovery frames. Returns true on transition.
    /// </summary>
    protected bool TrySpecialCancel(AttackData data)
    {
        if (data == null || !data.specialCancelable || !robot.AttackConnected) return false;
        return TryConsumeSpecialCommands();
    }

    /// <summary>
    /// Chain cancel. Explicit <see cref="ComboBranch"/> links (target combos) are checked first;
    /// otherwise the universal gatling rule lets a connected normal chain into any normal of a
    /// strictly higher tier: Light > Medium > Heavy > Launcher. Returns true on transition.
    /// </summary>
    protected bool TryChainCancel(AttackData data)
    {
        if (data == null || !robot.AttackConnected) return false;

        if (data.comboBranches != null)
        {
            for (int i = 0; i < data.comboBranches.Count; i++)
            {
                ComboBranch branch = data.comboBranches[i];
                if (branch.nextAttack == null || !robot.TryConsumeCommand(branch.inputCommand)) continue;

                EnterNormal(branch.nextAttack);
                return true;
            }
        }

        FighterCommand command = robot.bufferedCommand;
        bool crouching = robot.MovementInput.y < CrouchInputThreshold;
        AttackData next = robot.ResolveNormal(command, crouching);
        if (next == null || next.GatlingTier <= data.GatlingTier || !robot.TryConsumeCommand(command)) return false;

        EnterNormal(next);
        return true;
    }

    /// <summary>
    /// Vertical velocity that keeps a grounded fighter pressed against the floor so
    /// CharacterController.isGrounded stays reliable, or lets it fall if the ground disappears
    /// underneath. States that already integrate their own horizontal motion fold this into the
    /// same Move call.
    /// </summary>
    protected float GroundingVelocity => robot.characterController.isGrounded ? GroundSnapVelocity : FallVelocity;

    protected void ApplyGroundingForce()
    {
        robot.characterController.Move(new Vector3(0f, GroundingVelocity * Time.deltaTime, 0f));
    }

    /// <summary>
    /// True on the frame a falling fighter touches the floor. The grounding force is applied on
    /// that same frame, so isGrounded holds through the state transition instead of flickering
    /// while the next state takes over.
    /// </summary>
    protected bool HasLanded(float verticalVelocity)
    {
        if (verticalVelocity >= 0f || !robot.characterController.isGrounded) return false;

        ApplyGroundingForce();
        return true;
    }

    /// <summary>
    /// Special moves take priority over normals in every neutral state. Returns true if a transition occurred.
    /// </summary>
    protected bool TryConsumeSpecialCommands()
    {
        if (robot.TryConsumeCommand(FighterCommand.Hadouken))
        {
            robot.EnterSpecialAttack(robot.hadoukenData);
            return true;
        }

        if (robot.TryConsumeCommand(FighterCommand.Shoryuken))
        {
            robot.EnterShoryuken(robot.shoryukenData);
            return true;
        }

        return false;
    }

    protected bool TryConsumeDashCommands()
    {
        if (robot.TryConsumeCommand(FighterCommand.DashForward))
        {
            robot.EnterDash(true);
            return true;
        }

        if (robot.TryConsumeCommand(FighterCommand.DashBackward))
        {
            robot.EnterDash(false);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Starts the normal bound to the buffered attack button for the given posture. Returns true
    /// on transition.
    /// </summary>
    protected bool TryConsumeNormalCommands(bool crouching)
    {
        FighterCommand command = robot.bufferedCommand;
        AttackData attack = robot.ResolveNormal(command, crouching);
        if (attack == null || !robot.TryConsumeCommand(command)) return false;

        EnterNormal(attack);
        return true;
    }

    /// <summary>Crouching normals keep the capsule lowered for their whole duration; everything else stands.</summary>
    private void EnterNormal(AttackData attack)
    {
        if (robot.IsCrouchingNormal(attack)) robot.EnterCrouchAttack(attack);
        else robot.EnterAttack(attack);
    }
}
