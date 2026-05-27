using UnityEngine;

public class Hurtbox : MonoBehaviour
{
    private RobotController controller;
    private Transform cameraTransform;
    private HealthComponent health;
    private int comboHitCount = 0;

    private void Awake()
    {
        controller = GetComponent<RobotController>();
        cameraTransform = Camera.main.transform;

        health = GetComponentInParent<HealthComponent>();
    }

    public void RecibirImpacto(AttackData ataque, RobotController atacante)
    {
        if (controller == null) return;
        if (health != null && health.IsDead) return;

        bool esCounterHit = controller.IsInAttackStartup;

        // --- CÁLCULO DE EFECTOS (JUICINESS) ---
        float freezeTime = ataque.damage * 0.005f;
        float shakeMag = ataque.damage * 0.003f;
        float shakeTime = 0.1f;

        if (esCounterHit)
        {
            Debug.Log("<color=yellow><b>¡COUNTER HIT!</b></color>");

            if (CombatEffectsManager.Instance != null)
                CombatEffectsManager.Instance.TriggerHitstop(freezeTime * 1.5f);

            FightingCamera cam = Camera.main.GetComponent<FightingCamera>();
            if (cam != null) cam.ShakeCamera(shakeTime * 1.5f, shakeMag * 1.5f);
        }
        else
        {
            Debug.Log($"<color=red>¡IMPACTO!</color> Ataque: {ataque.name}");

            if (CombatEffectsManager.Instance != null)
                CombatEffectsManager.Instance.TriggerHitstop(freezeTime);

            FightingCamera cam = Camera.main.GetComponent<FightingCamera>();
            if (cam != null) cam.ShakeCamera(shakeTime, shakeMag);
        }

        // 1. REGLA DE ORO: Impactos aéreos
        bool estaEnElAire = !controller.IsGrounded;
        if (estaEnElAire)
        {
            ApplyDamage(ataque.damage);
            ProcesarDanoAereo(ataque, esCounterHit);
            return;
        }

        bool intentandoBloquear = EvaluarIntencionDeBloqueo();
        bool estaAgachado = controller.MovementInput.y < -0.5f;

        if (intentandoBloquear && !esCounterHit)
        {
            // --- MATRICES DE GUARDIA TERRESTRE NORMAL ---
            if (ataque.attackHeight == AttackHeight.High)
            {
                if (estaAgachado)
                {
                    Debug.Log("<color=green>¡EVADIDO!</color> El ataque alto pasó por encima.");
                    return;
                }
                else
                {
                    ApplyChipDamage(ataque.damage);
                    controller.ChangeState(new BlockstunState(controller, ataque.blockstunDuration, false, ataque.blockPushback, atacante));
                    return;
                }
            }
            else if (ataque.attackHeight == AttackHeight.Mid)
            {
                if (estaAgachado)
                {
                    Debug.Log("<color=orange>¡GUARDIA ROTA!</color> Impacto medio sufrido por estar agachado.");
                    ApplyDamage(ataque.damage);
                    IrAHitstun(ataque, false, atacante);
                    return;
                }
                else
                {
                    ApplyChipDamage(ataque.damage); 
                    controller.ChangeState(new BlockstunState(controller, ataque.blockstunDuration, false, ataque.blockPushback, atacante));
                    return;
                }
            }
            else if (ataque.attackHeight == AttackHeight.Low)
            {
                if (estaAgachado)
                {
                    ApplyChipDamage(ataque.damage); 
                    controller.ChangeState(new BlockstunState(controller, ataque.blockstunDuration, true, ataque.blockPushback, atacante));
                    return;
                }
                else
                {
                    Debug.Log("<color=orange>¡GUARDIA ROTA!</color> Impacto bajo sufrido por estar de pie.");
                    ApplyDamage(ataque.damage);
                    IrAHitstun(ataque, false, atacante);
                    return;
                }
            }
        }

        ApplyDamage(ataque.damage);
        IrAHitstun(ataque, esCounterHit, atacante);
    }

    private void ApplyDamage(float rawDamage)
    {
        if (health == null) return;

        comboHitCount++;

        float multiplicadorEscalado = Mathf.Pow(0.85f, comboHitCount - 1);

        multiplicadorEscalado = Mathf.Max(multiplicadorEscalado, 0.1f);

        float finalDamage = rawDamage * multiplicadorEscalado;

        health.TakeDamage(finalDamage);

        Debug.Log($"<color=cyan>Combo x{comboHitCount}</color> | Daño Original: {rawDamage} | Daño Escalado: {finalDamage:F1}");

        if (health.IsDead)
        {
            controller.ChangeState(new DeathState(controller));
        }
    }

    public void ResetCombo()
    {
        if (comboHitCount > 0)
        {
            Debug.Log($"<color=white>Combo finalizado. Total hits: {comboHitCount}</color>");
            comboHitCount = 0;
        }
    }

    private void ApplyChipDamage(float rawDamage)
    {
        if (health == null || health.IsDead) return;

        float chipDamage = rawDamage * 0.15f;

        if (health.CurrentHealth - chipDamage <= 0f)
        {
            chipDamage = health.CurrentHealth - 1f;
        }

        if (chipDamage > 0f)
        {
            health.TakeDamage(chipDamage);
            Debug.Log($"<color=cyan>¡CHIP DAMAGE!</color> Daño absorbido por la guardia: {chipDamage}");
        }
    }

    private void IrAHitstun(AttackData ataque, bool esCounter, RobotController atacante)
    {
        if (health != null && health.IsDead) return;

        float modDuration = esCounter ? 1.5f : 1f;
        float modPushback = esCounter ? 1.4f : 1f;
        float modLaunch = esCounter ? 1.3f : 1f;

        if (ataque.isLauncher)
        {
            Debug.Log("<color=magenta>¡LAUNCHER CONECTADO!</color> Mandando al rival a las nubes.");
            controller.ChangeState(new AirborneHurtState(controller, ataque.launchForce * modLaunch));
        }
        else
        {
            controller.ChangeState(new HitstunState(
                controller,
                ataque.hitstunDuration * modDuration,
                ataque.hitPushback * modPushback,
                esCounter,
                atacante
            ));
        }
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
        if (health != null && health.IsDead) return;

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