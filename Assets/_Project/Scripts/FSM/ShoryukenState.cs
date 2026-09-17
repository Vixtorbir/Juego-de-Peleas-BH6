using UnityEngine;

/// <summary>
/// Rising uppercut special driven by its <see cref="AttackData"/> frame data. The startup frames
/// are invincible when the data flags them so the move works as a reversal and anti-air, the
/// hitbox is live from the end of startup for the active window while the fighter climbs and
/// drifts towards the opponent, and landing is followed by the data's recovery, which leaves a
/// long punishable window on whiff or block.
/// </summary>
public class ShoryukenState : RobotState
{
    private const float RiseVelocity = 14f;
    private const float RiseGravity = -25f;
    private const float ForwardSpeed = 4f;

    private readonly AttackData attackData;
    private float timer;
    private float verticalVelocity;
    private bool hitboxActive;
    private bool landed;
    private float landingTimer;

    public ShoryukenState(RobotController robot, AttackData data) : base(robot)
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

        if (verticalVelocity < 0f && robot.characterController.isGrounded)
        {
            landed = true;
            robot.characterController.Move(Vector3.up * GroundSnapVelocity);
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
        if (attackData == null || robot.hitboxBase == null) return;

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

        float forwardSign = robot.opponentTarget != null
            ? Mathf.Sign(robot.opponentTarget.position.x - robot.transform.position.x)
            : 0f;

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
            robot.ChangeState(new IdleState(robot));
        }
    }

    private void DeactivateHitbox()
    {
        if (!hitboxActive) return;

        robot.hitboxBase.Deactivate();
        hitboxActive = false;
    }

    /// <summary>
    /// The airborne portion is dictated by the jump physics, so the clip is stretched over the
    /// full flight time plus the landing recovery rather than over the raw frame data.
    /// </summary>
}
