using UnityEngine;

/// <summary>
/// Side-view combat camera. Tracks the midpoint between both fighters, pulls back as they
/// separate, clamps to the stage bounds so corners stay framed, and layers an unscaled-time
/// screen shake on top so hitstop does not freeze the impact feedback.
/// </summary>
public class FightingCamera : MonoBehaviour
{
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

    private Vector3 currentVelocity;
    private float shakeTimer;
    private float shakeMagnitude;

    private void Start()
    {
        transform.rotation = Quaternion.Euler(pitchAngle, 0f, 0f);
    }

    public void ShakeCamera(float duration, float magnitude)
    {
        shakeTimer = duration;
        shakeMagnitude = magnitude;
    }

    private void LateUpdate()
    {
        if (player1 == null || player2 == null) return;

        Vector3 midPoint = (player1.position + player2.position) * 0.5f;
        float separation = Vector3.Distance(player1.position, player2.position);

        float depth = Mathf.Clamp(baseOffset.z - separation * zoomMultiplier, maxZoomDistance, minZoomDistance);
        float clampedX = Mathf.Clamp(midPoint.x + baseOffset.x, leftBound, rightBound);

        Vector3 targetPosition = new Vector3(clampedX, midPoint.y + baseOffset.y, depth);
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref currentVelocity, smoothTime);

        if (shakeTimer > 0f)
        {
            transform.position += Random.insideUnitSphere * shakeMagnitude;
            shakeTimer -= Time.unscaledDeltaTime;
        }
    }
}
