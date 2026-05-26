using System.Collections.Generic;
using UnityEngine;

public enum AttackHeight
{
    High,
    Mid,
    Low
}

[System.Serializable]
public struct ComboBranch
{
    public FighterCommand inputCommand;
    public AttackData nextAttack;
}

[CreateAssetMenu(fileName = "NewAttackData", menuName = "Fighting Game/Attack Data")]
public class AttackData : ScriptableObject
{
    [Header("Animation & Visuals")]
    public string animationTrigger = "LeftPunch";
    public Vector3 visualScale = new Vector3(1f, 1f, 1.2f);

    [Header("Frame Data (en segundos)")]
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
}