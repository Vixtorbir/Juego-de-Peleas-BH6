using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

/// <summary>
/// Training room tooling. Fills both super gauges (M / R3) and restores both life bars
/// (Backspace / Select) on demand, keeps the dummy (player 2) from being knocked out mid-combo by
/// flooring its health, and lets the dummy recover once it has stood idle for a moment without
/// being hit. Devices are polled directly so the hotkeys stay independent of the fighters' action
/// maps. Disabling the component restores normal lethal damage.
/// </summary>
public class TrainingModeController : MonoBehaviour
{
    [SerializeField] private MatchManager matchManager;

    [Header("Hotkeys")]
    [Tooltip("Keyboard key that fills both super gauges. Gamepads use the right stick press.")]
    [SerializeField] private Key fillMeterKey = Key.M;
    [Tooltip("Keyboard key that restores both life bars. Gamepads use Select. Must not collide with a fighter binding.")]
    [SerializeField] private Key restoreHealthKey = Key.Backspace;

    [Header("Dummy Recovery")]
    [Tooltip("Seconds the dummy must stand idle without being hit before it starts to recover.")]
    [SerializeField] private float regenDelay = 1.5f;
    [Tooltip("Health restored per second once recovery starts.")]
    [SerializeField] private float regenPerSecond = 40f;
    [Tooltip("Lowest health damage can take the dummy to while training mode is active.")]
    [SerializeField] private float dummyHealthFloor = 1f;

    private RobotController player1;
    private RobotController dummy;
    private HealthComponent dummyHealth;
    private float idleTimer;
    private float lastDummyHealth;

    private void Awake()
    {
        if (matchManager == null) matchManager = FindFirstObjectByType<MatchManager>();
        if (matchManager == null) return;

        player1 = matchManager.player1;
        dummy = matchManager.player2;
        if (dummy != null) dummy.TryGetComponent(out dummyHealth);
    }

    private void OnEnable()
    {
        if (dummyHealth == null) return;

        dummyHealth.SetDamageFloor(dummyHealthFloor);
        dummyHealth.OnHealthChanged += HandleDummyHealthChanged;
        lastDummyHealth = dummyHealth.CurrentHealth;
        idleTimer = 0f;
    }

    private void OnDisable()
    {
        if (dummyHealth == null) return;

        dummyHealth.OnHealthChanged -= HandleDummyHealthChanged;
        dummyHealth.SetDamageFloor(0f);
    }

    private void Update()
    {
        if (matchManager == null || matchManager.CurrentState != MatchManager.MatchState.Fighting) return;

        if (WasFillMeterPressed(fillMeterKey))
        {
            FillMeter(player1);
            FillMeter(dummy);
        }

        if (WasRestoreHealthPressed(restoreHealthKey))
        {
            RestoreHealth(player1);
            RestoreHealth(dummy);
        }

        TickDummyRecovery(Time.deltaTime);
    }

    private void TickDummyRecovery(float deltaTime)
    {
        if (dummyHealth == null || dummy.CurrentState is not IdleState)
        {
            idleTimer = 0f;
            return;
        }

        idleTimer += deltaTime;
        if (idleTimer >= regenDelay) dummyHealth.Heal(regenPerSecond * deltaTime);
    }

    private void HandleDummyHealthChanged(float current, float max)
    {
        if (current < lastDummyHealth) idleTimer = 0f;
        lastDummyHealth = current;
    }

    private static void FillMeter(RobotController fighter)
    {
        if (fighter != null && fighter.SuperMeter != null) fighter.SuperMeter.FillCharge();
    }

    private static void RestoreHealth(RobotController fighter)
    {
        if (fighter != null && fighter.Health != null) fighter.Health.ResetHealth();
    }

    private static bool WasFillMeterPressed(Key key)
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[key].wasPressedThisFrame) return true;

        ReadOnlyArray<Gamepad> gamepads = Gamepad.all;
        for (int i = 0; i < gamepads.Count; i++)
        {
            if (gamepads[i].rightStickButton.wasPressedThisFrame) return true;
        }

        return false;
    }

    private static bool WasRestoreHealthPressed(Key key)
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[key].wasPressedThisFrame) return true;

        ReadOnlyArray<Gamepad> gamepads = Gamepad.all;
        for (int i = 0; i < gamepads.Count; i++)
        {
            if (gamepads[i].selectButton.wasPressedThisFrame) return true;
        }

        return false;
    }
}
