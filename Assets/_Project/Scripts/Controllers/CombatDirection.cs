/// <summary>
/// Joystick direction expressed relative to the opponent (numpad notation), so motion inputs
/// remain valid regardless of which side of the screen the fighter is standing on.
/// </summary>
public enum CombatDirection
{
    Neutral,
    Down,
    Forward,
    Back,
    DownForward,
    DownBack,
    Up,
    UpForward,
    UpBack
}

/// <summary>
/// Timestamped directional input stored by <see cref="InputBuffer"/> for motion detection.
/// </summary>
public readonly struct BufferedDirection
{
    public readonly CombatDirection Direction;
    public readonly float Timestamp;

    public BufferedDirection(CombatDirection direction, float timestamp)
    {
        Direction = direction;
        Timestamp = timestamp;
    }
}
