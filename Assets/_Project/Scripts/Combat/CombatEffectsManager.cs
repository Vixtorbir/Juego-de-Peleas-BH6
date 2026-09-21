using UnityEngine;

/// <summary>
/// Scene-wide cinematic time service. Per-hit hitstop is handled by each fighter's own freeze
/// (<see cref="RobotController.FreezeFor"/>), so the global time scale is reserved for beats that
/// must slow the whole scene at once: the knockout, and any future super freeze. The slow-motion
/// is tracked with an unscaled timer instead of a coroutine so triggering it never allocates, and
/// a longer beat always extends a shorter one.
/// </summary>
public class CombatEffectsManager : MonoBehaviour
{
    public static CombatEffectsManager Instance { get; private set; }

    public bool IsSlowMotionActive => slowMotionRemaining > 0f;

    private float slowMotionRemaining;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    /// <summary>
    /// A beat in flight must never outlive the manager: disabling it (scene unload, editor stop,
    /// object toggled) restores normal time so the next scene starts cleanly.
    /// </summary>
    private void OnDisable()
    {
        if (Instance != this) return;

        slowMotionRemaining = 0f;
        Time.timeScale = 1f;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Time.timeScale = 1f;
            Instance = null;
        }
    }

    private void Update()
    {
        if (slowMotionRemaining <= 0f) return;

        slowMotionRemaining -= Time.unscaledDeltaTime;
        if (slowMotionRemaining <= 0f)
        {
            slowMotionRemaining = 0f;
            Time.timeScale = 1f;
        }
    }

    /// <summary>
    /// Runs the scene at <paramref name="timeScale"/> for <paramref name="duration"/> seconds of
    /// real time. The slower scale wins while both are in flight.
    /// </summary>
    public void TriggerSlowMotion(float timeScale, float duration)
    {
        if (duration <= 0f) return;

        slowMotionRemaining = Mathf.Max(slowMotionRemaining, duration);
        Time.timeScale = IsSlowMotionActive ? Mathf.Min(Time.timeScale, timeScale) : timeScale;
    }
}
