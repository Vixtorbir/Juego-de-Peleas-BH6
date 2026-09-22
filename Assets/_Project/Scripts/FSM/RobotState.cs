using UnityEngine;

public abstract class RobotState
{
    protected const float Gravity = -35f;
    protected const float GroundSnapVelocity = -0.5f;
    protected const float FallVelocity = -9.81f;
    protected const float CrouchInputThreshold = -0.5f;

    protected const int BaseAnimationLayer = 0;

    protected readonly RobotController robot;

    protected RobotState(RobotController robot)
    {
        this.robot = robot;
    }

    public virtual bool CanGuard => false;

    public virtual bool IsCrouchingPosture => false;

    public virtual bool IsNeutral => false;

    public virtual bool AcceptsCommands => IsNeutral;

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
        return command == FighterCommand.Hadouken
            || command == FighterCommand.Shoryuken
            || command == FighterCommand.SuperArt;
    }

    protected static bool IsDashCommand(FighterCommand command)
    {
        return command == FighterCommand.DashForward || command == FighterCommand.DashBackward;
    }

    public virtual bool IgnoresGroundedHits => false;

    public virtual void Enter() { }

    public virtual void Tick() { }

    public virtual void Exit() { }

    // Play states directly instead of using triggers - a trigger can get eaten by a transition already in progress.
    protected void PlayAnimationState(int stateHash, int layer = BaseAnimationLayer)
    {
        if (robot.animator == null) return;
        robot.animator.CrossFadeInFixedTime(stateHash, FighterAnimatorParameters.EntryCrossFadeDuration, layer, 0f);
    }

    protected void PlayAttackAnimation(AttackData data)
    {
        if (robot.animator == null || data == null || data.AnimationStateHash == 0) return;

        robot.animator.SetFloat(FighterAnimatorParameters.AttackSpeed, data.animationSpeed);
        PlayAnimationState(data.AnimationStateHash, data.animationLayer);
    }

    protected void ReturnToStance(int stanceStateHash)
    {
        if (robot.animator == null) return;
        if (robot.animator.GetCurrentAnimatorStateInfo(BaseAnimationLayer).shortNameHash == stanceStateHash) return;
        if (robot.animator.GetNextAnimatorStateInfo(BaseAnimationLayer).shortNameHash == stanceStateHash) return;

        robot.animator.CrossFadeInFixedTime(stanceStateHash, FighterAnimatorParameters.ReturnCrossFadeDuration, BaseAnimationLayer, 0f);
    }

    protected void PlayTimedAnimation(int stateHash, AnimationClip clip, float duration)
    {
        if (robot.animator == null) return;

        robot.animator.SetFloat(FighterAnimatorParameters.TimedSpeed, FighterAnimatorParameters.FitPlaybackSpeed(clip, duration));
        PlayAnimationState(stateHash);
    }

    // Snap + Update(0) so the hit pose is visible during hitstop (a cross-fade never starts under a paused animator).
    protected void PlayReactionAnimation(int stateHash, AnimationClip clip, float duration)
    {
        if (robot.animator == null) return;

        robot.animator.SetFloat(FighterAnimatorParameters.TimedSpeed, FighterAnimatorParameters.FitPlaybackSpeed(clip, duration));
        robot.animator.Play(stateHash, BaseAnimationLayer, 0f);
        robot.animator.Update(0f);
    }

    protected bool TrySpecialCancel(AttackData data)
    {
        if (data == null || !data.specialCancelable || !robot.AttackConnected) return false;
        return TryConsumeSpecialCommands();
    }

    // Explicit combo branches first, then the gatling rule (chain into any higher tier).
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

    protected float GroundingVelocity => robot.characterController.isGrounded ? GroundSnapVelocity : FallVelocity;

    protected void ApplyGroundingForce()
    {
        robot.characterController.Move(new Vector3(0f, GroundingVelocity * Time.deltaTime, 0f));
    }

    // Apply the grounding force on the landing frame so isGrounded does not flicker across the transition.
    protected bool HasLanded(float verticalVelocity)
    {
        if (verticalVelocity >= 0f || !robot.characterController.isGrounded) return false;

        ApplyGroundingForce();
        return true;
    }

    protected bool TryConsumeSpecialCommands()
    {
        if (TryConsumeSuperArtCommand()) return true;

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

    private bool TryConsumeSuperArtCommand()
    {
        if (robot.bufferedCommand != FighterCommand.SuperArt) return false;
        if (!robot.TryEnterSuperArt()) return false;

        robot.TryConsumeCommand(FighterCommand.SuperArt);
        return true;
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

    protected bool TryConsumeNormalCommands(bool crouching)
    {
        FighterCommand command = robot.bufferedCommand;
        AttackData attack = robot.ResolveNormal(command, crouching);
        if (attack == null || !robot.TryConsumeCommand(command)) return false;

        EnterNormal(attack, false);
        return true;
    }

    protected bool TryConsumeAirNormalCommands()
    {
        FighterCommand command = robot.bufferedCommand;
        AttackData attack = robot.ResolveAirNormal(command);
        if (attack == null || !robot.TryConsumeCommand(command)) return false;

        robot.EnterAirAttack(attack);
        return true;
    }

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

    private void EnterNormal(AttackData attack, bool chained)
    {
        if (attack.isAirAttack) robot.EnterAirAttack(attack);
        else if (robot.IsCrouchingNormal(attack)) robot.EnterCrouchAttack(attack, chained);
        else robot.EnterAttack(attack, chained);
    }
}
