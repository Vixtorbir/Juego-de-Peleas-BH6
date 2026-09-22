using UnityEngine;

public class ComboCounterView : MonoBehaviour
{
    private const int MinDisplayedHits = 2;
    private const int MaxHits = 99;
    private const int MaxDamage = 999;
    private const string CounterTag = "COUNTER";
    private const string ChipTag = "CHIP";

    private static readonly string[] HitsLookup = BuildLookup(MaxHits, "", " HITS");
    private static readonly string[] DamageLookup = BuildLookup(MaxDamage, "", " DMG");
    private static readonly string[] ReadoutLookup = BuildLookup(MaxDamage, "-", "");

    [Header("Combo Counter")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMPro.TMP_Text hitsLabel;
    [SerializeField] private TMPro.TMP_Text damageLabel;
    [SerializeField] private RectTransform punchTransform;

    [Header("Hit Readout")]
    [SerializeField] private CanvasGroup readoutGroup;
    [SerializeField] private RectTransform readoutTransform;
    [SerializeField] private TMPro.TMP_Text hitTagLabel;
    [SerializeField] private TMPro.TMP_Text hitDamageLabel;
    [SerializeField] private Color hitColor = Color.white;
    [SerializeField] private Color counterHitColor = new Color(1f, 0.55f, 0.2f);
    [SerializeField] private Color chipColor = new Color(0.6f, 0.75f, 0.9f);

    [Header("Motion")]
    [SerializeField] private float punchScale = 1.35f;
    [SerializeField] private float punchDuration = 0.15f;
    [SerializeField] private float fadeDuration = 0.3f;
    [SerializeField] private float readoutPunchScale = 1.5f;
    [SerializeField] private float readoutPunchDuration = 0.25f;
    [SerializeField] private float readoutHold = 0.6f;
    [SerializeField] private float readoutFadeDuration = 0.2f;

    private Hurtbox boundHurtbox;
    private float punchTimer;
    private float fadeTimer;
    private bool visible;
    private float readoutPunchTimer;
    private float readoutHoldTimer;
    private float readoutFadeTimer;
    private bool readoutVisible;

    public Hurtbox BoundHurtbox => boundHurtbox;
    public bool IsVisible => visible;
    public float Alpha => canvasGroup != null ? canvasGroup.alpha : 0f;
    public float FadeDuration => fadeDuration;
    public bool IsReadoutVisible => readoutVisible;
    public float ReadoutAlpha => readoutGroup != null ? readoutGroup.alpha : 0f;
    public string ReadoutText => hitDamageLabel != null ? hitDamageLabel.text : string.Empty;
    public string ReadoutTag => hitTagLabel != null ? hitTagLabel.text : string.Empty;

    public void Bind(Hurtbox hurtbox)
    {
        Unbind();
        boundHurtbox = hurtbox;
        if (boundHurtbox == null) return;

        boundHurtbox.OnHitRegistered += HandleHitRegistered;
        boundHurtbox.OnComboHit += HandleComboHit;
        boundHurtbox.OnComboEnd += HandleComboEnd;
    }

    public void Unbind()
    {
        if (boundHurtbox == null) return;

        boundHurtbox.OnHitRegistered -= HandleHitRegistered;
        boundHurtbox.OnComboHit -= HandleComboHit;
        boundHurtbox.OnComboEnd -= HandleComboEnd;
        boundHurtbox = null;
    }

    private void Awake()
    {
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (readoutGroup != null) readoutGroup.alpha = 0f;
        visible = false;
        readoutVisible = false;
    }

    private void OnDisable()
    {
        Unbind();
    }

    private void Update()
    {
        float deltaTime = Time.unscaledDeltaTime;
        TickCounter(deltaTime);
        TickReadout(deltaTime);
    }

    private void TickCounter(float deltaTime)
    {
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

    private void TickReadout(float deltaTime)
    {
        if (readoutPunchTimer > 0f && readoutTransform != null)
        {
            readoutPunchTimer = Mathf.Max(0f, readoutPunchTimer - deltaTime);
            float progress = 1f - readoutPunchTimer / readoutPunchDuration;
            float scale = Mathf.LerpUnclamped(readoutPunchScale, 1f, EaseOutBack(progress));
            readoutTransform.localScale = new Vector3(scale, scale, 1f);
        }

        if (readoutHoldTimer > 0f)
        {
            readoutHoldTimer -= deltaTime;
            if (readoutHoldTimer <= 0f) readoutFadeTimer = readoutFadeDuration;
            return;
        }

        if (readoutFadeTimer > 0f && readoutGroup != null)
        {
            readoutFadeTimer = Mathf.Max(0f, readoutFadeTimer - deltaTime);
            readoutGroup.alpha = readoutFadeTimer / readoutFadeDuration;
            if (readoutFadeTimer <= 0f) readoutVisible = false;
        }
    }

    private void HandleHitRegistered(HitFeedback feedback)
    {
        int damage = Mathf.Clamp(Mathf.RoundToInt(feedback.Damage), 0, MaxDamage);
        Color tint = feedback.Blocked ? chipColor : feedback.CounterHit ? counterHitColor : hitColor;

        if (hitDamageLabel != null)
        {
            hitDamageLabel.text = ReadoutLookup[damage];
            hitDamageLabel.color = tint;
        }

        if (hitTagLabel != null)
        {
            hitTagLabel.text = feedback.Blocked ? ChipTag : feedback.CounterHit ? CounterTag : string.Empty;
            hitTagLabel.color = tint;
        }

        readoutVisible = true;
        readoutPunchTimer = readoutPunchDuration;
        readoutHoldTimer = readoutHold;
        readoutFadeTimer = 0f;
        if (readoutGroup != null) readoutGroup.alpha = 1f;
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

    private static string[] BuildLookup(int max, string prefix, string suffix)
    {
        var lookup = new string[max + 1];
        for (int i = 0; i < lookup.Length; i++) lookup[i] = prefix + i + suffix;
        return lookup;
    }
}
