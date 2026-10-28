using System;
using Unity.Entities;

public enum WeaponId : byte
{
    MiningLaser = 0, // cracks rock; every worker bot has one, and it's the player's starting weapon
    PulseLaser = 1,  // fast, accurate anti-ship fire
    Rockets = 2      // slow, heavy anti-ship salvo
}

// One weapon's numbers. Built-in defaults live in WeaponData.Default; add WeaponCatalog_Authoring
// to the subscene to tune them in the Inspector.
[Serializable]
public struct WeaponDef : IBufferElementData
{
    public WeaponId id;
    public int prefabIndex;        // which projectile prefab on the Spawner to fire
    public float scale;            // projectile size
    public float speed;            // units per second, on top of the shooter's speed
    public float lifetime;         // seconds; range = speed * lifetime
    public float hullDamage;       // per projectile, against density 0 (ships)
    public float rockDamage;       // per projectile, against density 1 (asteroids)
    public float fireInterval;     // seconds between shots
    public int projectilesPerShot; // > 1 fires a spread
    public float spreadDegrees;    // total angle across a spread
    public CollisionLayer projectileLayer; // the Layer Matrix decides what these projectiles hit
}

// Marks the entity holding a WeaponDef buffer that overrides the built-in defaults.
public struct WeaponCatalog : IComponentData
{
}

// A weapon fitted to a ship. readyTime / interval drive the cooldown display.
public struct WeaponSlot : IBufferElementData
{
    public WeaponId id;
    public float readyTime; // elapsed time when it can fire again
    public float interval;  // the cooldown last started, for the HUD
}

public struct ActiveWeapon : IComponentData
{
    public int slot;
}

public static class WeaponData
{
    public const int Count = 3;

    public static WeaponDef Default(WeaponId id)
    {
        switch (id)
        {
            case WeaponId.PulseLaser:
                return new WeaponDef
                {
                    id = id, prefabIndex = 0, scale = 0.7f, speed = 120f, lifetime = 1.5f,
                    hullDamage = 8f, rockDamage = 0.05f, fireInterval = 0.2f,
                    projectilesPerShot = 1, spreadDegrees = 0f, projectileLayer = CollisionLayer.Projectile
                };
            case WeaponId.Rockets:
                return new WeaponDef
                {
                    id = id, prefabIndex = 0, scale = 1.6f, speed = 45f, lifetime = 4f,
                    hullDamage = 25f, rockDamage = 0.2f, fireInterval = 1.2f,
                    projectilesPerShot = 2, spreadDegrees = 6f, projectileLayer = CollisionLayer.Missile
                };
            default: // MiningLaser
                return new WeaponDef
                {
                    id = WeaponId.MiningLaser, prefabIndex = 0, scale = 1f, speed = 60f, lifetime = 2f,
                    hullDamage = 2f, rockDamage = 1f, fireInterval = 0.35f,
                    projectilesPerShot = 1, spreadDegrees = 0f, projectileLayer = CollisionLayer.MiningShot
                };
        }
    }

    // The catalog's entry for this weapon if there is one, otherwise the default.
    public static WeaponDef Find(WeaponId id, bool hasCatalog, in DynamicBuffer<WeaponDef> catalog)
    {
        if (hasCatalog)
            for (int i = 0; i < catalog.Length; i++)
                if (catalog[i].id == id)
                    return catalog[i];
        return Default(id);
    }

    // UI only (managed strings; not for Burst code).
    public static string Name(WeaponId id)
    {
        switch (id)
        {
            case WeaponId.PulseLaser: return "Pulse laser";
            case WeaponId.Rockets: return "Rockets";
            default: return "Mining laser";
        }
    }
}
