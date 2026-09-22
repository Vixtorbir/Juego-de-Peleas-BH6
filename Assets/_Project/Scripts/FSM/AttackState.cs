using UnityEngine;

// Startup -> active -> recovery from AttackData. Cancels open as soon as the attack connects (hit or block).
public class AttackState : RobotState
{
    private const float JumpCancelInputThreshold = 0.6f;

    private AttackData data;
    private bool chained;
    private float stateTimer;
    private bool hitboxActive;
    private float driftSpeed;
    private float driftEndTime;
    private float microStepBudget;

    public AttackState(RobotController robot) : base(robot) { }

    public AttackData Data => data;

    public override bool AcceptsCommands => IsCancelWindowOpen;

    public override bool CanConsume(FighterCommand command)
    {
        if (!AcceptsCommands) return false;
        if (IsAttackButton(command)) return true;
        if (IsSpecialCommand(command)) return data.specialCancelable;
        return IsDashCommand(command) && data.dashCancelable;
    }

    protected float StateTimer => stateTimer;

    private bool IsCancelWindowOpen => data != null && robot.AttackConnected && stateTimer >= data.startupTime && stateTimer < data.CancelWindowEnd;

    public void Configure(AttackData attackData) => Configure(attackData, false);

    public void Configure(AttackData attackData, bool chainedEntry)
    {
        data = attackData;
        chained = chainedEntry;
    }

    public override void Enter()
    {
        stateTimer = 0f;
        hitboxActive = false;
        robot.IsInAttackStartup = true;
        robot.AttackConnected = false;
        ConfigureDrift();
        PlayAttackAnimation(data);
    }

    public override void Tick()
    {
        stateTimer += Time.deltaTime;
        if (IntegrateMotion()) return;

        if (data == null)
        {
            ExitToStance();
            return;
        }

        if (stateTimer < data.startupTime) return;

        robot.IsInAttackStartup = false;
        if (stateTimer < data.CancelWindowEnd && TryCancels()) return;

        if (stateTimer < data.ActiveEndTime)
        {
            if (!hitboxActive)
            {
                robot.hitboxBase.Activate(data);
                hitboxActive = true;
            }
            return;
        }

        DeactivateHitbox(true);

        if (stateTimer >= data.TotalDuration)
        {
            ExitToStance();
        }
    }

    public override void Exit()
    {
        robot.IsInAttackStartup = false;
        robot.hitboxBase.Deactivate();
    }

    protected virtual bool IntegrateMotion()
    {
        float forward = CurrentDriftSpeed() * Time.deltaTime + MicroStepDistance(Time.deltaTime);
        if (forward <= 0f)
        {
            ApplyGroundingForce();
            return false;
        }

        Vector3 motion = new Vector3(robot.transform.forward.x * forward, GroundingVelocity * Time.deltaTime, 0f);
        robot.characterController.Move(motion);
        return false;
    }

    private void ConfigureDrift()
    {
        driftSpeed = 0f;
        driftEndTime = 0f;
        microStepBudget = 0f;
        if (data == null) return;

        float momentum = robot.PendingAttackMomentum;
        robot.PendingAttackMomentum = 0f;

        if (momentum > 0f)
        {
            driftSpeed = momentum;
            driftEndTime = data.ActiveEndTime;
            return;
        }

        if (chained && robot.opponentTarget != null) microStepBudget = robot.Rules.comboMicroStepMax;
    }

    private float CurrentDriftSpeed()
    {
        if (driftSpeed <= 0f || stateTimer >= driftEndTime) return 0f;
        return driftSpeed * (1f - stateTimer / driftEndTime);
    }

    // Chained normals step in during startup if pushback carried the attacker out of range.
    private float MicroStepDistance(float deltaTime)
    {
        if (microStepBudget <= 0f || data == null || stateTimer >= data.startupTime) return 0f;

        CombatRules rules = robot.Rules;
        float distance = Mathf.Abs(robot.opponentTarget.position.x - robot.transform.position.x);
        if (distance > rules.comboMicroStepRange) return 0f;

        float gap = distance - (data.Reach + robot.OpponentBodyRadius - rules.comboMicroStepMargin);
        if (gap <= 0f) return 0f;

        float maxThisFrame = rules.comboMicroStepMax / data.startupTime * deltaTime;
        float step = Mathf.Min(gap, maxThisFrame, microStepBudget);
        microStepBudget -= step;
        return step;
    }

    // Priority: special > jump > chain > dash.
    protected virtual bool TryCancels()
    {
        if (TrySpecialCancel(data)) return true;
        if (TryJumpCancel()) return true;
        if (TryChainCancel(data, false)) return true;
        return TryDashCancel();
    }

    protected virtual void ExitToStance()
    {
        robot.EnterIdle();
    }

    protected void DeactivateHitbox(bool reportWhiff)
    {
        if (!hitboxActive) return;

        robot.hitboxBase.Deactivate();
        hitboxActive = false;

        if (reportWhiff && !robot.AttackConnected && AudioManager.Instance != null) AudioManager.Instance.PlayWhiff();
    }

    private bool TryJumpCancel()
    {
        if (!data.jumpCancelable || !robot.AttackConnected || robot.MovementInput.y <= JumpCancelInputThreshold) return false;

        robot.EnterJump(robot.Rules.jumpCancelForce);
        return true;
    }

    private bool TryDashCancel()
    {
        if (!data.dashCancelable || !robot.AttackConnected) return false;
        return TryConsumeDashCommands();
    }
}
