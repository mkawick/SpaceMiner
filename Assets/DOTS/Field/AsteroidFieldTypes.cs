using Unity.Entities;
using Unity.Mathematics;

public enum ResourceType : byte
{
    Iron = 0,     // starter: frames, armor, weapons
    Copper = 1,   // starter: wiring, power, drones
    Silicate = 2, // starter: stone and glass for hulls
    Ice = 3,      // starter: water and life support
    Carbon = 4,   // starter: hydrocarbons for polymers and plastics
    Gold = 5,     // rare: electronics, sensors
    Fermite = 6,  // very rare, deep field only: shields, reactors
}

public enum HexKind : byte
{
    Wild = 0,
    Newbie = 1,  // inside a faction's safe newbie zone
    Starter = 2, // a player start hex in a faction's home ring
}

public struct AsteroidFieldConfig : IComponentData
{
    public uint seed;
    public float mapRadius;
    public float hexSize;
    public float2 grixHome;
    public float2 postibuleHome;
    public float2 fairwayHome;
    public float newbieZoneRadius;
    public float starterRingRadius;
    public int starterAsteroidsPerHex;
    public int2 wildAsteroidsPerHex; // x at the map edge, y at the centre
    public float2 radiusRange;       // world units
    public float2 spinRange;         // degrees per second
    public CombatFaction playerFaction;
    public int playerStartSlot;
}

public struct AsteroidVisualPrefab : IBufferElementData
{
    public Entity prefab;
    public float meshRadius; // radius of the prefab's mesh at scale 1
}

// Added to the config entity once the field exists.
public struct AsteroidFieldReady : IComponentData
{
    public int hexCount;
    public int asteroidCount;
    public int starterHexCount;
    public int2 playerHex;
}

public struct PlayerStartPlaced : IComponentData
{
}

public struct AsteroidResource : IComponentData
{
    public ResourceType type;
    public float amount; // ore pieces left to knock out; the asteroid breaks up at 0
}

// Global mining settings, baked from AsteroidField_Authoring.
public struct MiningConfig : IComponentData
{
    public float oreDropChance;     // chance per hit that a piece of ore pops out
    public int orePerAsteroid;      // ore pieces each asteroid can give before it breaks up
    public float2 oreScatterDistance; // how far ore pops out beyond the asteroid's surface (min, max)
    public float oreScatterTime;    // seconds to pop out
    public float oreSpreadDegrees;  // how far from the shot's direction ore may fly
}

// The ore prefab for each ResourceType, in enum order.
[InternalBufferCapacity(7)]
public struct OrePrefab : IBufferElementData
{
    public Entity prefab;
}

public enum OreState : byte
{
    PoppingOut = 0,
    Resting = 1,
    Collecting = 2,
}

public struct OreChunk : IComponentData
{
    public ResourceType type;
    public float amount;
    public OreState state;
    public float3 from;  // where it left the asteroid
    public float3 rest;  // where it settles
    public float progress; // 0..1 while popping out
    public float speed;    // while being pulled in
    public Entity collector;
}

// Sits on a ship: pulls in ore within radius and adds it to the ship's cargo. Upgradeable later.
public struct OreCollector : IComponentData
{
    public float radius;
    public float pickupDistance;
    public float pullAcceleration;
    public float maxPullSpeed;
}

// Which hex an asteroid was generated in, its index there (a stable ID), and its radius for hit tests.
public struct AsteroidHex : IComponentData
{
    public int2 hex;
    public int index;
    public float radius;
}

// One entry per ResourceType, in enum order.
[InternalBufferCapacity(7)]
public struct CargoItem : IBufferElementData
{
    public float amount;
}

public struct ShipRadar : IComponentData
{
    public float range;
}

public static class AsteroidFieldRules
{
    public const int ResourceCount = 7;
    public const int StarterResourceCount = 5;

    // Starter hexes get three of the five common types: all except colour c and (c + 2) mod 5.
    // Neighbouring hexes always have different colours, so they never share the same set,
    // and each type appears in exactly 3 of every 5 starter hexes.
    public static bool InStarterSet(int colour, int type)
    {
        return type != colour && type != (colour + 2) % 5;
    }

    public static ResourceType StarterType(int colour, int n)
    {
        int found = 0;
        for (int t = 0; t < StarterResourceCount; t++)
        {
            if (!InStarterSet(colour, t))
                continue;
            if (found == n % 3)
                return (ResourceType)t;
            found++;
        }
        return ResourceType.Iron;
    }

    public static void NearestHome(in AsteroidFieldConfig cfg, float2 p, out CombatFaction faction, out float distance)
    {
        faction = CombatFaction.Grix;
        distance = math.distance(p, cfg.grixHome);
        float d = math.distance(p, cfg.postibuleHome);
        if (d < distance) { distance = d; faction = CombatFaction.PostibuleSanctuary; }
        d = math.distance(p, cfg.fairwayHome);
        if (d < distance) { distance = d; faction = CombatFaction.FairwayDominion; }
    }

    public static HexKind Classify(in AsteroidFieldConfig cfg, int2 hex, out CombatFaction faction)
    {
        float2 centre = HexGrid.HexToWorld(hex, cfg.hexSize);
        NearestHome(cfg, centre, out faction, out float distance);
        if (distance < cfg.newbieZoneRadius) return HexKind.Newbie;
        if (distance < cfg.starterRingRadius) return HexKind.Starter;
        faction = CombatFaction.None;
        return HexKind.Wild;
    }

    public static bool IsInsideMap(in AsteroidFieldConfig cfg, int2 hex)
    {
        return math.length(HexGrid.HexToWorld(hex, cfg.hexSize)) <= cfg.mapRadius;
    }

    // Ore per unit of asteroid radius.
    public static float Yield(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Iron: return 10f;
            case ResourceType.Copper: return 8f;
            case ResourceType.Silicate: return 12f;
            case ResourceType.Ice: return 10f;
            case ResourceType.Carbon: return 9f;
            case ResourceType.Gold: return 4f;
            default: return 2f; // Fermite
        }
    }
}
