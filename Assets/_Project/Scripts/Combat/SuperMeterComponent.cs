using System;
using UnityEngine;

/// <summary>
/// Super gauge for a fighter. Charge is awarded by the damage resolver on every registered strike:
/// the attacker is paid more than the defender so offence builds the meter faster than turtling,
/// and blocked chip pays both sides a fraction of the hit rate. Specials are free; the gauge is
/// reserved for the super art, which drains it in full, so a single clean combo is enough to bank
/// one. Changes are broadcast for UI binding in the same shape as
/// <see cref="HealthComponent.OnHealthChanged"/>.
/// </summary>
public class SuperMeterComponent : MonoBehaviour
{
    [Header("Capacity")]
    [SerializeField] private float maxCharge = 100f;

    [Header("Gain Rates")]
    [Tooltip("Charge awarded to the attacker per point of damage dealt.")]
    [SerializeField] private float chargePerDamageDealt = 2.8f;
    [Tooltip("Charge awarded to the defender per point of damage taken.")]
    [SerializeField] private float chargePerDamageTaken = 1f;
    [Tooltip("Fraction of the normal rate paid out when the strike was blocked.")]
    [SerializeField] private float blockedChargeRatio = 0.3f;

    private float currentCharge;

    /// <summary>Raised with the current and maximum charge whenever the gauge moves.</summary>
    public event Action<float, float> OnChargeChanged;

    /// <summary>Raised when the gauge reaches capacity, for the full-meter telegraph on the HUD.</summary>
    public event Action OnChargeFull;

    public float CurrentCharge => currentCharge;
    public float MaxCharge => maxCharge;
    public float NormalizedCharge => maxCharge > 0f ? currentCharge / maxCharge : 0f;
    public bool IsFull => currentCharge >= maxCharge;

    /// <summary>Charge a super art drains: the whole gauge.</summary>
    public float SuperArtCost => maxCharge;

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

    /// <summary>
    /// Atomic spend: the gauge is drained only when it covers <paramref name="cost"/> in full, so a
    /// move gated on its own cost never leaves the player paying for an activation that was refused.
    /// </summary>
    public bool TrySpendExact(float cost)
    {
        if (cost > currentCharge) return false;

        SetCharge(currentCharge - cost);
        return true;
    }

    /// <summary>Tops the gauge up to capacity. Used by training mode to rehearse the super art.</summary>
    public void FillCharge()
    {
        SetCharge(maxCharge);
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
