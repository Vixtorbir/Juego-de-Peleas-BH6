using UnityEngine;

/// <summary>
/// Rising uppercut special driven by its <see cref="AttackData"/> frame data. The startup frames
/// are invincible when the data flags them so the move works as a reversal and anti-air, the
/// hitbox is live from the end of startup for the active window while the fighter climbs and
/// drifts forward, and landing is followed by the data's recovery, which leaves a long punishable
/// window on whiff or block. The leap direction is locked at takeoff: a cross-over during the
/// rise never reverses the arc mid-air.
/// </summary>
public class ShoryukenState : RobotState
{
    private const float RiseVelocity = 14f;
    private const float RiseGravity = -25f;
    private const float ForwardSpeed = 4f;

    private AttackData attackData;
    private float timer;
    private float verticalVelocity;
    private float forwardSign;
    private bool hitboxActive;
    private bool landed;
    private float landingTimer;

    public ShoryukenState(RobotController robot) : base(robot) { }

    public void Configure(AttackData data)
    {
        attackData = data;
    }

    public override void Enter()
    {
        timer = 0f;
        verticalVelocity = RiseVelocity;
        hitboxActive = false;
        landed = false;
        landingTimer = 0f;

        robot.AttackConnected = false;
        robot.IsInvincible = attackData != null && attackData.invincibleStartup;
        robot.FaceOpponent();
        forwardSign = robot.transform.forward.x < 0f ? -1f : 1f;

        PlayAttackAnimation(attackData);

        if (AudioManager.Instance != null) AudioManager.Instance.PlayShoryukenRise();
    }

    public override void Tick()
    {
        if (landed)
        {
            TickLandingRecovery();
            return;
        }

        timer += Time.deltaTime;
        UpdateInvincibility();
        UpdateHitbox();
        Move();

        if (HasLanded(verticalVelocity))
        {
            landed = true;
            DeactivateHitbox();
        }
    }

    public override void Exit()
    {
        robot.IsInvincible = false;
        DeactivateHitbox();
    }

    private void UpdateInvincibility()
    {
        if (robot.IsInvincible && attackData != null && timer >= attackData.startupTime)
        {
            robot.IsInvincible = false;
        }
    }

    private void UpdateHitbox()
    {
        if (attackData == null) return;

        if (!hitboxActive && timer >= attackData.startupTime && timer < attackData.ActiveEndTime)
        {
            robot.hitboxBase.Activate(attackData);
            hitboxActive = true;
        }
        else if (hitboxActive && timer >= attackData.ActiveEndTime)
        {
            DeactivateHitbox();
        }
    }

    private void Move()
    {
        verticalVelocity += RiseGravity * Time.deltaTime;

        Vector3 velocity = new Vector3(forwardSign * ForwardSpeed, verticalVelocity, 0f);
        robot.characterController.Move(velocity * Time.deltaTime);
    }

    private void TickLandingRecovery()
    {
        landingTimer += Time.deltaTime;
        ApplyGroundingForce();

        float recovery = attackData != null ? attackData.recoveryTime : 0f;
        if (landingTimer >= recovery)
        {
            robot.EnterIdle();
        }
    }

    private void DeactivateHitbox()
    {
        if (!hitboxActive) return;

        robot.hitboxBase.Deactivate();
        hitboxActive = false;
    }
}
