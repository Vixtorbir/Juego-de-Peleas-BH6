using UnityEngine;

/// <summary>
/// Side-view combat camera. Tracks the midpoint between both fighters, pulls back as they
/// separate, clamps to the stage bounds so corners stay framed, and layers an unscaled-time
/// screen shake on top so hitstop does not freeze the impact feedback. The shake is a damped
/// oscillation biased along the hit direction, with a small random component for texture.
/// </summary>
public class FightingCamera : MonoBehaviour
{
    private const float ShakeFrequency = 42f;
    private const float RandomShakeRatio = 0.35f;

    [Header("Fighters")]
    public Transform player1;
    public Transform player2;

    [Header("Framing")]
    public Vector3 baseOffset = new Vector3(0f, 1.5f, 0f);
    public float pitchAngle = 8f;

    [Header("Dynamic Zoom")]
    public float zoomMultiplier = 0.4f;
    public float minZoomDistance = -6f;
    public float maxZoomDistance = -14f;

    [Header("Stage Bounds")]
    public float leftBound = -8f;
    public float rightBound = 8f;

    [Header("Smoothing")]
    public float smoothTime = 0.15f;

    private Vector3 framedPosition;
    private Vector3 currentVelocity;

    private float shakeDuration;
    private float shakeTimer;
    private float shakeMagnitude;
    private Vector3 shakeDirection;

    private void Start()
    {
        transform.rotation = Quaternion.Euler(pitchAngle, 0f, 0f);
        framedPosition = transform.position;
    }

    /// <summary>
    /// Starts an omnidirectional shake. A stronger shake replaces a weaker one in progress.
    /// </summary>
    public void ShakeCamera(float duration, float magnitude)
    {
        ShakeCamera(duration, magnitude, Vector3.zero);
    }

    /// <summary>
    /// Starts a shake whose main oscillation runs along <paramref name="direction"/> (screen space),
    /// so a punch pushes the camera the same way the defender is knocked.
    /// </summary>
    public void ShakeCamera(float duration, float magnitude, Vector3 direction)
    {
        if (magnitude < shakeMagnitude * (shakeTimer / Mathf.Max(shakeDuration, 0.0001f))) return;

        shakeDuration = duration;
        shakeTimer = duration;
        shakeMagnitude = magnitude;
        shakeDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }

    private void LateUpdate()
    {
        if (player1 == null || player2 == null) return;

        Vector3 midPoint = (player1.position + player2.position) * 0.5f;
        float separation = Vector3.Distance(player1.position, player2.position);

        float depth = Mathf.Clamp(baseOffset.z - separation * zoomMultiplier, maxZoomDistance, minZoomDistance);
        float clampedX = Mathf.Clamp(midPoint.x + baseOffset.x, leftBound, rightBound);

        Vector3 targetPosition = new Vector3(clampedX, midPoint.y + baseOffset.y, depth);
        framedPosition = Vector3.SmoothDamp(framedPosition, targetPosition, ref currentVelocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);

        transform.position = framedPosition + ComputeShakeOffset();
    }

    private Vector3 ComputeShakeOffset()
    {
        if (shakeTimer <= 0f) return Vector3.zero;

        shakeTimer -= Time.unscaledDeltaTime;

        float normalizedRemaining = Mathf.Clamp01(shakeTimer / shakeDuration);
        float envelope = normalizedRemaining * normalizedRemaining;
        float amplitude = shakeMagnitude * envelope;

        Vector3 randomOffset = Random.insideUnitSphere * (amplitude * RandomShakeRatio);
        if (shakeDirection == Vector3.zero) return randomOffset;

        float oscillation = Mathf.Sin(Time.unscaledTime * ShakeFrequency);
        return shakeDirection * (oscillation * amplitude) + randomOffset;
    }
}
