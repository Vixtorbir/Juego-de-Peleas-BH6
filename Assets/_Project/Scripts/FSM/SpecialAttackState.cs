using UnityEngine;

/// <summary>
/// Projectile special (quarter-circle forward + punch). The fighter is rooted for the attack's
/// full frame data: the projectile is released once the startup frames elapse, mirroring the
/// throw frame of the animation, and the recovery frames leave the move punishable up close.
/// </summary>
public class SpecialAttackState : RobotState
{
    private const float ProjectileSpeed = 12f;

    private readonly AttackData attackData;
    private readonly GameObject projectilePrefab;
    private readonly Transform spawnPoint;

    private float timer;
    private bool projectileSpawned;

    public SpecialAttackState(RobotController robot, AttackData data, GameObject prefab, Transform spawn) : base(robot)
    {
        attackData = data;
        projectilePrefab = prefab;
        spawnPoint = spawn;
    }

    public override void Enter()
    {
        timer = 0f;
        projectileSpawned = false;

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

        if (!projectileSpawned && timer >= attackData.startupTime)
        {
            SpawnProjectile();
        }

        if (timer >= attackData.TotalDuration)
        {
            robot.ChangeState(new IdleState(robot));
        }
    }

    private void SpawnProjectile()
    {
        projectileSpawned = true;

        if (projectilePrefab == null || spawnPoint == null || robot.opponentTarget == null) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayHadoukenShot();

        float forwardSign = Mathf.Sign(robot.opponentTarget.position.x - robot.transform.position.x);
        Vector3 fireDirection = new Vector3(forwardSign, 0f, 0f);

        GameObject instance = Object.Instantiate(projectilePrefab, spawnPoint.position, Quaternion.identity);
        HadoukenProjectile projectile = instance.GetComponent<HadoukenProjectile>();

        if (projectile != null)
        {
            projectile.Initialize(fireDirection, ProjectileSpeed, attackData, robot);
        }
    }
}
