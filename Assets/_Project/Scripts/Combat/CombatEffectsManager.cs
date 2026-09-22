using UnityEngine;

// Only thing allowed to touch Time.timeScale (slow-mo beats and pause). Hitstop is per fighter, see RobotController.FreezeFor.
public class CombatEffectsManager : MonoBehaviour
{
    public static CombatEffectsManager Instance { get; private set; }

    public bool IsSlowMotionActive => slowMotionRemaining > 0f;
    public bool IsPaused { get; private set; }

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

    // Restore time scale so a slow-mo beat or pause never leaks into the next scene.
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

    public void TriggerSlowMotion(float timeScale, float duration)
    {
        if (duration <= 0f) return;

        slowMotionScale = IsSlowMotionActive ? Mathf.Min(slowMotionScale, timeScale) : timeScale;
        slowMotionRemaining = Mathf.Max(slowMotionRemaining, duration);
        if (!IsPaused) Time.timeScale = slowMotionScale;
    }

    // Resuming goes back to the slow-mo scale if a beat was interrupted by the pause.
    public void SetPaused(bool paused)
    {
        IsPaused = paused;
        Time.timeScale = paused ? 0f : IsSlowMotionActive ? slowMotionScale : 1f;
    }
}
