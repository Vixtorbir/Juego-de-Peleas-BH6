using UnityEngine;

// Hit detection runs on the physics step, so the fixed timestep is pinned to 60 Hz to match the frame data.
public static class PerformanceSettings
{
    public const int TargetFrameRate = 60;
    public const float FixedTimestep = 1f / TargetFrameRate;
    private const int VSyncEveryRefresh = 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyFramePacing()
    {
        Application.targetFrameRate = TargetFrameRate;
        QualitySettings.vSyncCount = VSyncEveryRefresh;
        Time.fixedDeltaTime = FixedTimestep;
    }
}
