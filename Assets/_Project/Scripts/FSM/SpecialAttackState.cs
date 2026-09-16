using UnityEngine;

/// <summary>
/// Projectile special (quarter-circle forward + punch). The fighter is rooted for the full
/// duration and the projectile is released at the midpoint, mirroring the release frame of
/// the throwing animation.
/// </summary>
public class SpecialAttackState : RobotState
{
    private const float TotalDuration = 0.6f;
    private const float ReleaseNormalizedTime = 0.5f;
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
        timer = TotalDuration;
        projectileSpawned = false;
        robot.FaceOpponent();
    }

    public override void Tick()
    {
        timer -= Time.deltaTime;

        if (!projectileSpawned && timer <= TotalDuration * ReleaseNormalizedTime)
        {
            SpawnProjectile();
        }

        if (timer <= 0f)
        {
            robot.ChangeState(new IdleState(robot));
        }
    }

    private void SpawnProjectile()
    {
        projectileSpawned = true;

        if (projectilePrefab == null || spawnPoint == null || robot.opponentTarget == null) return;

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
