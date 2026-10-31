using Unity.Entities;
using Unity.Mathematics;

public enum FlameColour : byte
{
    Blue = 0,
    Green = 1,
    Red = 2,
    White = 3,
    Yellow = 4
}

// What a ship's Propulsion asks for: a fixed colour, or its faction's colour.
public enum FlameColourChoice : byte
{
    Faction = 0,
    Blue = 1,
    Green = 2,
    Red = 3,
    White = 4,
    Yellow = 5
}

// One engine effect from the library, baked by GameLibrary_Authoring.
public struct EngineEffectPrefab : IBufferElementData
{
    public byte style; // 1..6 for the SimpleSpaceshipEngineEffects pack; add your own as 7+
    public FlameColour colour;
    public Entity prefab;
}

// Marks the entity that holds the game-wide libraries (engine effects for now).
public struct GameLibrary : IComponentData
{
}

public static class EngineEffects
{
    // Exact match, else the same style in any colour, else anything.
    public static Entity Find(in DynamicBuffer<EngineEffectPrefab> library, int style, FlameColour colour)
    {
        Entity sameStyle = Entity.Null;
        for (int i = 0; i < library.Length; i++)
        {
            if (library[i].style != style)
                continue;
            if (library[i].colour == colour)
                return library[i].prefab;
            if (sameStyle == Entity.Null)
                sameStyle = library[i].prefab;
        }
        if (sameStyle != Entity.Null)
            return sameStyle;
        return library.Length > 0 ? library[0].prefab : Entity.Null;
    }

    public static FlameColour Resolve(FlameColourChoice choice, CombatFaction faction)
    {
        if (choice != FlameColourChoice.Faction)
            return (FlameColour)((int)choice - 1);
        switch (faction)
        {
            case CombatFaction.PostibuleSanctuary: return FlameColour.Blue;
            case CombatFaction.FairwayDominion: return FlameColour.Red;
            case CombatFaction.Grix: return FlameColour.Green;
            case CombatFaction.Pirates: return FlameColour.Yellow;
            default: return FlameColour.White;
        }
    }
}

// ---- Propulsion -------------------------------------------------------------------------

public enum PropulsionKind : byte
{
    Engine = 0,   // main drive: basic flame, boost flame at speed
    Thruster = 1  // manoeuvring jet: short puffs
}

// An attachment point found under the ship's Propulsion/Engines or Propulsion/Thrusters.
// Its green arrow (local +Y) is the exhaust direction.
public struct PropulsionSocket : IBufferElementData
{
    public Entity socket;
    public PropulsionKind kind;
}

// Ship-level flame settings (Propulsion_Authoring). Flames are spawned once, at runtime.
public struct Propulsion : IComponentData
{
    public FlameColourChoice colour;
    public byte engineStyle;
    public byte boostStyle;    // 0 = no boost flame
    public byte thrusterStyle;
    public float engineSize;   // flame scale at full throttle (also scaled by the socket's own scale)
    public float thrusterSize;
    public float fullThrustSpeed; // ship speed that shows a full flame
    public float boostSpeed;      // above this the boost flame takes over; 0 = never
    public bool spawned;
}

// One flame instance, parented to its socket.
public struct Flame : IComponentData
{
    public Entity ship;
    public PropulsionKind kind;
    public bool isBoost;
    public float2 push;    // ship-local (x, z) direction this nozzle pushes the ship
    public float2 arm;     // ship-local (x, z) offset of the nozzle from the ship's centre
    public float size;
    public float fullThrustSpeed;
    public float boostSpeed;
    public float seed;
    public float throttle;   // smoothed 0..1
    public float boostBlend; // smoothed 0 = basic, 1 = boost
}

// How a ship is moving, measured each frame from its transform (works for the player, bots and
// AI alike), plus the player's keys when it's the player's ship.
public struct ThrustState : IComponentData
{
    public float3 lastPosition;
    public quaternion lastRotation;
    public float2 localVelocity; // x = strafe (+ right), y = forward
    public float yawRate;        // radians per second, + = turning right
    public float speed;
    public float2 inputMove;     // player only: (strafe, thrust)
    public float inputTurn;      // player only: -1 Q .. +1 E
    public bool hasInput;
}
