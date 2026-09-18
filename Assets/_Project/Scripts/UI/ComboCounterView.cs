using UnityEngine;

/// <summary>
/// Combo readout for the hits one fighter lands on the other. Hidden until a combo reaches two
/// hits, then every hit re-fires a scale punch and updates the hit and damage labels from
/// pre-built string tables, and the panel fades out when the defender regains control. All motion
/// runs on unscaled time so the counter keeps animating through hitstop.
/// </summary>
public class ComboCounterView : MonoBehaviour
{
    private const int MinDisplayedHits = 2;
    private const int MaxHits = 99;
    private const int MaxDamage = 999;

    private static readonly string[] HitsLookup = BuildHitsLookup();
    private static readonly string[] DamageLookup = BuildDamageLookup();

    [Header("References")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMPro.TMP_Text hitsLabel;
    [SerializeField] private TMPro.TMP_Text damageLabel;
    [SerializeField] private RectTransform punchTransform;

    [Header("Motion")]
    [SerializeField] private float punchScale = 1.35f;
    [SerializeField] private float punchDuration = 0.15f;
    [SerializeField] private float fadeDuration = 0.3f;

    private Hurtbox boundHurtbox;
    private float punchTimer;
    private float fadeTimer;
    private bool visible;

    public Hurtbox BoundHurtbox => boundHurtbox;
    public bool IsVisible => visible;
    public float Alpha => canvasGroup != null ? canvasGroup.alpha : 0f;
    public float FadeDuration => fadeDuration;

    /// <summary>
    /// Listens to the hurtbox of the fighter being hit; the counter therefore belongs to the
    /// attacker, which is why the HUD binds each view to the opposite fighter's hurtbox.
    /// </summary>
    public void Bind(Hurtbox hurtbox)
    {
        Unbind();
        boundHurtbox = hurtbox;
        if (boundHurtbox == null) return;

        boundHurtbox.OnComboHit += HandleComboHit;
        boundHurtbox.OnComboEnd += HandleComboEnd;
    }

    public void Unbind()
    {
        if (boundHurtbox == null) return;

        boundHurtbox.OnComboHit -= HandleComboHit;
        boundHurtbox.OnComboEnd -= HandleComboEnd;
        boundHurtbox = null;
    }

    private void Awake()
    {
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        visible = false;
    }

    private void OnDisable()
    {
        Unbind();
    }

    private void Update()
    {
        float deltaTime = Time.unscaledDeltaTime;

        if (punchTimer > 0f && punchTransform != null)
        {
            punchTimer = Mathf.Max(0f, punchTimer - deltaTime);
            float progress = 1f - punchTimer / punchDuration;
            float scale = Mathf.LerpUnclamped(punchScale, 1f, EaseOutBack(progress));
            punchTransform.localScale = new Vector3(scale, scale, 1f);
        }

        if (fadeTimer > 0f && canvasGroup != null)
        {
            fadeTimer = Mathf.Max(0f, fadeTimer - deltaTime);
            canvasGroup.alpha = fadeTimer / fadeDuration;
        }
    }

    private void HandleComboHit(int hitCount, float totalDamage)
    {
        if (hitCount < MinDisplayedHits) return;

        if (hitsLabel != null) hitsLabel.text = HitsLookup[Mathf.Min(hitCount, MaxHits)];
        if (damageLabel != null) damageLabel.text = DamageLookup[Mathf.Clamp(Mathf.RoundToInt(totalDamage), 0, MaxDamage)];

        visible = true;
        fadeTimer = 0f;
        punchTimer = punchDuration;
        if (canvasGroup != null) canvasGroup.alpha = 1f;
    }

    private void HandleComboEnd()
    {
        if (!visible) return;

        visible = false;
        fadeTimer = fadeDuration;
    }

    private static float EaseOutBack(float t)
    {
        const float overshoot = 1.7f;
        float inverse = t - 1f;
        return 1f + inverse * inverse * ((overshoot + 1f) * inverse + overshoot);
    }

    private static string[] BuildHitsLookup()
    {
        var lookup = new string[MaxHits + 1];
        for (int i = 0; i < lookup.Length; i++) lookup[i] = i + " HITS";
        return lookup;
    }

    private static string[] BuildDamageLookup()
    {
        var lookup = new string[MaxDamage + 1];
        for (int i = 0; i < lookup.Length; i++) lookup[i] = i + " DMG";
        return lookup;
    }
}
