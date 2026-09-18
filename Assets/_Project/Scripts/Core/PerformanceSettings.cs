using UnityEngine;

/// <summary>
/// Locks the frame pacing the combat rules are authored against before the first scene loads.
/// Frame data is expressed in seconds, but hit detection runs on the physics step, so the fixed
/// timestep is pinned to 60 Hz to keep trigger timing identical on every machine. VSync provides
/// the pacing on desktop; the target frame rate is the fallback for platforms or drivers where
/// VSync is unavailable, so the game never spins at an uncapped rate.
/// </summary>
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
