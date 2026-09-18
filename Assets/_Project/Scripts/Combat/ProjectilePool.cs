using UnityEngine;

/// <summary>
/// Fixed set of projectiles owned by one fighter, built once at startup and recycled round-robin
/// in the same spirit as <see cref="HitsparkPool"/>: firing never instantiates and expiring never
/// destroys. The pool is sized for the few projectiles a fighter can realistically keep in flight;
/// if every slot is busy the oldest projectile is recalled and reused. Instances live at the scene
/// root so the owner's movement never drags them.
/// </summary>
public sealed class ProjectilePool
{
    private readonly HadoukenProjectile[] projectiles;
    private int nextIndex;

    public int Capacity => projectiles.Length;

    public ProjectilePool(GameObject prefab, string ownerName, int capacity)
    {
        projectiles = new HadoukenProjectile[capacity];

        for (int i = 0; i < capacity; i++)
        {
            GameObject instance = Object.Instantiate(prefab);
            instance.name = prefab.name + " (" + ownerName + ") " + i;
            instance.SetActive(false);
            projectiles[i] = instance.GetComponent<HadoukenProjectile>();
        }
    }

    /// <summary>
    /// Returns the next projectile in rotation, recalling it first if it is still in flight.
    /// </summary>
    public HadoukenProjectile Acquire()
    {
        HadoukenProjectile projectile = projectiles[nextIndex];
        nextIndex = (nextIndex + 1) % projectiles.Length;

        if (projectile.IsInFlight) projectile.Recall();
        return projectile;
    }

    /// <summary>
    /// Clears every projectile still in flight, used on round reset so nothing fired in the
    /// previous round lands after the fighters have been re-placed.
    /// </summary>
    public void RecallAll()
    {
        for (int i = 0; i < projectiles.Length; i++)
        {
            if (projectiles[i].IsInFlight) projectiles[i].Recall();
        }
    }

    public int CountInFlight()
    {
        int count = 0;
        for (int i = 0; i < projectiles.Length; i++)
        {
            if (projectiles[i].IsInFlight) count++;
        }

        return count;
    }
}
