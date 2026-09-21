using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Serialization;

/// <summary>
/// Bridge between the generated <see cref="FighterInputActions"/> asset and gameplay code.
/// Each instance owns a private copy of the action asset for its player slot. Device routing is
/// deliberately permissive: Player 1 listens to every connected device until a second controller
/// shows up, so any pad Windows exposes (XInput, DualShock/DualSense HID, DirectInput joystick or a
/// generic gamepad) drives the fighter with no reconnect. Only when two controllers are present are
/// they split by connection order between the two slots, with the keyboard shared by both.
/// </summary>
public class InputReader : MonoBehaviour
{
    public enum PlayerSlot { Player1, Player2 }

    private const string MovementActionName = "Movement";
    private const string LeftPunchActionName = "LeftPunch";
    private const string RightPunchActionName = "RightPunch";
    private const string LeftKickActionName = "LeftKick";
    private const string RightKickActionName = "RightKick";
    private const int MaxControllersPerRefresh = 8;

    private static readonly Comparison<InputDevice> DeviceIdOrder = CompareByDeviceId;

    [Header("Player Assignment")]
    [FormerlySerializedAs("tipoJugador")]
    public PlayerSlot playerSlot = PlayerSlot.Player1;

    public event Action<Vector2> MoveEvent;
    public event Action LeftPunchEvent;
    public event Action RightPunchEvent;
    public event Action LeftKickEvent;
    public event Action RightKickEvent;

    private FighterInputActions controls;
    private InputActionMap actionMap;
    private InputAction movementAction;
    private InputAction leftPunchAction;
    private InputAction rightPunchAction;
    private InputAction leftKickAction;
    private InputAction rightKickAction;

    private readonly List<InputDevice> connectedControllers = new List<InputDevice>(MaxControllersPerRefresh);
    private readonly List<InputDevice> listenedDevices = new List<InputDevice>(MaxControllersPerRefresh + 1);
    private readonly StringBuilder deviceReport = new StringBuilder(128);

    private InputDevice[] routedDevices;
    private bool routedUnrestricted;

    private void Awake()
    {
        controls = new FighterInputActions();
        actionMap = playerSlot == PlayerSlot.Player1 ? controls.Player1.Get() : controls.Player2.Get();

        movementAction = actionMap[MovementActionName];
        leftPunchAction = actionMap[LeftPunchActionName];
        rightPunchAction = actionMap[RightPunchActionName];
        leftKickAction = actionMap[LeftKickActionName];
        rightKickAction = actionMap[RightKickActionName];
    }

    private void OnEnable()
    {
        movementAction.performed += OnMove;
        movementAction.canceled += OnMove;
        leftPunchAction.started += OnLeftPunch;
        rightPunchAction.started += OnRightPunch;
        leftKickAction.started += OnLeftKick;
        rightKickAction.started += OnRightKick;

        RefreshDeviceRouting();
        actionMap.Enable();
        InputSystem.onDeviceChange += OnDeviceChange;
    }

    private void OnDisable()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
        if (controls == null) return;

        movementAction.performed -= OnMove;
        movementAction.canceled -= OnMove;
        leftPunchAction.started -= OnLeftPunch;
        rightPunchAction.started -= OnRightPunch;
        leftKickAction.started -= OnLeftKick;
        rightKickAction.started -= OnRightKick;

