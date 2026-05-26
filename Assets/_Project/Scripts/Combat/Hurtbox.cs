using UnityEngine;

public class Hurtbox : MonoBehaviour
{
    private RobotController controller;
    private Transform cameraTransform;

    private void Awake()
    {
        controller = GetComponent<RobotController>();
        cameraTransform = Camera.main.transform;
    }

    public void RecibirImpacto(AttackData ataque)
    {
        if (controller == null) return;

        // --- DETECTAR COUNTER HIT ---
        bool esCounterHit = controller.IsInAttackStartup;

        if (esCounterHit)
        {
            Debug.Log("<color=yellow><b>¡COUNTER HIT!</b> Interrupción letal detectada.</color>");
        }

        // 1. REGLA DE ORO: Si está en el aire, se traga el golpe automáticamente (Air Hit / Juggle)
        bool estaEnElAire = !controller.IsGrounded;
        if (estaEnElAire)
        {
            ProcesarDanoAereo(ataque, esCounterHit);
            return;
        }

        bool intentandoBloquear = EvaluarIntencionDeBloqueo();
        bool estaAgachado = controller.MovementInput.y < -0.5f;

        // REGLA PROFESIONAL: Si es Counter Hit, NO se puede bloquear. El counter rompe la guardia.
        if (intentandoBloquear && !esCounterHit)
        {
            // --- MATRICES DE GUARDIA TERRESTRE NORMAL ---
            if (ataque.attackHeight == AttackHeight.High)
            {
                if (estaAgachado)
                {
                    Debug.Log("<color=green>¡EVADIDO!</color> El ataque alto pasó por encima del robot agachado.");
                    return;
                }
                else
                {
                    controller.ChangeState(new BlockstunState(controller, ataque.blockstunDuration, false, ataque.blockPushback));
                    return;
                }
            }
            else if (ataque.attackHeight == AttackHeight.Mid)
            {
                if (estaAgachado)
                {
                    Debug.Log("<color=orange>¡GUARDIA ROTA!</color> Impacto medio sufrido por estar agachado.");
                    IrAHitstun(ataque, false);
                    return;
                }
                else
                {
                    controller.ChangeState(new BlockstunState(controller, ataque.blockstunDuration, false, ataque.blockPushback));
                    return;
                }
            }
            else if (ataque.attackHeight == AttackHeight.Low)
            {
                if (estaAgachado)
                {
                    controller.ChangeState(new BlockstunState(controller, ataque.blockstunDuration, true, ataque.blockPushback));
                    return;
                }
                else
                {
                    Debug.Log("<color=orange>¡GUARDIA ROTA!</color> Impacto bajo sufrido por estar de pie.");
                    IrAHitstun(ataque, false);
                    return;
                }
            }
        }

        IrAHitstun(ataque, esCounterHit);
    }

    private void IrAHitstun(AttackData ataque, bool esCounter)
    {
        float modDuration = esCounter ? 1.5f : 1f;
        float modPushback = esCounter ? 1.4f : 1f;

        controller.ChangeState(new HitstunState(
            controller,
            ataque.hitstunDuration * modDuration,
            ataque.hitPushback * modPushback,
            esCounter
        ));
    }

    private bool EvaluarIntencionDeBloqueo()
    {
        if (controller.opponentTarget == null) return false;

        Vector3 directionToOpponent = controller.opponentTarget.position - transform.position;
        directionToOpponent.y = 0f;
        float screenSide = Mathf.Sign(Vector3.Dot(directionToOpponent.normalized, cameraTransform.right));
        float horizontalInput = controller.MovementInput.x;

        return (horizontalInput * screenSide) < -0.3f;
    }

    private void ProcesarDanoAereo(AttackData ataque, bool esCounter)
    {
        float modLaunch = esCounter ? 1.3f : 1f;

        if (ataque.isLauncher)
        {
            controller.ChangeState(new AirborneHurtState(controller, ataque.launchForce * modLaunch));
        }
        else
        {
            float fuerzaReboteAereo = 5f;
            controller.ChangeState(new AirborneHurtState(controller, fuerzaReboteAereo * modLaunch));
        }
    }
}