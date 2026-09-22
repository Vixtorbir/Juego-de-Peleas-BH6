using UnityEngine;

// Small per-fighter pool; firing never instantiates and expiring never destroys.
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

    public HadoukenProjectile Acquire()
    {
        HadoukenProjectile projectile = projectiles[nextIndex];
        nextIndex = (nextIndex + 1) % projectiles.Length;

        if (projectile.IsInFlight) projectile.Recall();
        return projectile;
    }

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
