using UnityEngine;

public class Hurtbox : MonoBehaviour
{
    private RobotController controller;

    private void Awake()
    {
        controller = GetComponent<RobotController>();
    }

    public void RecibirImpacto(AttackData ataque)
    {
        if (controller == null) return;

        Debug.Log($"<color=red>¡IMPACTO RECIBIDO!</color> Ataque: {ataque.name}");

        bool estabaEnElAire = !controller.characterController.isGrounded;

        if (ataque.isLauncher)
        {
            controller.ChangeState(new AirborneHurtState(controller, ataque.launchForce));
        }
        else if (estabaEnElAire)
        {
            float fuerzaReboteAereo = 5f;
            controller.ChangeState(new AirborneHurtState(controller, fuerzaReboteAereo));
        }
        else
        {
            controller.ChangeState(new HitstunState(controller));
        }
    }
}