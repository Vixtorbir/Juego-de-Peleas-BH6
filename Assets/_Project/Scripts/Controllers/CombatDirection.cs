using UnityEngine;
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
public struct BufferedDirection
{
    public CombatDirection direction;
    public float timestamp;

    public BufferedDirection(CombatDirection direction, float timestamp)
    {
        this.direction = direction;
        this.timestamp = timestamp;
    }
}