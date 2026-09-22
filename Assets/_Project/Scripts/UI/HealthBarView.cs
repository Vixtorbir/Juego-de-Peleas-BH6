using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Life bar for one fighter: a foreground fill that snaps to the current health, a ghost fill
/// that lingers on the previous value before draining down so the size of a hit stays readable
/// through hitstop, a white impact flash over the lost segment, and the round-win pips. Fill
/// direction and the angled silhouette are authored on the images in the scene (Player 1 drains
/// towards the left edge, Player 2 towards the right), so the view only writes fill amounts,
/// colours and alphas and never touches layout. Every animation runs on unscaled time.
/// </summary>
public class HealthBarView : MonoBehaviour
{
    private const float LowHealthRatio = 0.25f;
    private const float LowHealthPulseFrequency = 3f;
    private const float LowHealthPulseDepth = 0.35f;

    [Header("Bars")]
    [SerializeField] private Image foregroundHealthBar;
    [SerializeField] private Image ghostHealthBar;
    [SerializeField] private Image damageFlash;

    [Header("Identity")]
    [SerializeField] private TMPro.TMP_Text playerNameLabel;

    [Header("Round Pips")]
    [SerializeField] private Image[] winPips;
    [SerializeField] private Color activePipColor = new Color(0f, 0.9f, 1f);
    [SerializeField] private Color inactivePipColor = new Color(0.16f, 0.18f, 0.22f);

    [Header("Ghost Bar")]
    [Tooltip("Seconds the ghost fill holds its position after a hit before draining.")]
    [SerializeField] private float ghostDelay = 0.5f;
    [Tooltip("Fill units per second the ghost drains at, on unscaled time.")]
    [SerializeField] private float ghostDrainSpeed = 1.2f;

    [Header("Impact Flash")]
    [SerializeField] private float flashDuration = 0.12f;
    [SerializeField] private Color lowHealthColor = new Color(1f, 0.25f, 0.2f);

    private HealthComponent boundHealth;
    private float ghostHoldTimer;
    private float flashTimer;
    private Color foregroundColor;

    public HealthComponent BoundHealth => boundHealth;
    public float ForegroundFill => foregroundHealthBar != null ? foregroundHealthBar.fillAmount : 0f;
    public float GhostFill => ghostHealthBar != null ? ghostHealthBar.fillAmount : 0f;
    public float FlashAlpha => damageFlash != null ? damageFlash.color.a : 0f;
    public float GhostDelay => ghostDelay;

    /// <summary>
    /// Subscribes to the fighter's health and mirrors its current value at once, so the bar is
    /// correct even when the fighter took damage before the HUD came up.
    /// </summary>
    public void Bind(HealthComponent health)
    {
        Unbind();
        boundHealth = health;
        if (boundHealth == null) return;

        boundHealth.OnHealthChanged += HandleHealthChanged;
        SetFill(boundHealth.CurrentHealth / boundHealth.MaxHealth, true);
    }

    public void Unbind()
    {
        if (boundHealth == null) return;

        boundHealth.OnHealthChanged -= HandleHealthChanged;
        boundHealth = null;
    }

    public void SetPlayerName(string name)
    {
        if (playerNameLabel != null) playerNameLabel.text = name;
    }

    public void SetRoundWins(int wins)
    {
        if (winPips == null) return;

        for (int i = 0; i < winPips.Length; i++)
        {
            if (winPips[i] != null) winPips[i].color = i < wins ? activePipColor : inactivePipColor;
        }
    }

    private void Awake()
    {
        if (foregroundHealthBar != null) foregroundColor = foregroundHealthBar.color;
        SetFlashAlpha(0f);
    }

    private void OnDisable()
    {
        Unbind();
    }

    private void Update()
    {
        float deltaTime = Time.unscaledDeltaTime;
        TickFlash(deltaTime);
        TickLowHealthPulse();
        TickGhost(deltaTime);
    }

    private void TickGhost(float deltaTime)
    {
        if (ghostHealthBar == null || foregroundHealthBar == null) return;

        float target = foregroundHealthBar.fillAmount;
        if (ghostHealthBar.fillAmount <= target) return;

        if (ghostHoldTimer > 0f)
        {
            ghostHoldTimer -= deltaTime;
            return;
        }

        ghostHealthBar.fillAmount = Mathf.MoveTowards(ghostHealthBar.fillAmount, target, ghostDrainSpeed * deltaTime);
    }

    private void TickFlash(float deltaTime)
    {
        if (flashTimer <= 0f) return;

        flashTimer = Mathf.Max(0f, flashTimer - deltaTime);
        SetFlashAlpha(flashDuration > 0f ? flashTimer / flashDuration : 0f);
    }

    /// <summary>
    /// Below a quarter of the life bar the foreground breathes towards the low-health colour so the
    /// danger reads without a separate warning element.
    /// </summary>
    private void TickLowHealthPulse()
    {
        if (foregroundHealthBar == null) return;

        if (foregroundHealthBar.fillAmount > LowHealthRatio)
        {
            if (foregroundHealthBar.color != foregroundColor) foregroundHealthBar.color = foregroundColor;
            return;
        }

        float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * LowHealthPulseFrequency * Mathf.PI * 2f);
        foregroundHealthBar.color = Color.Lerp(foregroundColor, lowHealthColor, wave * LowHealthPulseDepth + (1f - LowHealthPulseDepth) * 0.5f);
    }

    private void HandleHealthChanged(float current, float max)
    {
        float fill = max > 0f ? current / max : 0f;
        bool healed = fill > foregroundHealthBar.fillAmount;
        SetFill(fill, healed);
    }

    /// <summary>
    /// Damage leaves the ghost where it was, starts the hold and flashes the bar; healing (round
    /// reset) snaps both fills so a fresh round never opens with a draining bar.
    /// </summary>
    private void SetFill(float fill, bool snapGhost)
    {
        if (foregroundHealthBar != null) foregroundHealthBar.fillAmount = fill;

        if (ghostHealthBar != null)
        {
            if (snapGhost)
            {
                ghostHealthBar.fillAmount = fill;
                ghostHoldTimer = 0f;
            }
            else
            {
                ghostHoldTimer = ghostDelay;
            }
        }

        if (snapGhost)
        {
            flashTimer = 0f;
            SetFlashAlpha(0f);
            return;
        }

        if (damageFlash != null) damageFlash.fillAmount = ghostHealthBar != null ? ghostHealthBar.fillAmount : fill;
        flashTimer = flashDuration;
        SetFlashAlpha(1f);
    }

    private void SetFlashAlpha(float alpha)
    {
        if (damageFlash == null) return;

        Color color = damageFlash.color;
        color.a = alpha;
        damageFlash.color = color;
    }
}
