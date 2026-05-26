using System.Collections;
using UnityEngine;

public class CombatEffectsManager : MonoBehaviour
{
    public static CombatEffectsManager Instance;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void TriggerHitstop(float duration)
    {
        StopAllCoroutines(); 
        StartCoroutine(HitstopRoutine(duration));
    }

    private IEnumerator HitstopRoutine(float duration)
    {
        Time.timeScale = 0.01f;

        yield return new WaitForSecondsRealtime(duration);

        Time.timeScale = 1f;
    }
}