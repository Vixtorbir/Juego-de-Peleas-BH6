using System.Collections.Generic;
using UnityEngine;

// Short history of stick directions used to detect motion inputs (QCF, DP, double QCF).
[RequireComponent(typeof(RobotController))]
public class InputBuffer : MonoBehaviour
{
    private const float VerticalDeadzone = 0.4f;
    private const float HorizontalDeadzone = 0.4f;

    [Header("Buffer Settings")]
    [Tooltip("Seconds an input stays in the history. Sized for the longest motion; shorter motions apply their own limit per lookup.")]
    [SerializeField] private float inputLifeTime = 0.6f;
    [Tooltip("Age limit applied to a lookup that does not ask for one, covering the single-motion specials.")]
    [SerializeField] private float defaultMotionWindow = 0.35f;

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

    public bool CheckMotionInput(CombatDirection[] sequence)
    {
        return CheckMotionInput(sequence, defaultMotionWindow);
    }

    // Walks the history backwards looking for the sequence in order; noise between steps is allowed.
    public bool CheckMotionInput(CombatDirection[] sequence, float motionWindow)
    {
        if (directionHistory.Count < sequence.Length) return false;

        float oldestAccepted = Time.time - motionWindow;
        int sequenceIndex = sequence.Length - 1;

        for (int i = directionHistory.Count - 1; i >= 0; i--)
        {
            if (directionHistory[i].Timestamp < oldestAccepted) return false;
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

    // History is chronological, so expired entries are always a prefix -> one RemoveRange, no allocations.
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
