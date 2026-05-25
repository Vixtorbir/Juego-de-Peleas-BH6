using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputReader : MonoBehaviour, FighterInputActions.IGameplayActions
{
    private FighterInputActions _fighterInput;

    public event Action<Vector2> MoveEvent;
    public event Action LeftPunchEvent;
    public event Action RightPunchEvent;
    public event Action LeftKickEvent;
    public event Action RightKickEvent;

    private void OnEnable()
    {
        if (_fighterInput == null)
        {
            _fighterInput = new FighterInputActions();
            _fighterInput.Gameplay.SetCallbacks(this);
        }

        _fighterInput.Gameplay.Enable();
    }

    private void OnDisable()
    {
        _fighterInput.Gameplay.Disable();
    }

    // --- IMPLEMENTACIÓN DE LA INTERFAZ IGameplayActions ---

    public void OnMovement(InputAction.CallbackContext context)
    {
        
        MoveEvent?.Invoke(context.ReadValue<Vector2>());
    }

    public void OnLeftPunch(InputAction.CallbackContext context)
    {
        if (context.phase == InputActionPhase.Started)
        {
            LeftPunchEvent?.Invoke();
        }
    }

    public void OnRightPunch(InputAction.CallbackContext context)
    {
        if (context.phase == InputActionPhase.Started)
        {
            RightPunchEvent?.Invoke();
        }
    }

    public void OnLeftKick(InputAction.CallbackContext context)
    {
        if (context.phase == InputActionPhase.Started)
        {
            LeftKickEvent?.Invoke();
        }
    }

    public void OnRightKick(InputAction.CallbackContext context)
    {
        if (context.phase == InputActionPhase.Started)
        {
            RightKickEvent?.Invoke();
        }
    }
}