using UnityEngine;

// Body tint through a MaterialPropertyBlock so materials are never instanced.
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

    // minVisible makes very short pulses still readable by fading out instead of cutting.
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
