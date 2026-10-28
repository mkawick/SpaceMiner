using Unity.Entities;
using Unity.Mathematics;

public struct Health : IComponentData
{
    public float current;
    public float max;
    public float Fraction => max > 0f ? math.saturate(current / max) : 0f;
}

// Anything that can be shot at or targeted (set it on a prefab with Targetable_Authoring).
// layer: see CollisionLayer and the Layer Matrix. faction None = neutral (asteroids): anyone can hit it.
// density 0 = a thin hull (ships), 1 = solid rock (asteroids). A hit does
// lerp(weapon.hullDamage, weapon.rockDamage, density), so mining weapons are the ones that crack
// rock and combat weapons are the ones that hurt ships.
public struct Targetable : IComponentData
{
    public CollisionLayer layer;
    public CombatFaction faction;
    public float radius;  // hit radius on the play plane
    public float density; // 0..1
}

// The player's current target (Tab, or left-click on anything that isn't friendly).
public struct PlayerTarget : IComponentData
{
    public Entity target;
}

// Where the player's ship comes back after being destroyed.
public struct RespawnPoint : IComponentData
{
    public float3 position;
}

// A hostile practice target spawned for testing (F8). Never moves or shoots.
public struct TrainingDrone : IComponentData
{
}

public static class Combat
{
    // Same faction = friendly: never hit, never targeted. Neutral (None) is fair game for everyone.
    public static bool IsFriendly(CombatFaction a, CombatFaction b)
    {
        return a != CombatFaction.None && a == b;
    }

    // For things the game spawns (buildings, bots, drones): keep what the prefab's
    // Targetable_Authoring set up, fill in what it didn't, and stamp the owner's faction.
    public static void ApplyTargetable(EntityManager em, Entity e, Targetable fallback, float fallbackHealth, CombatFaction faction)
    {
        Targetable tg = em.HasComponent<Targetable>(e) ? em.GetComponentData<Targetable>(e) : fallback;
        if (tg.radius <= 0f)
            tg.radius = fallback.radius;
        tg.faction = faction;
        em.AddComponentData(e, tg);
        if (!em.HasComponent<Health>(e))
            em.AddComponentData(e, new Health { current = fallbackHealth, max = fallbackHealth });
    }

    public static float Damage(float hullDamage, float rockDamage, float density)
    {
        return math.lerp(hullDamage, rockDamage, math.saturate(density));
    }

    // How solid each kind of asteroid is. Ice is softer, metal-rich rock tougher.
    public static float AsteroidDensity(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Ice: return 0.8f;
            case ResourceType.Carbon: return 0.9f;
            default: return 1f;
        }
    }
}
