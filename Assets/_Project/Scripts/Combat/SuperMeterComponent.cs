using System;
using UnityEngine;

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

    public event Action<float, float> OnChargeChanged;

    public event Action OnChargeFull;

    public float CurrentCharge => currentCharge;
    public float MaxCharge => maxCharge;
    public float NormalizedCharge => maxCharge > 0f ? currentCharge / maxCharge : 0f;
    public bool IsFull => currentCharge >= maxCharge;

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

    public bool TrySpend(float cost)
    {
        if (cost <= 0f) return true;

        bool covered = currentCharge >= cost;
        SetCharge(currentCharge - Mathf.Min(cost, currentCharge));
        return covered;
    }

    // Only drains if the full cost is available, so a refused super never costs meter.
    public bool TrySpendExact(float cost)
    {
        if (cost > currentCharge) return false;

        SetCharge(currentCharge - cost);
        return true;
    }

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
