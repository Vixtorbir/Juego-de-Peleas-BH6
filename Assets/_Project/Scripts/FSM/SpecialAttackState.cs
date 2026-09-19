using UnityEngine;

/// <summary>
/// Projectile special (quarter-circle forward + punch). The fighter is rooted for the attack's
/// full frame data: the projectile is released from the owner's <see cref="ProjectilePool"/> once
/// the startup frames elapse, and the recovery frames leave the move punishable up close. The
/// startup is calibrated to the clip frame on which the hands lock out and the spawn point sits at
/// the palms on that frame, so the shot sound, the launch and the pose coincide on the same tick.
/// </summary>
public class SpecialAttackState : RobotState
{
    private const float ProjectileSpeed = 12f;

    private readonly AttackData attackData;
    private readonly ProjectilePool projectiles;
    private readonly Transform spawnPoint;

    private float timer;
    private bool projectileFired;

    public SpecialAttackState(RobotController robot, AttackData data, ProjectilePool pool, Transform spawn) : base(robot)
    {
        attackData = data;
        projectiles = pool;
        spawnPoint = spawn;
    }

    public override void Enter()
    {
        timer = 0f;
        projectileFired = false;

        robot.AttackConnected = false;
        robot.FaceOpponent();
        PlayAttackAnimation(attackData);

        if (AudioManager.Instance != null) AudioManager.Instance.PlayHadoukenCast();
    }

    public override void Tick()
    {
        timer += Time.deltaTime;
        ApplyGroundingForce();

        if (attackData == null)
        {
            robot.ChangeState(new IdleState(robot));
            return;
        }

        if (!projectileFired && timer >= attackData.startupTime)
        {
            FireProjectile();
        }

        if (timer >= attackData.TotalDuration)
        {
            robot.ChangeState(new IdleState(robot));
        }
    }

    private void FireProjectile()
    {
        projectileFired = true;

        if (projectiles == null || spawnPoint == null) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayHadoukenShot();

        Vector3 fireDirection = robot.transform.forward.x < 0f ? Vector3.left : Vector3.right;
        projectiles.Acquire().Launch(spawnPoint.position, fireDirection, ProjectileSpeed, attackData, robot);
    }
}
