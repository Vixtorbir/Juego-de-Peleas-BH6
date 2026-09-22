using UnityEngine;

/// <summary>
/// Standing normal attack driven by <see cref="AttackData"/> frame data: startup (counter-hit
/// vulnerable), active (hitbox enabled) and recovery. The moment the attack connects, hit or
/// block, the cancel window opens: a buffered special interrupts into the special, holding up on a
/// jump-cancelable attack launches a super jump to chase the opponent, a buffered chain command
/// interrupts into the matching <see cref="ComboBranch"/> or the next gatling tier, and a buffered
/// dash on a dash-cancelable attack interrupts into the dash. The window stays open until the
/// attack's cancel point, so a slightly late press still chains. A chained normal micro-steps
/// towards a target that pushback carried just out of reach, and a normal started from a dash keeps
/// a decaying share of the dash speed, so strings and dash attacks slide instead of stopping dead.
/// Crouching and jumping variants derive from this class and override only posture, motion and the
/// stance they return to.
/// </summary>
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

    /// <summary>The buffer only ages while the cancel window is open, so a press during startup or after the window closes waits for the next actionable state.</summary>
    public override bool AcceptsCommands => IsCancelWindowOpen;

    /// <summary>Chain buttons always; specials and dashes only when the attack allows that cancel.</summary>
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

    /// <summary>
    /// Moves the fighter for this frame. Grounded attacks stay glued to the floor and slide with any
    /// dash momentum still in flight plus the micro-step that keeps a chained strike on its target.
    /// Returns true when the motion ended the attack (an air attack touching the ground), so the
    /// timeline stops.
    /// </summary>
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

    /// <summary>
    /// Dash momentum handed over by the controller decays linearly to zero by the end of the active
    /// frames. A chained normal instead receives a micro-step budget it may spend during startup.
    /// </summary>
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

    /// <summary>
    /// Combo micro-step. During the startup of a chained normal the attacker closes on the target
    /// whenever pushback has carried it beyond the strike's reach, tracking the slide frame by frame
    /// because the defender is often still frozen in hitstop when the chain starts. The step is paid
    /// from a budget capped by the rules and only within the micro-step range, so a string dropped at
    /// distance stays dropped and nothing moves when the target is already in reach.
    /// </summary>
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

    /// <summary>
    /// Cancel priority once the attack has connected: special, jump, chain, dash. Returns true on transition.
    /// </summary>
    protected virtual bool TryCancels()
    {
        if (TrySpecialCancel(data)) return true;
        if (TryJumpCancel()) return true;
        if (TryChainCancel(data, false)) return true;
        return TryDashCancel();
    }

    /// <summary>
    /// Stance the fighter settles into once the recovery frames elapse. Standing attacks always
    /// return to Idle; crouching attacks override this to honour a held-down stick.
    /// </summary>
    protected virtual void ExitToStance()
    {
        robot.EnterIdle();
    }

    /// <summary>
    /// Ends the active window. A swing that never touched the opponent plays the whiff sound when
    /// <paramref name="reportWhiff"/> is set.
    /// </summary>
    protected void DeactivateHitbox(bool reportWhiff)
    {
        if (!hitboxActive) return;

        robot.hitboxBase.Deactivate();
        hitboxActive = false;

        if (reportWhiff && !robot.AttackConnected && AudioManager.Instance != null) AudioManager.Instance.PlayWhiff();
    }

    /// <summary>
    /// Jump cancel: a connected jump-cancelable attack with the stick held up launches the super
    /// jump immediately, so the attacker rises with the body it just launched.
    /// </summary>
    private bool TryJumpCancel()
    {
        if (!data.jumpCancelable || !robot.AttackConnected || robot.MovementInput.y <= JumpCancelInputThreshold) return false;

        robot.EnterJump(robot.Rules.jumpCancelForce);
        return true;
    }

    /// <summary>Dash cancel: a connected dash-cancelable attack yields to a buffered dash.</summary>
    private bool TryDashCancel()
    {
        if (!data.dashCancelable || !robot.AttackConnected) return false;
        return TryConsumeDashCommands();
    }
}
