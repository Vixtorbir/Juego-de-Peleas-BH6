using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Records discrete directional inputs relative to the opponent and matches them against
/// motion sequences (quarter-circle, dragon punch) using reverse sequence matching.
/// Inputs older than <see cref="inputLifeTime"/> are pruned every frame without allocating.
/// </summary>
[RequireComponent(typeof(RobotController))]
public class InputBuffer : MonoBehaviour
{
    private const float VerticalDeadzone = 0.4f;
    private const float HorizontalDeadzone = 0.4f;

    [Header("Buffer Settings")]
    [SerializeField] private float inputLifeTime = 0.35f;

    private readonly List<BufferedDirection> directionHistory = new List<BufferedDirection>(16);
    private CombatDirection lastDiscreteDirection = CombatDirection.Neutral;
    private RobotController robot;

    private void Awake()
    {
        robot = GetComponent<RobotController>();
    }

    private void Update()
    {
        PruneExpiredInputs();
        RecordCurrentDirection();
    }

    /// <summary>
    /// Returns true if the most recent inputs contain <paramref name="sequence"/> in order,
    /// allowing intermediate noise between steps. On success the history is cleared so the
    /// same motion cannot trigger twice.
    /// </summary>
    public bool CheckMotionInput(CombatDirection[] sequence)
    {
        if (directionHistory.Count < sequence.Length) return false;

        int sequenceIndex = sequence.Length - 1;

        for (int i = directionHistory.Count - 1; i >= 0; i--)
        {
            if (directionHistory[i].Direction != sequence[sequenceIndex]) continue;

            sequenceIndex--;

            if (sequenceIndex < 0)
            {
                directionHistory.Clear();
                return true;
            }
        }

        return false;
    }

    private void RecordCurrentDirection()
    {
        if (robot.opponentTarget == null) return;

        float facingSign = Mathf.Sign(robot.opponentTarget.position.x - transform.position.x);
        CombatDirection currentDirection = ToCombatDirection(robot.MovementInput, facingSign);

        if (currentDirection == lastDiscreteDirection) return;

        lastDiscreteDirection = currentDirection;
        directionHistory.Add(new BufferedDirection(currentDirection, Time.time));
    }

    /// <summary>
    /// The history is chronological, so expired entries always form a contiguous prefix.
    /// A single RemoveRange keeps this Zero-GC, unlike RemoveAll with a predicate.
    /// </summary>
    private void PruneExpiredInputs()
    {
        float expirationTime = Time.time - inputLifeTime;
        int expiredCount = 0;

        while (expiredCount < directionHistory.Count && directionHistory[expiredCount].Timestamp < expirationTime)
        {
            expiredCount++;
        }

        if (expiredCount > 0)
        {
            directionHistory.RemoveRange(0, expiredCount);
        }
    }

    private static CombatDirection ToCombatDirection(Vector2 input, float facingSign)
    {
        bool crouching = input.y <= -VerticalDeadzone;
        bool jumping = input.y >= VerticalDeadzone;

        bool horizontalNeutral = Mathf.Abs(input.x) < HorizontalDeadzone;
        bool movingForward = !horizontalNeutral && Mathf.Sign(input.x) == facingSign;
        bool movingBack = !horizontalNeutral && !movingForward;

        if (crouching)
        {
            if (movingForward) return CombatDirection.DownForward;
            if (movingBack) return CombatDirection.DownBack;
            return CombatDirection.Down;
        }

        if (jumping)
        {
            if (movingForward) return CombatDirection.UpForward;
            if (movingBack) return CombatDirection.UpBack;
            return CombatDirection.Up;
        }

        if (movingForward) return CombatDirection.Forward;
        if (movingBack) return CombatDirection.Back;

        return CombatDirection.Neutral;
    }
}
