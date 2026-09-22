// Numpad-style directions relative to the opponent, so motions work on both sides.
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
