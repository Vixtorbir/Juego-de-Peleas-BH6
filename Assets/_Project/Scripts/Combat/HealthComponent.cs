using System;
using UnityEngine;

/// <summary>
/// Health pool for a fighter. Broadcasts changes for UI binding and raises <see cref="OnDeath"/>
/// once when health reaches zero; further damage is ignored until the next round resets it. A
/// damage floor above zero makes the pool non-lethal, which training mode uses for its dummy.
/// </summary>
public class HealthComponent : MonoBehaviour
{
    [Header("Health Settings")]
    [SerializeField] private float maxHealth = 100f;

    private float currentHealth;
    private float damageFloor;

    public event Action<float, float> OnHealthChanged;
    public event Action OnDeath;

    public bool IsDead { get; private set; }
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public float DamageFloor => damageFloor;

    private void Awake()
    {
        currentHealth = maxHealth;
        IsDead = false;
    }

    private void Start()
    {
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void TakeDamage(float amount)
    {
        if (IsDead) return;

        currentHealth = Mathf.Clamp(currentHealth - amount, Mathf.Min(damageFloor, currentHealth), maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0f)
        {
            IsDead = true;
            OnDeath?.Invoke();
        }
    }

    /// <summary>Restores up to <paramref name="amount"/> health. A knocked-out fighter stays down.</summary>
    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f || currentHealth >= maxHealth) return;

        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    /// <summary>
    /// Sets the lowest value damage can take the pool to. Zero restores normal lethal damage; any
    /// positive floor keeps the fighter standing however hard it is hit.
    /// </summary>
    public void SetDamageFloor(float floor)
    {
        damageFloor = Mathf.Clamp(floor, 0f, maxHealth);
    }

    public void ResetHealth()
    {
        currentHealth = maxHealth;
        IsDead = false;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }
}
