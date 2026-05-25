using UnityEngine;

public class Hurtbox : MonoBehaviour
{
    // Más adelante aquí le pasaremos el daño al RobotController para bajarle la vida
    // o para mandarlo al estado de Hitstun.
    public void RecibirImpacto()
    {
        Debug.Log("<color=red>¡IMPACTO RECIBIDO!</color> El robot ha sido golpeado.");
    }
}