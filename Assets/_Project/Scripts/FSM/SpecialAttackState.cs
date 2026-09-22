using UnityEngine;

public class SpecialAttackState : RobotState
{
    private const float ProjectileSpeed = 12f;

    private AttackData attackData;
    private float timer;
    private bool projectileFired;

    public SpecialAttackState(RobotController robot) : base(robot) { }

    public void Configure(AttackData data)
    {
        attackData = data;
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
            robot.EnterIdle();
            return;
        }

        if (!projectileFired && timer >= attackData.startupTime)
        {
            FireProjectile();
        }

        if (timer >= attackData.TotalDuration)
        {
            robot.EnterIdle();
        }
    }

    private void FireProjectile()
    {
        projectileFired = true;

        ProjectilePool projectiles = robot.Projectiles;
        Transform spawnPoint = robot.hadoukenSpawnPoint;
        if (projectiles == null || spawnPoint == null) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayHadoukenShot();

        Vector3 fireDirection = robot.transform.forward.x < 0f ? Vector3.left : Vector3.right;
        projectiles.Acquire().Launch(spawnPoint.position, fireDirection, ProjectileSpeed, attackData, robot);
    }
}
