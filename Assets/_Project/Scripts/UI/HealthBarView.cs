using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Life bar for one fighter: a foreground fill that snaps to the current health, a ghost fill
/// that lingers on the previous value before draining down so the size of a hit stays readable
/// through hitstop, and the round-win pips. Fill direction is authored on the foreground image in
/// the scene (Player 1 drains towards the left edge, Player 2 towards the right), so the view only
/// writes fill amounts and never touches layout.
/// </summary>
public class HealthBarView : MonoBehaviour
{
    [Header("Bars")]
    [SerializeField] private Image foregroundHealthBar;
    [SerializeField] private Image ghostHealthBar;

    [Header("Identity")]
    [SerializeField] private TMPro.TMP_Text playerNameLabel;

    [Header("Round Pips")]
    [SerializeField] private Image[] winPips;
    [SerializeField] private Color activePipColor = new Color(0f, 0.9f, 1f);
    [SerializeField] private Color inactivePipColor = new Color(0.16f, 0.18f, 0.22f);

    [Header("Ghost Bar")]
    [Tooltip("Seconds the ghost fill holds its position after a hit before draining.")]
    [SerializeField] private float ghostDelay = 0.4f;
    [Tooltip("Fill units per second the ghost drains at, on unscaled time.")]
    [SerializeField] private float ghostDrainSpeed = 1.2f;

    private HealthComponent boundHealth;
    private float ghostHoldTimer;

    public HealthComponent BoundHealth => boundHealth;
    public float ForegroundFill => foregroundHealthBar != null ? foregroundHealthBar.fillAmount : 0f;
    public float GhostFill => ghostHealthBar != null ? ghostHealthBar.fillAmount : 0f;

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

    private void OnDisable()
    {
        Unbind();
    }

    private void Update()
    {
        if (ghostHealthBar == null || foregroundHealthBar == null) return;

        float target = foregroundHealthBar.fillAmount;
        if (ghostHealthBar.fillAmount <= target) return;

        if (ghostHoldTimer > 0f)
        {
            ghostHoldTimer -= Time.unscaledDeltaTime;
            return;
        }

        ghostHealthBar.fillAmount = Mathf.MoveTowards(ghostHealthBar.fillAmount, target, ghostDrainSpeed * Time.unscaledDeltaTime);
    }

    private void HandleHealthChanged(float current, float max)
    {
        float fill = max > 0f ? current / max : 0f;
        bool healed = fill > foregroundHealthBar.fillAmount;
        SetFill(fill, healed);
    }

    /// <summary>
    /// Damage leaves the ghost where it was and starts the hold; healing (round reset) snaps
    /// both fills so a fresh round never opens with a draining bar.
    /// </summary>
    private void SetFill(float fill, bool snapGhost)
    {
        if (foregroundHealthBar != null) foregroundHealthBar.fillAmount = fill;
        if (ghostHealthBar == null) return;

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
}
