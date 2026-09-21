using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Side-view combat camera. Tracks the midpoint between both fighters, pulls back as they
/// separate and guarantees both stay inside a horizontal safe area of the viewport even when the
/// stage bounds pin the camera against a corner. Vertically the camera is anchored to the floor:
/// walking, crouching and short hops never move it, while a launched fighter lifts it with slower
/// damping and a hard ceiling so the floor stays in frame. Impact feedback runs on unscaled time so
/// hitstop never freezes it: a damped directional shake with a subtle roll, and a field-of-view
/// punch that springs back to the base lens on heavy hits.
/// </summary>
[RequireComponent(typeof(Camera))]
public class FightingCamera : MonoBehaviour
{
    private const float ShakeFrequency = 42f;
    private const float RollShakeFrequency = 31f;
    private const float RandomShakeRatio = 0.35f;
    private const float RollDegreesPerMeter = 6f;
    private const float FallbackAspect = 16f / 9f;
    private const float MinSmoothTime = 0.0001f;

    [Header("Fighters")]
    public Transform player1;
    public Transform player2;

    [Header("Framing")]
    public Vector3 baseOffset = new Vector3(0f, 1.5f, 0f);
    public float pitchAngle = 8f;
    [Tooltip("Viewport fraction kept clear on each side; both fighters are always framed inside it.")]
    [Range(0f, 0.3f)] public float horizontalSafeMargin = 0.12f;
    [Tooltip("Lateral half extent of a fighter, so the body and not just its root stays inside the safe area.")]
    public float fighterHalfWidth = 0.5f;

    [Header("Dynamic Zoom")]
    public float zoomMultiplier = 0.4f;
    public float minZoomDistance = -6f;
    public float maxZoomDistance = -14f;

    [Header("Vertical Tracking")]
    [Tooltip("Height of the stage floor; the camera baseline is anchored to it.")]
    public float groundHeight = 0f;
    [Tooltip("Fighters below this height above the floor never move the camera vertically.")]
    public float verticalDeadzone = 1f;
    [Tooltip("Share of a launched fighter's height above the deadzone that the camera follows.")]
    [Range(0f, 1f)] public float verticalFollowRatio = 0.6f;
    [Tooltip("Furthest the camera can rise above its baseline, keeping the floor in view.")]
    public float maxElevation = 3f;

    [Header("Stage Bounds")]
    public float leftBound = -8f;
    public float rightBound = 8f;

    [Header("Smoothing")]
    [FormerlySerializedAs("smoothTime")]
    public float smoothTimeX = 0.15f;
    [Tooltip("Slower than the horizontal axis so juggles lift the camera without bobbing.")]
    public float smoothTimeY = 0.35f;
    public float smoothTimeZoom = 0.25f;

    [Header("Impact Zoom")]
    [Tooltip("Time for a field-of-view punch to spring back to the base lens.")]
    public float zoomPunchDuration = 0.15f;

    [Header("Impact Punch")]
    [Tooltip("Time for a translational punch to settle back onto the framed position.")]
    public float punchReturnTime = 0.09f;

    private Camera attachedCamera;
    private Quaternion baseRotation;
    private float baseFieldOfView;
    private float tanHalfHorizontalFov;

    private Vector3 framedPosition;
    private float velocityX;
    private float velocityY;
    private float velocityZ;

    private float shakeDuration;
    private float shakeTimer;
    private float shakeMagnitude;
    private Vector3 shakeDirection;

    private float zoomPunchTimer;
    private float zoomPunchDelta;

    private Vector3 punchOffset;
    private Vector3 punchVelocity;

    private void Awake()
    {
        attachedCamera = GetComponent<Camera>();
        baseFieldOfView = attachedCamera.fieldOfView;
    }

    private void Start()
    {
        baseRotation = Quaternion.Euler(pitchAngle, 0f, 0f);
        transform.rotation = baseRotation;
        framedPosition = transform.position;
        RefreshLensExtents();
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
        if (magnitude < shakeMagnitude * (shakeTimer / Mathf.Max(shakeDuration, MinSmoothTime))) return;

        shakeDuration = duration;
        shakeTimer = duration;
        shakeMagnitude = magnitude;
        shakeDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }

