using UnityEngine;

/// <summary>
/// Scene-wide hitstop service. Freezes gameplay time on impact while UI and camera shake keep
/// running on unscaled time. The freeze is tracked with an unscaled timer instead of a coroutine
/// so triggering it every hit never allocates. A longer hitstop always extends a shorter one.
/// </summary>
public class CombatEffectsManager : MonoBehaviour
{
    private const float HitstopTimeScale = 0.01f;

    public static CombatEffectsManager Instance { get; private set; }

    public bool IsHitstopActive => hitstopRemaining > 0f;

    private float hitstopRemaining;

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
    /// A hitstop in flight must never outlive the manager: disabling it (scene unload, editor
    /// stop, object toggled) clears the freeze so the next scene starts on normal time.
    /// </summary>
    private void OnDisable()
    {
        if (Instance != this) return;

        hitstopRemaining = 0f;
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
        if (hitstopRemaining <= 0f) return;

        hitstopRemaining -= Time.unscaledDeltaTime;
        if (hitstopRemaining <= 0f)
        {
            hitstopRemaining = 0f;
            Time.timeScale = 1f;
        }
    }

    public void TriggerHitstop(float duration)
    {
        if (duration <= 0f) return;

        hitstopRemaining = Mathf.Max(hitstopRemaining, duration);
        Time.timeScale = HitstopTimeScale;
    }
}
