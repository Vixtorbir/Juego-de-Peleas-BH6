using System;
using UnityEngine;

/// <summary>
/// Super gauge for a fighter. Charge is awarded by the damage resolver on every registered strike:
/// the attacker is paid more than the defender so offence builds the meter faster than turtling,
/// and blocked chip pays both sides a fraction of the hit rate. Specials spend whatever charge is
/// banked without gating the move, so the gauge reads as a reward for pressure rather than as a
/// resource that can lock a fighter out of its own moveset. Changes are broadcast for UI binding
/// in the same shape as <see cref="HealthComponent.OnHealthChanged"/>.
/// </summary>
public class SuperMeterComponent : MonoBehaviour
{
    [Header("Capacity")]
    [SerializeField] private float maxCharge = 100f;

    [Header("Gain Rates")]
    [Tooltip("Charge awarded to the attacker per point of damage dealt.")]
    [SerializeField] private float chargePerDamageDealt = 0.9f;
    [Tooltip("Charge awarded to the defender per point of damage taken.")]
    [SerializeField] private float chargePerDamageTaken = 0.45f;
    [Tooltip("Fraction of the normal rate paid out when the strike was blocked.")]
    [SerializeField] private float blockedChargeRatio = 0.3f;

    [Header("Spending")]
    [Tooltip("Charge a special move drains when it starts.")]
    [SerializeField] private float specialCost = 34f;

    private float currentCharge;

    /// <summary>Raised with the current and maximum charge whenever the gauge moves.</summary>
    public event Action<float, float> OnChargeChanged;

    /// <summary>Raised when the gauge reaches capacity, for the full-meter telegraph on the HUD.</summary>
    public event Action OnChargeFull;

    public float CurrentCharge => currentCharge;
    public float MaxCharge => maxCharge;
    public float NormalizedCharge => maxCharge > 0f ? currentCharge / maxCharge : 0f;
    public bool IsFull => currentCharge >= maxCharge;
    public float SpecialCost => specialCost;

    private void Start()
    {
        OnChargeChanged?.Invoke(currentCharge, maxCharge);
    }

    public void AwardForDamageDealt(float damage, bool blocked)
    {
        Award(damage * chargePerDamageDealt, blocked);
    }

    public void AwardForDamageTaken(float damage, bool blocked)
    {
        Award(damage * chargePerDamageTaken, blocked);
    }

    /// <summary>
    /// Drains up to <paramref name="cost"/> and reports whether the gauge could cover it in full.
    /// Callers that do not gate on the return value still get the partial drain.
    /// </summary>
    public bool TrySpend(float cost)
    {
        if (cost <= 0f) return true;

        bool covered = currentCharge >= cost;
        SetCharge(currentCharge - Mathf.Min(cost, currentCharge));
        return covered;
    }

    public void SpendForSpecial()
    {
        TrySpend(specialCost);
    }

    public void ResetCharge()
    {
        SetCharge(0f);
    }

    private void Award(float amount, bool blocked)
    {
        if (amount <= 0f) return;

        SetCharge(currentCharge + (blocked ? amount * blockedChargeRatio : amount));
    }

    private void SetCharge(float value)
    {
        float clamped = Mathf.Clamp(value, 0f, maxCharge);
        if (Mathf.Approximately(clamped, currentCharge)) return;

        bool wasFull = IsFull;
        currentCharge = clamped;
        OnChargeChanged?.Invoke(currentCharge, maxCharge);

        if (!wasFull && IsFull) OnChargeFull?.Invoke();
    }
}
