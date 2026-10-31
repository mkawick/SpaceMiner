using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public enum PirateState : byte
{
    Hunt = 0,   // find the best target (or loot) and fly to it
    Attack = 1, // circle the target at weapon range and shoot
    Flee = 2,   // badly damaged: run, and vanish once clear
    Depart = 3  // got what it came for: leave, and vanish once clear
}

// What a party is after. Set on the Pirate Party asset.
public enum PirateGoal : byte
{
    Raid = 0,     // hit stations and bots; fights anything
    Scavenge = 1, // steal loose ore; only fights when shot at
    Slavers = 2,  // disable worker bots and carry them off
    Hunt = 3      // go for pilots and ships
}

public enum PartyFormation : byte
{
    Vee = 0,
    Line = 1,
    Column = 2,
    Cluster = 3
}

public struct PirateAI : IComponentData
{
    public PirateState state;
    public Entity target;
    public bool targetIsOre;
    public float3 velocity;
    public float fireTimer;
    public float retargetTimer;
    public int nextGun;
    public float orbitSign;     // +1 / -1: which way it circles
    public float loot;          // ore stolen (1 each) and bots captured (5 each)
    public float lastHealth;    // to notice being shot
    public float provokedTimer; // > 0: was shot recently, fights back whatever its goal
}

// The party this ship came with, from its Pirate Party asset.
public struct PirateBehaviour : IComponentData
{
    public PirateGoal goal;
    public float aggression; // 0 = cautious, 1 = reckless
    public float lootGoal;   // leaves once it has this much loot; 0 = never
    public int party;        // ships from the same wave share this
}

// Per-ship tuning, from Pirate_Authoring.
public struct PirateSettings : IComponentData
{
    public float maxSpeed;
    public float acceleration;
    public float turnSpeed;      // radians per second
    public float preferredRange; // orbit distance while attacking
    public float sightRange;     // how far it looks for targets
    public float fireArc;        // radians either side of the nose it will shoot within
    public float fleeBelow;      // health fraction that makes it run
    public WeaponId weapon;
}

// A gun position on the ship (children named FiringPoint...), in the ship's local space.
public struct PirateGun : IBufferElementData
{
    public float3 localPosition;
}

// Wave timing, baked from the Pirate Roster asset by GameLibrary_Authoring.
public struct PirateSpawnConfig : IComponentData
{
    public bool enabled;
    public float firstWaveDelay;
    public float waveInterval;
    public float spawnDistance;
    public int maxAlive;
}

// One party type from the roster. Its ships are partyShips[shipStart .. shipStart + shipCount).
public struct PiratePartyDef : IBufferElementData
{
    public FixedString64Bytes name;
    public float weight;
    public PirateGoal goal;
    public float aggression;
    public float lootGoal;
    public PartyFormation formation;
    public float spacing; // 0 = from ship size
    public int shipStart;
    public int shipCount;
}

public struct PiratePartyShip : IBufferElementData
{
    public Entity prefab;
    public int min;
    public int max;
}
