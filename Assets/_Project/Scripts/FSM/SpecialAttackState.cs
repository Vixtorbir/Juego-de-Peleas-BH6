using UnityEngine;

public class SpecialAttackState : RobotState
{
    private float totalDuration = 0.6f; 
    private float timer;
    private bool projectileSpawned;

    private AttackData attackData;
    private GameObject prefabProyectil;
    private Transform puntoSpawn;

    public SpecialAttackState(RobotController robot, AttackData data, GameObject prefab, Transform spawn) : base(robot)
    {
        this.attackData = data;
        this.prefabProyectil = prefab;
        this.puntoSpawn = spawn;
    }

    public override void Enter()
    {
        timer = totalDuration;
        projectileSpawned = false;

        robot.characterController.Move(Vector3.zero);

        // TODO: robot.animator.SetTrigger("Hadouken");

        Renderer renderer = robot.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = Color.magenta;
    }

    public override void Tick()
    {
        timer -= Time.deltaTime;

        // SINCRONIZACIÓN DE FRAME: Spawneamos la bola a mitad del ataque (a los 0.3 segundos)
        if (!projectileSpawned && timer <= totalDuration * 0.5f)
        {
            SpawnHadouken();
        }

        if (timer <= 0f)
        {
            robot.ChangeState(new IdleState(robot));
        }
    }

    private void SpawnHadouken()
    {
        projectileSpawned = true;

        if (prefabProyectil == null || puntoSpawn == null) return;

        // 1. Instanciamos el Prefab en la posición del punto de spawn
        GameObject clon = Object.Instantiate(prefabProyectil, puntoSpawn.position, Quaternion.identity);

        // 2. Calculamos hacia dónde mira el robot en este milisegundo exacto respecto a su rival
        float dirX = Mathf.Sign(robot.opponentTarget.position.x - robot.transform.position.x);
        Vector3 direccionDisparo = new Vector3(dirX, 0f, 0f);

        // 3. Inicializamos el proyectil pasándole sus datos y quién es su dueño
        HadoukenProjectile proyectilScript = clon.GetComponent<HadoukenProjectile>();
        if (proyectilScript != null)
        {
            proyectilScript.Initialize(direccionDisparo, 12f, attackData, robot);
        }
    }

    public override void Exit()
    {
        Renderer renderer = robot.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = Color.white;
    }
}