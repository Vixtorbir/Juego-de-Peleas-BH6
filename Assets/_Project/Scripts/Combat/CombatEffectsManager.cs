using UnityEngine;

/// <summary>
/// Scene-wide cinematic time service and the single owner of <see cref="Time.timeScale"/>.
/// Per-hit hitstop is handled by each fighter's own freeze (<see cref="RobotController.FreezeFor"/>),
/// so the global time scale is reserved for beats that must slow the whole scene at once (the
/// knockout and the super freeze) and for the match pause. The slow-motion is tracked with an
/// unscaled timer instead of a coroutine so triggering it never allocates, and a longer beat always
/// extends a shorter one. A pause holds any beat in flight and hands it back intact on resume.
/// </summary>
public class CombatEffectsManager : MonoBehaviour
{
    public static CombatEffectsManager Instance { get; private set; }

    public bool IsSlowMotionActive => slowMotionRemaining > 0f;
    public bool IsPaused { get; private set; }

    /// <summary>Real-time seconds of slow-motion still to serve; frozen while paused.</summary>
    public float SlowMotionRemaining => slowMotionRemaining;

    private float slowMotionRemaining;
    private float slowMotionScale = 1f;

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
    /// A beat or pause in flight must never outlive the manager: disabling it (scene unload, editor
    /// stop, object toggled) restores normal time so the next scene starts cleanly.
    /// </summary>
    private void OnDisable()
    {
        if (Instance != this) return;

        slowMotionRemaining = 0f;
        slowMotionScale = 1f;
        IsPaused = false;
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
        if (IsPaused || slowMotionRemaining <= 0f) return;

        slowMotionRemaining -= Time.unscaledDeltaTime;
        if (slowMotionRemaining <= 0f)
        {
            slowMotionRemaining = 0f;
            slowMotionScale = 1f;
            Time.timeScale = 1f;
        }
    }

    /// <summary>
    /// Runs the scene at <paramref name="timeScale"/> for <paramref name="duration"/> seconds of
    /// real time. The slower scale wins while both are in flight. A beat triggered while paused is
    /// recorded and only applied once the pause lifts.
    /// </summary>
    public void TriggerSlowMotion(float timeScale, float duration)
    {
        if (duration <= 0f) return;

        slowMotionScale = IsSlowMotionActive ? Mathf.Min(slowMotionScale, timeScale) : timeScale;
        slowMotionRemaining = Mathf.Max(slowMotionRemaining, duration);
        if (!IsPaused) Time.timeScale = slowMotionScale;
    }

    /// <summary>
    /// Stops or restarts scene time. Resuming restores the slow-motion scale when a beat was
    /// interrupted, so a pause taken during the super freeze returns to the freeze, not to full speed.
    /// </summary>
    public void SetPaused(bool paused)
    {
        IsPaused = paused;
        Time.timeScale = paused ? 0f : IsSlowMotionActive ? slowMotionScale : 1f;
    }
}
