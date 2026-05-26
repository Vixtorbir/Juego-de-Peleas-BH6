using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputReader : MonoBehaviour
{
    public enum PlayerType { Player1, Player2 }

    [Header("Asignación de Controles")]
    public PlayerType tipoJugador = PlayerType.Player1;

    public event Action<Vector2> MoveEvent;
    public event Action LeftPunchEvent;
    public event Action RightPunchEvent;
    public event Action LeftKickEvent;
    public event Action RightKickEvent;

    private FighterInputActions controls;

    private void Awake()
    {
        controls = new FighterInputActions();

        // --- FILTRO DE DISPOSITIVOS (TECLADO + MANDO ASIGNADO) ---
        List<InputDevice> dispositivosPermitidos = new List<InputDevice>();

        if (Keyboard.current != null)
            dispositivosPermitidos.Add(Keyboard.current);

        // Asignación de Mandos (Gamepads)
        if (tipoJugador == PlayerType.Player1)
        {
            if (Gamepad.all.Count > 0)
                dispositivosPermitidos.Add(Gamepad.all[0]);
        }
        else if (tipoJugador == PlayerType.Player2)
        {
            if (Gamepad.all.Count > 1)
                dispositivosPermitidos.Add(Gamepad.all[1]);
        }

        controls.devices = dispositivosPermitidos.ToArray();
    }

    private void OnEnable()
    {
        controls.Enable();

        if (tipoJugador == PlayerType.Player1)
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
        if (controls != null)
        {
            if (tipoJugador == PlayerType.Player1)
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
        }
    }

    private void OnMove(InputAction.CallbackContext context) => MoveEvent?.Invoke(context.ReadValue<Vector2>());
    private void OnLeftPunch(InputAction.CallbackContext context) => LeftPunchEvent?.Invoke();
    private void OnRightPunch(InputAction.CallbackContext context) => RightPunchEvent?.Invoke();
    private void OnLeftKick(InputAction.CallbackContext context) => LeftKickEvent?.Invoke();
    private void OnRightKick(InputAction.CallbackContext context) => RightKickEvent?.Invoke();
}