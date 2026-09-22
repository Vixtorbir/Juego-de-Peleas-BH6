using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Heat gauge for one fighter, sitting under the life bar. The charge fill chases the meter value
/// so gains read as a sweep rather than a snap, a lead segment sits ahead of the fill to give the
/// sweep a bright edge, and the whole bar breathes once the gauge is full so a ready super reads
/// at a glance. Fill direction and the angled silhouette are authored on the images in the scene,
/// so the view only writes fill amounts, colours and label text. Every animation runs on unscaled
/// time and the percentage label is served from a prebuilt table, so a charging gauge allocates
/// nothing.
/// </summary>
public class SuperGaugeView : MonoBehaviour
{
    private const int PercentSteps = 101;
    private const float FullPulseFrequency = 1.6f;

    private static readonly string[] PercentLookup = BuildPercentLookup();

    [Header("Bars")]
    [SerializeField] private Image chargeFill;
    [SerializeField] private Image leadFill;

    [Header("Readout")]
    [SerializeField] private TMPro.TMP_Text chargeLabel;

    [Header("Colours")]
    [SerializeField] private Color chargeColor = new Color(0.15f, 0.72f, 1f);
    [SerializeField] private Color fullColor = new Color(0.6f, 0.95f, 1f);

    [Header("Motion")]
    [Tooltip("Fill units per second the bar sweeps towards the meter value, on unscaled time.")]
    [SerializeField] private float sweepSpeed = 1.1f;
    [Tooltip("Extra fill the bright lead segment holds ahead of the charge fill.")]
    [SerializeField] private float leadWidth = 0.035f;

    private SuperMeterComponent boundMeter;
    private float targetFill;
    private int displayedPercent = -1;

    public SuperMeterComponent BoundMeter => boundMeter;
    public float ChargeFill => chargeFill != null ? chargeFill.fillAmount : 0f;
    public bool IsFull => boundMeter != null && boundMeter.IsFull;

    /// <summary>
    /// Subscribes to the fighter's meter and mirrors its current charge at once, so the gauge is
    /// correct even when the HUD comes up mid-round.
    /// </summary>
    public void Bind(SuperMeterComponent meter)
    {
        Unbind();
        boundMeter = meter;
        if (boundMeter == null) return;

        boundMeter.OnChargeChanged += HandleChargeChanged;
        WarmLabelBuffers();
        SetCharge(boundMeter.NormalizedCharge, true);
    }

    public void Unbind()
    {
        if (boundMeter == null) return;

        boundMeter.OnChargeChanged -= HandleChargeChanged;
        boundMeter = null;
    }

    private void OnDisable()
    {
        Unbind();
    }

    private void Update()
    {
        TickSweep(Time.unscaledDeltaTime);
        TickFullPulse();
    }

    private void TickSweep(float deltaTime)
    {
        if (chargeFill == null) return;

        if (!Mathf.Approximately(chargeFill.fillAmount, targetFill))
        {
            chargeFill.fillAmount = Mathf.MoveTowards(chargeFill.fillAmount, targetFill, sweepSpeed * deltaTime);
        }

        if (leadFill != null)
        {
            leadFill.fillAmount = Mathf.Min(1f, chargeFill.fillAmount + leadWidth);
            Color lead = leadFill.color;
            lead.a = Mathf.Abs(targetFill - chargeFill.fillAmount) > 0.001f ? 1f : 0f;
            leadFill.color = lead;
        }

        PublishPercent(chargeFill.fillAmount);
    }

    /// <summary>
    /// A full gauge breathes between the charge and full colours so the ready state is visible
    /// without a separate flashing element competing with the life bar.
    /// </summary>
    private void TickFullPulse()
    {
        if (chargeFill == null) return;

        if (!IsFull)
        {
            if (chargeFill.color != chargeColor) chargeFill.color = chargeColor;
            return;
        }

        float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * FullPulseFrequency * Mathf.PI * 2f);
        chargeFill.color = Color.Lerp(chargeColor, fullColor, wave);
    }

    private void HandleChargeChanged(float current, float max)
    {
        SetCharge(max > 0f ? current / max : 0f, current <= 0f);
    }

    private void SetCharge(float normalized, bool snap)
    {
        targetFill = Mathf.Clamp01(normalized);
        if (!snap || chargeFill == null) return;

        chargeFill.fillAmount = targetFill;
        if (leadFill != null) leadFill.fillAmount = targetFill;
        PublishPercent(targetFill);
    }

    private void PublishPercent(float fill)
    {
        if (chargeLabel == null) return;

        int percent = Mathf.Clamp(Mathf.RoundToInt(fill * 100f), 0, PercentSteps - 1);
        if (percent == displayedPercent) return;

        displayedPercent = percent;
        chargeLabel.SetText(PercentLookup[percent]);
    }

    /// <summary>
    /// TMP grows its glyph buffers to the longest text it has parsed, and "100" is the only
    /// three-digit label. Parsing it once at bind time keeps that growth out of the first full
    /// gauge of the fight; the snap that follows publishes the real value.
    /// </summary>
    private void WarmLabelBuffers()
    {
        if (chargeLabel == null) return;

        chargeLabel.SetText(PercentLookup[PercentSteps - 1]);
        chargeLabel.ForceMeshUpdate(true, true);
        displayedPercent = -1;
    }

    private static string[] BuildPercentLookup()
    {
        var lookup = new string[PercentSteps];
        for (int i = 0; i < lookup.Length; i++) lookup[i] = i.ToString();
        return lookup;
    }
}
