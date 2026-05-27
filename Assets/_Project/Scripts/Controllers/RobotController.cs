using UnityEngine;

// 1. AÑADIMOS EL NUEVO COMANDO AL ENUMERADO
public enum FighterCommand
{
    None,
    LeftPunch,
    RightPunch,
    LeftKick,
    RightKick,
    DashForward,
    DashBackward,
    Hadouken,
    Shoryuken
}

public class RobotController : MonoBehaviour
{
    [Header("Dependencies")]
    public InputReader inputReader;
    public Animator animator;
    public CharacterController characterController;
    public Hitbox hitboxBase;

    [Header("Combat Settings")]
    public Transform opponentTarget;

    [Header("Attack Data Assets")]
    public AttackData leftPunchBase;
    public AttackData rightPunchBase;
    public AttackData leftKickBase;
    public AttackData rightKickBase;
    public AttackData crouchPunchBase;
    public AttackData crouchSweepBase;
    public AttackData launcherBase;
    public AttackData hadoukenBase;
    public AttackData shoryukenData;

    [Header("Special Attack Settings")]
    public AttackData hadoukenData;        
    public GameObject hadoukenPrefab;     
    public Transform hadoukenSpawnPoint;

    [Header("Input Buffer")]
    public FighterCommand bufferedCommand = FighterCommand.None;
    private float bufferTimer = 0f;
    private float bufferWindow = 0.2f;

    [Header("Dash Detection Settings")]
    private float lastTapTime;
    private float doubleTapWindow = 0.25f;
    private int lastTapDirection = 0;
    private bool isDirectionPressed = false;

    [Header("Physics & Grounding")]
    public LayerMask groundLayer;
    public float groundCheckOffset = 0.1f;
    public bool IsGrounded { get; private set; }

    public bool IsInAttackStartup { get; set; }

    public Vector2 MovementInput { get; private set; }
    private RobotState currentState;
    private Transform cameraTransform;