    /// <summary>
    /// Kicks the field of view by <paramref name="fovDelta"/> degrees (negative tightens the lens)
    /// and springs it back over <see cref="zoomPunchDuration"/>. A larger punch replaces a smaller one.
    /// </summary>
    public void PunchZoom(float fovDelta)
    {
        if (Mathf.Abs(fovDelta) < Mathf.Abs(zoomPunchDelta) * ComputeZoomPunchEnvelope()) return;

        zoomPunchDelta = fovDelta;
        zoomPunchTimer = zoomPunchDuration;
    }

    /// <summary>
    /// Kicks the camera <paramref name="meters"/> along <paramref name="direction"/> (screen space)
    /// and lets a critically damped spring bring it back, so a hit nudges the frame the way the
    /// defender is knocked before the shake takes over. Punches accumulate; the spring settles them.
    /// </summary>
    public void Punch(Vector3 direction, float meters)
    {
        if (meters <= 0f || direction.sqrMagnitude < 0.0001f) return;
        punchOffset += direction.normalized * meters;
    }

    private void LateUpdate()
    {
        if (player1 == null || player2 == null) return;

        float unscaledDelta = Time.unscaledDeltaTime;
        Vector3 p1 = player1.position;
        Vector3 p2 = player2.position;

        float targetX = Mathf.Clamp((p1.x + p2.x) * 0.5f + baseOffset.x, leftBound, rightBound);
        float targetY = ComputeTargetHeight(p1.y, p2.y);
        float targetZ = ComputeTargetDepth(p1, p2, targetX, targetY);

        framedPosition.x = Mathf.SmoothDamp(framedPosition.x, targetX, ref velocityX, smoothTimeX, Mathf.Infinity, unscaledDelta);
        framedPosition.y = Mathf.SmoothDamp(framedPosition.y, targetY, ref velocityY, smoothTimeY, Mathf.Infinity, unscaledDelta);
        framedPosition.z = Mathf.SmoothDamp(framedPosition.z, targetZ, ref velocityZ, smoothTimeZoom, Mathf.Infinity, unscaledDelta);

        AdvanceShake(unscaledDelta, out Vector3 shakeOffset, out float shakeRoll);
        punchOffset = Vector3.SmoothDamp(punchOffset, Vector3.zero, ref punchVelocity, Mathf.Max(punchReturnTime, MinSmoothTime), Mathf.Infinity, unscaledDelta);
        transform.SetPositionAndRotation(framedPosition + shakeOffset + punchOffset, baseRotation * Quaternion.Euler(0f, 0f, shakeRoll));

        AdvanceZoomPunch(unscaledDelta);
    }

    /// <summary>
    /// The baseline sits a fixed height above the floor. Only the highest fighter can lift it, and
    /// only by the share of their height beyond the deadzone, so hops keep the horizon still while
    /// a full juggle raises the frame just enough to keep the airborne fighter in view.
    /// </summary>
    private float ComputeTargetHeight(float p1Height, float p2Height)
    {
        float baseline = groundHeight + baseOffset.y;
        float highest = Mathf.Max(p1Height, p2Height) - groundHeight;
        float lift = Mathf.Max(0f, highest - verticalDeadzone) * verticalFollowRatio;

        return baseline + Mathf.Min(lift, maxElevation);
    }

