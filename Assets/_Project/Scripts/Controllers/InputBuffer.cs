using System.Collections.Generic;
using UnityEngine;

public class InputBuffer : MonoBehaviour
{
    private RobotController robot;
    private InputReader inputReader;

    private List<BufferedDirection> directionHistory = new List<BufferedDirection>();

    private CombatDirection lastDiscreteDirection = CombatDirection.Neutral;

    [Header("Configuración del Buffer")]
    [SerializeField] private float inputLifeTime = 0.35f; 

    private void Awake()
    {
        robot = GetComponent<RobotController>();
        inputReader = GetComponent<InputReader>();
    }

    private void Update()
    {
        LimpiarInputViejo();
        ProcesarDireccionActual();
    }

    private void ProcesarDireccionActual()
    {
        if (robot.opponentTarget == null) return;

        // 1. Leer el input vectorial crudo
        Vector2 rawInput = robot.MovementInput; 

        // 2. Determinar hacia dónde está mirando el robot respecto al rival
        float direccionHaciaRival = Mathf.Sign(robot.opponentTarget.position.x - transform.position.x);

        // 3. Traducir los ejes x e y a direcciones relativas de combate
        CombatDirection currentDir = TraducirACombatDirection(rawInput, direccionHaciaRival);

        // 4. Registrar solo cuando la dirección CAMBIA (para no llenar el buffer con duplicados)
        if (currentDir != lastDiscreteDirection)
        {
            lastDiscreteDirection = currentDir;
            directionHistory.Add(new BufferedDirection(currentDir, Time.time));
        }
    }

    private CombatDirection TraducirACombatDirection(Vector2 input, float ladoEnPantalla)
    {
        float x = input.x;
        float y = input.y;

        bool dePie = y > -0.4f && y < 0.4f;
        bool agachado = y <= -0.4f;
        bool saltando = y >= 0.4f;

        bool neutralHorizontal = x > -0.4f && x < 0.4f;
        bool moviendoAdelante = !neutralHorizontal && Mathf.Sign(x) == ladoEnPantalla;
        bool moviendoAtras = !neutralHorizontal && Mathf.Sign(x) != ladoEnPantalla;

        if (agachado)
        {
            if (moviendoAdelante) return CombatDirection.DownForward;
            if (moviendoAtras) return CombatDirection.DownBack;
            return CombatDirection.Down;
        }
        if (saltando)
        {
            if (moviendoAdelante) return CombatDirection.UpForward;
            if (moviendoAtras) return CombatDirection.UpBack;
            return CombatDirection.Up;
        }

        if (moviendoAdelante) return CombatDirection.Forward;
        if (moviendoAtras) return CombatDirection.Back;

        return CombatDirection.Neutral;
    }

    private void LimpiarInputViejo()
    {
        directionHistory.RemoveAll(input => Time.time - input.timestamp > inputLifeTime);
    }

    // EL ALGORITMO CLAVE: Comprobación en orden inverso (Reverse Sequence Matching)
    public bool CheckMotionInput(CombatDirection[] sequenceRequired)
    {
        if (directionHistory.Count < sequenceRequired.Length) return false;

        int indexParaBuscar = sequenceRequired.Length - 1; 

        for (int i = directionHistory.Count - 1; i >= 0; i--)
        {
            if (directionHistory[i].direction == sequenceRequired[indexParaBuscar])
            {
                indexParaBuscar--; 

                if (indexParaBuscar < 0)
                {
                    directionHistory.Clear();
                    return true;
                }
            }
        }

        return false;
    }
}