    private InputBuffer inputBuffer;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (characterController == null) characterController = GetComponent<CharacterController>();
        cameraTransform = Camera.main.transform;
        inputBuffer = GetComponent<InputBuffer>();
    }

    private void Start()
    {
        if (inputReader != null)
        {
            inputReader.MoveEvent += HandleMove;
            inputReader.LeftPunchEvent += HandleLeftPunch;
            inputReader.RightPunchEvent += HandleRightPunch;
            inputReader.LeftKickEvent += HandleLeftKick;
            inputReader.RightKickEvent += HandleRightKick;
        }

        ChangeState(new IdleState(this));
    }

    private void Update()
    {
        CalcularSuelo();

        if (bufferTimer > 0f)
        {
            bufferTimer -= Time.deltaTime;
            if (bufferTimer <= 0f) bufferedCommand = FighterCommand.None;
        }

        AplicarPushboxHorizontal();

        currentState?.Tick();
    }

    private void CalcularSuelo()
    {
        float distanciaAlSuelo = characterController.bounds.extents.y + groundCheckOffset;
        IsGrounded = Physics.Raycast(transform.position, Vector3.down, distanciaAlSuelo, groundLayer);
    }

    public bool TryConsumeCommand(FighterCommand expectedCommand)
    {
        if (bufferedCommand == expectedCommand)
        {
            bufferedCommand = FighterCommand.None;
            bufferTimer = 0f;
            return true;
        }
        return false;
    }

    private void HandleMove(Vector2 input)
    {
        MovementInput = input;

        if (Mathf.Abs(input.x) < 0.2f)
        {
            isDirectionPressed = false;
            return;
        }

        if (!isDirectionPressed && Mathf.Abs(input.x) > 0.6f)
        {
            isDirectionPressed = true;
            int currentTapDirection = input.x > 0f ? 1 : -1;

            if (currentTapDirection == lastTapDirection && (Time.time - lastTapTime) <= doubleTapWindow)
            {
                ProcesarDashRelativo(currentTapDirection);
            }
            else
            {
                lastTapDirection = currentTapDirection;
                lastTapTime = Time.time;
            }
        }
    }

    private void ProcesarDashRelativo(int tapDirection)
    {
        if (opponentTarget == null) return;

        Vector3 directionToOpponent = opponentTarget.position - transform.position;
        directionToOpponent.y = 0f;
        float screenSide = Mathf.Sign(Vector3.Dot(directionToOpponent.normalized, cameraTransform.right));

        if (tapDirection == (int)screenSide)
            bufferedCommand = FighterCommand.DashForward;
        else
            bufferedCommand = FighterCommand.DashBackward;

        bufferTimer = bufferWindow;
        lastTapDirection = 0;
    }

    // --- LA MAGIA DEL INPUT BUFFER ESTÁ AQUÍ ---
    private void HandleLeftPunch()
    {
        // 1. Definimos el movimiento a buscar (Cuarto de luna hacia adelante)
        CombatDirection[] hadoukenSequence = new CombatDirection[]
        {
            CombatDirection.Down,
            CombatDirection.DownForward,
            CombatDirection.Forward
        };

        // 2. Le preguntamos al InputBuffer si esa secuencia está en el historial
        if (inputBuffer != null && inputBuffer.CheckMotionInput(hadoukenSequence))
        {
            bufferedCommand = FighterCommand.Hadouken; 
            Debug.Log("<color=magenta>COMANDO ESPECIAL DETECTADO: Hadouken guardado en el buffer.</color>");
        }
        else
        {
            bufferedCommand = FighterCommand.LeftPunch; 
        }

        bufferTimer = bufferWindow;
    }

    private void HandleRightPunch()
    {
        // 1. Secuencia de la "Z" (Adelante -> Abajo -> AbajoAdelante)
        CombatDirection[] dpSequence = new CombatDirection[]
        {
            CombatDirection.Forward,
            CombatDirection.Down,
            CombatDirection.DownForward
        };

        // 2. Comprobamos el buffer
        if (inputBuffer != null && inputBuffer.CheckMotionInput(dpSequence))
        {
            bufferedCommand = FighterCommand.Shoryuken;
            Debug.Log("<color=magenta>COMANDO ESPECIAL DETECTADO: Shoryuken.</color>");
        }
        else
        {
            bufferedCommand = FighterCommand.RightPunch;
        }

        bufferTimer = bufferWindow;
    }
    private void HandleLeftKick() { bufferedCommand = FighterCommand.LeftKick; bufferTimer = bufferWindow; }
    private void HandleRightKick() { bufferedCommand = FighterCommand.RightKick; bufferTimer = bufferWindow; }

    public void ChangeState(RobotState newState) { currentState?.Exit(); currentState = newState; currentState?.Enter(); }

    private void AplicarPushboxHorizontal()
    {
        if (opponentTarget == null) return;

        // 1. Calculamos la distancia solo en horizontal (ignoramos la altura Y)
        Vector3 distancia = transform.position - opponentTarget.position;
        distancia.y = 0f;

        // Si por algún bug matemático caen EXACTAMENTE en la misma coordenada X, forzamos un lado
        if (distancia.magnitude < 0.01f)
        {
            distancia = transform.right;
        }

        // 2. La distancia mínima permitida entre los cuerpos (Ajusta esto entre 1.0f y 1.5f según tu modelo 3D)
        float distanciaMinima = 1.2f;

        // 3. Si están invadiendo su espacio vital, los repelemos magnéticamente
        if (distancia.magnitude < distanciaMinima)
        {
            float invasion = distanciaMinima - distancia.magnitude;

            // Empujamos a este robot hacia atrás
            Vector3 fuerzaSeparacion = distancia.normalized * invasion * 10f * Time.deltaTime;
            characterController.Move(fuerzaSeparacion);
        }
    }

    // HELPER: Activa o desactiva la lectura de controles
    public void SetControlsActive(bool active)
    {
        if (inputReader != null) inputReader.enabled = active;
    }

    // HELPER: Reinicia al personaje para un nuevo Round
    public void ResetForNewRound(Vector3 startPosition)
    {
        // 1. Desactivamos el CharacterController un milisegundo para poder teletransportarlo
        characterController.enabled = false;
        transform.position = startPosition;
        characterController.enabled = true;

        // 2. Curamos al robot
        HealthComponent health = GetComponent<HealthComponent>();
        if (health != null) health.ResetHealth();

        // 3. Limpiamos cualquier ataque guardado en la recámara
        bufferedCommand = FighterCommand.None;

        // 4. Lo devolvemos al estado inicial
        ChangeState(new IdleState(this));
    }

    private void OnDestroy()
    {
        if (inputReader != null)
        {
            inputReader.MoveEvent -= HandleMove;
            inputReader.LeftPunchEvent -= HandleLeftPunch;
            inputReader.RightPunchEvent -= HandleRightPunch;
            inputReader.LeftKickEvent -= HandleLeftKick;
            inputReader.RightKickEvent -= HandleRightKick;
        }
    }
}