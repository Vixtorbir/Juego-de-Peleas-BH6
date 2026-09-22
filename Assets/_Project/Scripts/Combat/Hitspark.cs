using UnityEngine;

public class Hitspark : MonoBehaviour
{
    private const float CoreStartScale = 0.04f;
    private const float CorePeakScale = 0.18f;
    private const float RayLength = 0.3f;
    private const float RayThickness = 0.018f;
    private const float LightPeakIntensity = 2.2f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [SerializeField] private Transform core;
    [SerializeField] private Transform[] rays;
    [SerializeField] private MeshRenderer[] renderers;
    [SerializeField] private Light flash;

    public void SetMaterial(Material material)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].sharedMaterial = material;
        }

        flash.color = material.GetColor(BaseColorId);
    }

    // Core pops open in the first third of the lifetime while the rays keep stretching and thinning out.
    public void Animate(float progress)
    {
        float attack = Mathf.Clamp01(progress * 3f);
        float decay = 1f - progress;

        float coreScale = Mathf.Lerp(CoreStartScale, CorePeakScale, EaseOut(attack)) * Mathf.Lerp(0.4f, 1f, decay);
        core.localScale = new Vector3(coreScale, coreScale, coreScale);

        float length = RayLength * EaseOut(progress);
        float thickness = RayThickness * decay;
        for (int i = 0; i < rays.Length; i++)
        {
            Transform ray = rays[i];
            ray.localScale = new Vector3(thickness, length, thickness);
            ray.localPosition = ray.localRotation * new Vector3(0f, length * 0.5f, 0f);
        }

        flash.intensity = LightPeakIntensity * decay * decay;
    }

    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
}