        actionMap.Disable();
        MoveEvent?.Invoke(Vector2.zero);
    }

    private void OnDestroy()
    {
        controls?.Dispose();
    }

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        switch (change)
        {
            case InputDeviceChange.Added:
            case InputDeviceChange.Removed:
            case InputDeviceChange.Reconnected:
            case InputDeviceChange.Disconnected:
            case InputDeviceChange.Enabled:
            case InputDeviceChange.Disabled:
                RefreshDeviceRouting();
                break;
        }
    }

    /// <summary>
    /// Decides which devices this slot listens to. With fewer than two controllers Player 1 is left
    /// unrestricted (every device reaches it) and Player 2 keeps the keyboard only, so a single pad
    /// never drives both fighters. With two or more controllers each slot gets the keyboard plus the
    /// controller matching its index in connection order. The routing is only pushed to the asset,
    /// and only logged, when the resolved device set actually differs from the one in place, so the
    /// per-round enable/disable cycle driven by the match flow neither allocates nor spams the log.
    /// </summary>
    private void RefreshDeviceRouting()
    {
        CollectConnectedControllers();
        listenedDevices.Clear();

        bool splitControllers = connectedControllers.Count >= 2;
        bool unrestricted = !splitControllers && playerSlot == PlayerSlot.Player1;

        if (Keyboard.current != null) listenedDevices.Add(Keyboard.current);

        if (unrestricted)
        {
            for (int i = 0; i < connectedControllers.Count; i++) listenedDevices.Add(connectedControllers[i]);
        }
        else if (splitControllers)
        {
            int controllerIndex = playerSlot == PlayerSlot.Player1 ? 0 : 1;
            listenedDevices.Add(connectedControllers[controllerIndex]);
        }

        if (routedDevices != null && unrestricted == routedUnrestricted && IsSameDeviceSet(listenedDevices, routedDevices)) return;

        routedDevices = listenedDevices.ToArray();
        routedUnrestricted = unrestricted;

        if (unrestricted) controls.devices = null;
        else controls.devices = routedDevices;

        LogDeviceRouting();
    }

    private static bool IsSameDeviceSet(List<InputDevice> current, InputDevice[] previous)
    {
        if (current.Count != previous.Length) return false;

        for (int i = 0; i < previous.Length; i++)
        {
            if (!ReferenceEquals(current[i], previous[i])) return false;
        }

        return true;
    }

    /// <summary>
    /// Gathers every usable controller regardless of how the platform classified it. XInput and
    /// native HID pads arrive as <see cref="Gamepad"/>; DirectInput and unrecognised HID pads arrive
    /// as <see cref="Joystick"/>. Sorted by device id, which follows connection order.
    /// </summary>
    private void CollectConnectedControllers()
    {
        connectedControllers.Clear();

        ReadOnlyArray<InputDevice> devices = InputSystem.devices;
        for (int i = 0; i < devices.Count; i++)
        {
            InputDevice device = devices[i];
            if (!device.added || !device.enabled) continue;
            if (device is not Gamepad && device is not Joystick) continue;
            if (connectedControllers.Count == MaxControllersPerRefresh) break;

            connectedControllers.Add(device);
        }

        connectedControllers.Sort(DeviceIdOrder);
    }

    private static int CompareByDeviceId(InputDevice a, InputDevice b) => a.deviceId.CompareTo(b.deviceId);

    private void LogDeviceRouting()
    {
        deviceReport.Clear();
        deviceReport.Append("Input routing: listening to [");
        for (int i = 0; i < listenedDevices.Count; i++) AppendDeviceName(listenedDevices[i]);
        deviceReport.Append("] (").Append(playerSlot == PlayerSlot.Player1 ? "Player1" : "Player2").Append(')');
        Debug.Log(deviceReport.ToString(), this);
    }

    private void AppendDeviceName(InputDevice device)
    {
        if (deviceReport[deviceReport.Length - 1] != '[') deviceReport.Append(", ");
        deviceReport.Append(device.displayName);
    }

    private void OnMove(InputAction.CallbackContext context) => MoveEvent?.Invoke(context.ReadValue<Vector2>());
    private void OnLeftPunch(InputAction.CallbackContext context) => LeftPunchEvent?.Invoke();
    private void OnRightPunch(InputAction.CallbackContext context) => RightPunchEvent?.Invoke();
    private void OnLeftKick(InputAction.CallbackContext context) => LeftKickEvent?.Invoke();
    private void OnRightKick(InputAction.CallbackContext context) => RightKickEvent?.Invoke();
}
