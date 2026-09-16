using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Guard height an attack must be blocked at. High attacks whiff on crouching opponents,
/// Mid attacks are blocked standing only, Low attacks are blocked crouching only.
/// </summary>
public enum AttackHeight
{
    High,
    Mid,
    Low
}

/// <summary>
/// Link from one attack to the next when the given command is buffered during recovery.
/// </summary>
[System.Serializable]
public struct ComboBranch
{
    public FighterCommand inputCommand;
    public AttackData nextAttack;
}

/// <summary>
/// Designer-authored frame data and hit properties for a single attack.
/// Timings are expressed in seconds so they remain frame-rate independent.
/// </summary>
[CreateAssetMenu(fileName = "NewAttackData", menuName = "Fighting Game/Attack Data")]
public class AttackData : ScriptableObject
{
    [Header("Animation")]
    public string animationTrigger = "LeftPunch";

    [Header("Frame Data (seconds)")]
    public float startupTime = 0.15f;
    public float activeTime = 0.1f;
    public float recoveryTime = 0.25f;

    [Header("Combat Stats")]
    public float damage = 10f;
    public float hitstunDuration = 0.4f;

    [Header("Guard & Block Settings")]
    public AttackHeight attackHeight = AttackHeight.Mid;
    public float blockstunDuration = 0.25f;

    [Header("Pushback Settings")]
    public float hitPushback = 8f;
    public float blockPushback = 4f;

    [Header("Launcher Settings")]
    public bool isLauncher = false;
    public float launchForce = 15f;

    [Header("Combo Routes")]
    public List<ComboBranch> comboBranches;

    public float ActiveEndTime => startupTime + activeTime;
    public float TotalDuration => startupTime + activeTime + recoveryTime;
}
