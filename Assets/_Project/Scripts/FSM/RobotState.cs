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

    /// <summary>Layer holding the full-body graph; the stances always settle back onto it.</summary>
    protected const int BaseAnimationLayer = 0;

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
    /// True while the state is open to buffered commands at all: the neutral stances, an attack
    /// whose cancel window is open, a dash past its attack delay, a jump above the air-action height.
    /// </summary>
    public virtual bool AcceptsCommands => IsNeutral;

    /// <summary>
    /// True when <paramref name="command"/> belongs to this state's vocabulary right now. The input
    /// buffer only ages while the head command is consumable, so a press the current state cannot
    /// act on (a special during a jump, a normal during recovery) is held for the first state that
    /// can, within the hold-through limit. Neutral stances consume everything.
    /// </summary>
    public virtual bool CanConsume(FighterCommand command) => AcceptsCommands;

    protected static bool IsAttackButton(FighterCommand command)
    {
        switch (command)
        {
            case FighterCommand.LeftPunch:
            case FighterCommand.RightPunch:
            case FighterCommand.LeftKick:
            case FighterCommand.RightKick:
            case FighterCommand.Launcher:
            case FighterCommand.Overhead:
            case FighterCommand.AntiAir:
                return true;
            default:
                return false;
        }
    }

    protected static bool IsSpecialCommand(FighterCommand command)
    {
        return command == FighterCommand.Hadouken || command == FighterCommand.Shoryuken;
    }

    protected static bool IsDashCommand(FighterCommand command)
    {
        return command == FighterCommand.DashForward || command == FighterCommand.DashBackward;
    }

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
    /// Aerial normals pass their own layer, which masks the swing onto the jump the base layer holds.
    /// </summary>
    protected void PlayAnimationState(int stateHash, int layer = BaseAnimationLayer)
    {
        if (robot.animator == null) return;
        robot.animator.CrossFadeInFixedTime(stateHash, FighterAnimatorParameters.EntryCrossFadeDuration, layer, 0f);
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
        PlayAnimationState(data.AnimationStateHash, data.animationLayer);
    }

    /// <summary>
    /// Settles the fighter back into a stance state when control returns from a one-shot clip.
    /// Skipped when the animator is already there so an idle never restarts its own blend.
    /// </summary>
    protected void ReturnToStance(int stanceStateHash)
    {
        if (robot.animator == null) return;
        if (robot.animator.GetCurrentAnimatorStateInfo(BaseAnimationLayer).shortNameHash == stanceStateHash) return;
        if (robot.animator.GetNextAnimatorStateInfo(BaseAnimationLayer).shortNameHash == stanceStateHash) return;

        robot.animator.CrossFadeInFixedTime(stanceStateHash, FighterAnimatorParameters.ReturnCrossFadeDuration, BaseAnimationLayer, 0f);
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
        robot.animator.Play(stateHash, BaseAnimationLayer, 0f);
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
    /// strictly higher tier: Light > Medium > Heavy > Launcher on the ground, j.L > j.M > j.H in
    /// the air. Returns true on transition.
    /// </summary>
    protected bool TryChainCancel(AttackData data, bool airborne)
    {
        if (data == null || !robot.AttackConnected) return false;

        if (data.comboBranches != null)
        {
            for (int i = 0; i < data.comboBranches.Count; i++)
            {
                ComboBranch branch = data.comboBranches[i];
                if (branch.nextAttack == null || robot.bufferedCommand != branch.inputCommand) continue;
                if (branch.nextAttack == data && !robot.TryUseRapidLink()) continue;

                robot.TryConsumeCommand(branch.inputCommand);
                EnterNormal(branch.nextAttack, true);
                return true;
            }
        }

        FighterCommand command = robot.bufferedCommand;
        bool crouching = robot.MovementInput.y < CrouchInputThreshold;
        AttackData next = airborne ? robot.ResolveAirNormal(command) : robot.ResolveNormal(command, crouching);
        if (next == null || next.GatlingTier <= data.GatlingTier || !robot.TryConsumeCommand(command)) return false;

        EnterNormal(next, true);
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

        EnterNormal(attack, false);
        return true;
    }

    /// <summary>Starts the jumping normal bound to the buffered attack button. Returns true on transition.</summary>
    protected bool TryConsumeAirNormalCommands()
    {
        FighterCommand command = robot.bufferedCommand;
        AttackData attack = robot.ResolveAirNormal(command);
        if (attack == null || !robot.TryConsumeCommand(command)) return false;

        robot.EnterAirAttack(attack);
        return true;
    }

    /// <summary>
    /// Consumes a buffered dash into an air dash while the jump still has one left. A dash pressed
    /// past the limit stays buffered and expires with the leniency window.
    /// </summary>
    protected bool TryConsumeAirDashCommands()
    {
        if (robot.AirDashesUsed >= robot.Rules.maxAirDashesPerJump) return false;

        if (robot.TryConsumeCommand(FighterCommand.DashForward))
        {
            robot.EnterAirDash(true);
            return true;
        }

        if (robot.TryConsumeCommand(FighterCommand.DashBackward))
        {
            robot.EnterAirDash(false);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Crouching normals keep the capsule lowered, jumping normals keep falling; everything else
    /// stands. <paramref name="chained"/> marks a chain cancel so the grounded attack may micro-step.
    /// </summary>
    private void EnterNormal(AttackData attack, bool chained)
    {
        if (attack.isAirAttack) robot.EnterAirAttack(attack);
        else if (robot.IsCrouchingNormal(attack)) robot.EnterCrouchAttack(attack, chained);
        else robot.EnterAttack(attack, chained);
    }
}
