using System.Collections;
using UnityEngine;

/// <summary>
/// Scene-wide hitstop service. Freezes gameplay time for a few frames on impact while UI and
/// camera shake keep running on unscaled time. A new hitstop always replaces the previous one.
/// </summary>
public class CombatEffectsManager : MonoBehaviour
{
    private const float HitstopTimeScale = 0.01f;

    public static CombatEffectsManager Instance { get; private set; }

    private Coroutine activeHitstop;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Time.timeScale = 1f;
            Instance = null;
        }
    }

    public void TriggerHitstop(float duration)
    {
        if (activeHitstop != null)
        {
            StopCoroutine(activeHitstop);
        }

        activeHitstop = StartCoroutine(HitstopRoutine(duration));
    }

    private IEnumerator HitstopRoutine(float duration)
    {
        Time.timeScale = HitstopTimeScale;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = 1f;
        activeHitstop = null;
    }
}
