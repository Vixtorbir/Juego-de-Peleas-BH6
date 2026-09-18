using UnityEngine;

/// <summary>
/// Base class for every fighter state. States are self-contained behaviours that read from
/// the owning <see cref="RobotController"/> and request transitions through
/// <see cref="RobotController.ChangeState"/>.
/// </summary>
public abstract class RobotState
{
    protected const float Gravity = -35f;
    protected const float GroundSnapVelocity = -0.5f;
    protected const float FallVelocity = -9.81f;

    protected readonly RobotController robot;

    protected RobotState(RobotController robot)
    {
        this.robot = robot;
    }

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
    /// Special cancel: once a cancelable normal has connected (hit or block), a buffered special
    /// command interrupts the remaining active and recovery frames. Returns true on transition.
    /// </summary>
    protected bool TrySpecialCancel(AttackData data)
    {
        if (data == null || !data.specialCancelable || !robot.AttackConnected) return false;
        return TryConsumeSpecialCommands();
    }

    /// <summary>
    /// Chain cancel: once the attack has connected, the first <see cref="ComboBranch"/> whose
    /// command is buffered replaces the remaining frames with the next attack. Returns true on transition.
    /// </summary>
    protected bool TryChainCancel(AttackData data)
    {
        if (data == null || data.comboBranches == null || !robot.AttackConnected) return false;

        for (int i = 0; i < data.comboBranches.Count; i++)
        {
            ComboBranch branch = data.comboBranches[i];
            if (branch.nextAttack == null || !robot.TryConsumeCommand(branch.inputCommand)) continue;

            robot.ChangeState(new AttackState(robot, branch.nextAttack));
            return true;
        }

        return false;
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
            robot.ChangeState(new SpecialAttackState(robot, robot.hadoukenData, robot.Projectiles, robot.hadoukenSpawnPoint));
            return true;
        }

        if (robot.TryConsumeCommand(FighterCommand.Shoryuken))
        {
            robot.ChangeState(new ShoryukenState(robot, robot.shoryukenData));
            return true;
        }

        return false;
    }

    protected bool TryConsumeDashCommands()
    {
        if (robot.TryConsumeCommand(FighterCommand.DashForward))
        {
            robot.ChangeState(new DashState(robot, true));
            return true;
        }

        if (robot.TryConsumeCommand(FighterCommand.DashBackward))
        {
            robot.ChangeState(new DashState(robot, false));
            return true;
        }

        return false;
    }
}