    /// <summary>
    /// Depth is the further of two demands: the separation zoom that gives the neutral game its
    /// breathing room, and the distance at which the fighter furthest from the camera axis still
    /// lands inside the horizontal safe area. The second only differs from the first when the
    /// stage bounds hold the camera short of the midpoint, which is exactly the corner situation.
    /// </summary>
    private float ComputeTargetDepth(Vector3 p1, Vector3 p2, float cameraX, float cameraY)
    {
        float separation = Mathf.Abs(p1.x - p2.x);
        float separationDepth = baseOffset.z - separation * zoomMultiplier;

        float furthestOffset = Mathf.Max(Mathf.Abs(p1.x - cameraX), Mathf.Abs(p2.x - cameraX)) + fighterHalfWidth;
        float safeHalfWidthRatio = 1f - horizontalSafeMargin * 2f;
        float requiredHalfWidth = furthestOffset / Mathf.Max(safeHalfWidthRatio, 0.01f);
        float requiredViewDistance = requiredHalfWidth / tanHalfHorizontalFov;

        float pitchRadians = pitchAngle * Mathf.Deg2Rad;
        float highestFighterY = Mathf.Max(p1.y, p2.y);
        float heightAlongView = (cameraY - highestFighterY) * Mathf.Sin(pitchRadians);
        float framingDepth = -(requiredViewDistance - heightAlongView) / Mathf.Cos(pitchRadians);

        return Mathf.Clamp(Mathf.Min(separationDepth, framingDepth), maxZoomDistance, minZoomDistance);
    }

    private void AdvanceShake(float unscaledDelta, out Vector3 offset, out float roll)
    {
        offset = Vector3.zero;
        roll = 0f;
        if (shakeTimer <= 0f) return;

        shakeTimer -= unscaledDelta;

        float normalizedRemaining = Mathf.Clamp01(shakeTimer / shakeDuration);
        float envelope = normalizedRemaining * normalizedRemaining;
        float amplitude = shakeMagnitude * envelope;
        float time = Time.unscaledTime;

        roll = Mathf.Sin(time * RollShakeFrequency) * amplitude * RollDegreesPerMeter;
        offset = Random.insideUnitSphere * (amplitude * RandomShakeRatio);
        if (shakeDirection == Vector3.zero) return;

        offset += shakeDirection * (Mathf.Sin(time * ShakeFrequency) * amplitude);
    }

    /// <summary>
    /// The envelope reads zero once the timer expires, so the final write of a punch lands exactly
    /// on the base lens and no further writes happen while the lens is at rest.
    /// </summary>
    private void AdvanceZoomPunch(float unscaledDelta)
    {
        if (zoomPunchTimer <= 0f) return;

        zoomPunchTimer -= unscaledDelta;
        attachedCamera.fieldOfView = baseFieldOfView + zoomPunchDelta * ComputeZoomPunchEnvelope();
    }

    private float ComputeZoomPunchEnvelope()
    {
        if (zoomPunchTimer <= 0f) return 0f;

        float normalizedRemaining = Mathf.Clamp01(zoomPunchTimer / Mathf.Max(zoomPunchDuration, MinSmoothTime));
        return normalizedRemaining * normalizedRemaining;
    }

    /// <summary>
    /// Horizontal lens extent derived from the base vertical field of view, so the framing rule is
    /// unaffected by a zoom punch in progress. Headless runs report no aspect and fall back to 16:9.
    /// </summary>
    private void RefreshLensExtents()
    {
        float aspect = attachedCamera.aspect;
        if (aspect <= 0f || float.IsNaN(aspect) || float.IsInfinity(aspect)) aspect = FallbackAspect;

        tanHalfHorizontalFov = Mathf.Tan(baseFieldOfView * 0.5f * Mathf.Deg2Rad) * aspect;
    }

    private void OnDrawGizmosSelected()
    {
        float baseline = groundHeight + baseOffset.y;
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.DrawLine(new Vector3(leftBound, baseline, 0f), new Vector3(rightBound, baseline, 0f));
        Gizmos.DrawLine(new Vector3(leftBound, baseline + maxElevation, 0f), new Vector3(rightBound, baseline + maxElevation, 0f));

        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.6f);
        float deadzoneTop = groundHeight + verticalDeadzone;
        Gizmos.DrawLine(new Vector3(leftBound, deadzoneTop, 0f), new Vector3(rightBound, deadzoneTop, 0f));
    }
}
