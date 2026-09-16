using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// Bridge between the generated <see cref="FighterInputActions"/> asset and gameplay code.
/// Each instance is bound to a player slot and restricts its action map to the keyboard plus
/// the gamepad assigned to that slot, so both fighters can be driven on the same machine.
/// </summary>
public class InputReader : MonoBehaviour
{
    public enum PlayerSlot { Player1, Player2 }

    [Header("Player Assignment")]
    [FormerlySerializedAs("tipoJugador")]
    public PlayerSlot playerSlot = PlayerSlot.Player1;

    public event Action<Vector2> MoveEvent;
    public event Action LeftPunchEvent;
    public event Action RightPunchEvent;
    public event Action LeftKickEvent;
    public event Action RightKickEvent;

    private FighterInputActions controls;

    private void Awake()
    {
        controls = new FighterInputActions();
        controls.devices = BuildAllowedDevices();
    }

    private void OnEnable()
    {
        controls.Enable();

        if (playerSlot == PlayerSlot.Player1)
        {
            controls.Player1.Movement.performed += OnMove;
            controls.Player1.Movement.canceled += OnMove;
            controls.Player1.LeftPunch.started += OnLeftPunch;
            controls.Player1.RightPunch.started += OnRightPunch;
            controls.Player1.LeftKick.started += OnLeftKick;
            controls.Player1.RightKick.started += OnRightKick;
        }
        else
        {
            controls.Player2.Movement.performed += OnMove;
            controls.Player2.Movement.canceled += OnMove;
            controls.Player2.LeftPunch.started += OnLeftPunch;
            controls.Player2.RightPunch.started += OnRightPunch;
            controls.Player2.LeftKick.started += OnLeftKick;
            controls.Player2.RightKick.started += OnRightKick;
        }
    }

    private void OnDisable()
    {
        if (controls == null) return;

        if (playerSlot == PlayerSlot.Player1)
        {
            controls.Player1.Movement.performed -= OnMove;
            controls.Player1.Movement.canceled -= OnMove;
            controls.Player1.LeftPunch.started -= OnLeftPunch;
            controls.Player1.RightPunch.started -= OnRightPunch;
            controls.Player1.LeftKick.started -= OnLeftKick;
            controls.Player1.RightKick.started -= OnRightKick;
        }
        else
        {
            controls.Player2.Movement.performed -= OnMove;
            controls.Player2.Movement.canceled -= OnMove;
            controls.Player2.LeftPunch.started -= OnLeftPunch;
            controls.Player2.RightPunch.started -= OnRightPunch;
            controls.Player2.LeftKick.started -= OnLeftKick;
            controls.Player2.RightKick.started -= OnRightKick;
        }

        controls.Disable();
        MoveEvent?.Invoke(Vector2.zero);
    }

    private void OnDestroy()
    {
        controls?.Dispose();
    }

    private InputDevice[] BuildAllowedDevices()
    {
        var devices = new List<InputDevice>(2);

        if (Keyboard.current != null)
        {
            devices.Add(Keyboard.current);
        }

        int gamepadIndex = playerSlot == PlayerSlot.Player1 ? 0 : 1;
        if (Gamepad.all.Count > gamepadIndex)
        {
            devices.Add(Gamepad.all[gamepadIndex]);
        }

        return devices.ToArray();
    }

    private void OnMove(InputAction.CallbackContext context) => MoveEvent?.Invoke(context.ReadValue<Vector2>());
    private void OnLeftPunch(InputAction.CallbackContext context) => LeftPunchEvent?.Invoke();
    private void OnRightPunch(InputAction.CallbackContext context) => RightPunchEvent?.Invoke();
    private void OnLeftKick(InputAction.CallbackContext context) => LeftKickEvent?.Invoke();
    private void OnRightKick(InputAction.CallbackContext context) => RightKickEvent?.Invoke();
}
