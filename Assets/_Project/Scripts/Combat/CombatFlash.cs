using UnityEngine;

/// <summary>
/// Body tint telegraph for a fighter. Drives the renderers' base colour through one cached
/// <see cref="MaterialPropertyBlock"/>, so the shared materials are never instanced and nothing is
/// allocated per frame. Two layers share the block: a pulse held while the fighter cannot be hit
/// (wake-up, reversal startup) and a one-shot pop for events such as a deflected strike. Both run on
/// unscaled time so they stay readable through hitstop, and the block is cleared once idle so the
/// renderers return to the SRP Batcher.
/// </summary>
[DisallowMultipleComponent]
public class CombatFlash : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private Renderer[] renderers;
    private Color[] baseColors;
    private MaterialPropertyBlock block;

    private bool pulsing;
    private Color pulseColor;
    private float pulseFrequency;
    private float pulseTailDuration;
    private float pulseTailRemaining;

    private Color popColor;
    private float popDuration;
    private float popRemaining;

    private bool applied;

    /// <summary>True while any tint is being drawn on the body.</summary>
    public bool IsFlashing => pulsing || pulseTailRemaining > 0f || popRemaining > 0f;

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        renderers = GetComponentsInChildren<Renderer>(true);
        baseColors = new Color[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
        {
            Material material = renderers[i].sharedMaterial;
            baseColors[i] = material != null && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.white;
        }
    }

    private void OnDisable()
    {
        pulsing = false;
        pulseTailRemaining = 0f;
        popRemaining = 0f;
        Clear();
    }

    private void Update()
    {
        if (!IsFlashing)
        {
            if (applied) Clear();
            return;
        }

        float deltaTime = Time.unscaledDeltaTime;
        float intensity = 0f;
        Color color = popColor;

        if (pulsing || pulseTailRemaining > 0f)
        {
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * pulseFrequency * Mathf.PI * 2f);
            float tail = pulsing ? 1f : Mathf.Clamp01(pulseTailRemaining / Mathf.Max(pulseTailDuration, 0.0001f));
            intensity = wave * tail;
            color = pulseColor;
            if (!pulsing) pulseTailRemaining -= deltaTime;
        }

        if (popRemaining > 0f)
        {
            float pop = popRemaining / Mathf.Max(popDuration, 0.0001f);
            if (pop > intensity)
            {
                intensity = pop;
                color = popColor;
            }

            popRemaining -= deltaTime;
        }

        Apply(color, intensity);
    }

    /// <summary>
    /// Starts or stops the invulnerability pulse. Stopping lets the pulse fade over
    /// <paramref name="minVisible"/> so a pulse shorter than a few frames still registers.
    /// </summary>
    public void SetPulse(bool on, Color color, float frequency, float minVisible)
    {
        if (on)
        {
            pulsing = true;
            pulseColor = color;
            pulseFrequency = frequency;
            pulseTailRemaining = 0f;
            return;
        }

        if (!pulsing) return;

        pulsing = false;
        pulseTailDuration = minVisible;
        pulseTailRemaining = minVisible;
    }

    /// <summary>One-shot tint that decays linearly over <paramref name="duration"/>.</summary>
    public void Pop(Color color, float duration)
    {
        if (duration <= 0f) return;

        popColor = color;
        popDuration = duration;
        popRemaining = duration;
    }

    private void Apply(Color color, float intensity)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer target = renderers[i];
            if (target == null) continue;

            block.SetColor(BaseColorId, Color.Lerp(baseColors[i], color, intensity));
            target.SetPropertyBlock(block);
        }

        applied = true;
    }

    private void Clear()
    {
        if (renderers == null) return;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null) renderers[i].SetPropertyBlock(null);
        }

        applied = false;
    }
}
