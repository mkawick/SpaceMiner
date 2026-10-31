using Unity.Entities;
using Unity.Mathematics;

public enum PirateState : byte
{
    Hunt = 0,   // find the best target and fly to it
    Attack = 1, // circle the target at weapon range and shoot
    Flee = 2    // badly damaged: run, and vanish once clear
}

public struct PirateAI : IComponentData
{
    public PirateState state;
    public Entity target;
    public float3 velocity;
    public float fireTimer;
    public float retargetTimer;
    public int nextGun;
    public float orbitSign; // +1 / -1: which way it circles
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

// Wave settings and pirate ship prefabs, baked by GameLibrary_Authoring.
public struct PirateSpawnConfig : IComponentData
{
    public bool enabled;
    public float firstWaveDelay;
    public float waveInterval;
    public int2 groupSize;
    public float spawnDistance;
    public int maxAlive;
}

public struct PiratePrefab : IBufferElementData
{
    public Entity prefab;
}
