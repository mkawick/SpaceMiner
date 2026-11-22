using Unity.Entities;
using Unity.Mathematics;

public enum BuildingId : byte
{
    WorkerFactory = 0,
    Storage = 1,
    Refinery = 2,
}

public enum UnitId : byte
{
    WorkerBot = 0,   // mining bot
    BuilderBot = 1,  // planned
    DefenderBot = 2, // planned
    AttackerBot = 3, // planned
    DefenceShip = 4, // planned
    AttackShip = 5,  // planned
}

// An amount of each ResourceType, in enum order.
[System.Serializable]
public struct ResourceCost
{
    public float iron, copper, silicate, ice, carbon, gold, fermite;

    public float Get(int type)
    {
        switch (type)
        {
            case 0: return iron;
            case 1: return copper;
            case 2: return silicate;
            case 3: return ice;
            case 4: return carbon;
            case 5: return gold;
            default: return fermite;
        }
    }

    public bool CanAfford(DynamicBuffer<CargoItem> cargo)
    {
        for (int i = 0; i < AsteroidFieldRules.ResourceCount; i++)
            if (Get(i) > 0f && (i >= cargo.Length || cargo[i].amount + 0.001f < Get(i)))
                return false;
        return true;
    }

    public void Pay(DynamicBuffer<CargoItem> cargo)
    {
        for (int i = 0; i < cargo.Length && i < AsteroidFieldRules.ResourceCount; i++)
        {
            CargoItem item = cargo[i];
            item.amount = math.max(0f, item.amount - Get(i));
            cargo[i] = item;
        }
    }
}

// Global build settings, baked from BuildCatalog_Authoring.
public struct BuildCatalog : IComponentData
{
    public float placementRange;    // how far from the player's ship buildings may go
    public bool freeBuild;          // testing: ignore costs and build instantly

    public ResourceCost workerFactoryCost;
    public float workerFactoryBuildTime;
    public float workerFactoryRadius;
    public float workerFactoryHealth;

    public ResourceCost storageCost;
    public float storageBuildTime;
    public float storageRadius;
    public float storageHealth;

    public ResourceCost refineryCost;
    public float refineryBuildTime;
    public float refineryRadius;
    public float refineryHealth;

    public ResourceCost workerBotCost;
    public float workerBotTrainTime;
    public float botSpeed;
    public float botTurnSpeed;      // radians per second
    public float botCargoCapacity;
    public float botCollectorRadius;
    public float botMiningStandOff; // distance kept from the asteroid's surface while mining
    public float botGatherRadius;   // loose ore this close to the work site gets picked up
    public float botFireInterval;
    public float botProjectileSpeed;
    public float botSelectRadius;
    public float botHealth;
    public float botScale;             // size multiplier for mining bots (model, click and hit radius)
    public ResourceCost startingResources; // what the player starts with

    public ResourceCost UnitCost(UnitId unit) => unit == UnitId.WorkerBot ? workerBotCost : UnitRules.PlannedCost(unit);
    public float UnitTime(UnitId unit) => freeBuild ? 0.5f : unit == UnitId.WorkerBot ? workerBotTrainTime : UnitRules.PlannedTime(unit);

    public ResourceCost CostOf(BuildingId id) => id == BuildingId.Storage ? storageCost : id == BuildingId.Refinery ? refineryCost : workerFactoryCost;
    public float RadiusOf(BuildingId id) => id == BuildingId.Storage ? storageRadius : id == BuildingId.Refinery ? refineryRadius : workerFactoryRadius;
    public float BuildTimeOf(BuildingId id) => id == BuildingId.Storage ? storageBuildTime : id == BuildingId.Refinery ? refineryBuildTime : workerFactoryBuildTime;
    public float HealthOf(BuildingId id) => id == BuildingId.Storage ? storageHealth : id == BuildingId.Refinery ? refineryHealth : workerFactoryHealth;
}

// A building prefab per faction. faction None = the fallback used when a faction has no prefab.
[InternalBufferCapacity(4)]
public struct FactionBuildingPrefab : IBufferElementData
{
    public BuildingId id;
    public CombatFaction faction;
    public Entity prefab;
}

public struct WorkerBotPrefab : IComponentData
{
    public Entity prefab; // used when a faction has no mining bot of its own
}

// A unit prefab per faction (e.g. the Grix Rudar). Factions without one use WorkerBotPrefab.
[InternalBufferCapacity(4)]
public struct FactionUnitPrefab : IBufferElementData
{
    public UnitId unit;
    public CombatFaction faction;
    public Entity prefab;
}

// The player asks for a building here; BuildingSystem validates and places it.
public struct BuildRequest : IComponentData
{
    public BuildingId id;
    public float3 position;
    public quaternion rotation;
}

// The player asks a factory for one more bot.
// Add a unit to a factory's queue (free; it's paid for when it starts building).
public struct TrainRequest : IComponentData
{
    public Entity building;
    public UnitId unit;
}

// Take a waiting unit out of a factory's queue. index 0, the one being built, can't be removed.
public struct QueueRemoveRequest : IComponentData
{
    public Entity building;
    public int index;
}

// A factory's build queue, in order. Element 0 is the unit being built.
public struct QueuedUnit : IBufferElementData
{
    public UnitId unit;
}

public struct Building : IComponentData
{
    public BuildingId id;
    public CombatFaction faction;
    public float radius;
    public float buildTime;
    public float progress;  // seconds built so far; done when progress >= buildTime
    public float baseScale;

    public bool IsComplete => progress >= buildTime;
}

// Progress of a factory's queue. The front unit is paid for when it starts; if the player can't
// afford it, the queue stalls until they can.
public struct TrainQueue : IComponentData
{
    public float progress; // seconds into the current unit
    public bool paid;      // the current unit has been paid for
    public bool stalled;   // waiting for resources (or switched off in Bot Config)
    public int trained;
}

// What each unit costs and how long it takes. Mining bots read the Build Catalog; the others are
// placeholders until those units exist.
public static class UnitRules
{
    public const int MaxQueue = 10;

    // What the bot factory offers, in menu order.
    public static readonly UnitId[] FactoryUnits = { UnitId.WorkerBot, UnitId.BuilderBot, UnitId.DefenderBot, UnitId.AttackerBot };

    public static bool Implemented(UnitId unit) => unit == UnitId.WorkerBot;

    public static ResourceCost PlannedCost(UnitId unit)
    {
        switch (unit)
        {
            case UnitId.BuilderBot: return new ResourceCost { iron = 25, copper = 10, silicate = 10 };
            case UnitId.DefenderBot: return new ResourceCost { iron = 40, copper = 15, carbon = 10 };
            case UnitId.AttackerBot: return new ResourceCost { iron = 50, copper = 20, carbon = 15, gold = 5 };
            case UnitId.DefenceShip: return new ResourceCost { iron = 150, copper = 60, carbon = 40, gold = 10 };
            case UnitId.AttackShip: return new ResourceCost { iron = 200, copper = 80, carbon = 60, gold = 20 };
            default: return new ResourceCost { iron = 8, copper = 2 };
        }
    }

    public static float PlannedTime(UnitId unit)
    {
        switch (unit)
        {
            case UnitId.BuilderBot: return 12f;
            case UnitId.DefenderBot: return 15f;
            case UnitId.AttackerBot: return 18f;
            case UnitId.DefenceShip: return 40f;
            case UnitId.AttackShip: return 50f;
            default: return 7f;
        }
    }
}

// Anything the player can click on. kind: 0 = unit, 1 = building.
public struct Selectable : IComponentData
{
    public float radius;
    public byte kind;
}

public enum BotState : byte
{
    Idle = 0,
    Moving = 1,
    Mining = 2,
    Returning = 3,
}

public struct WorkerBot : IComponentData
{
    public BotState state;
    public Entity home;        // the factory it delivers to
    public Entity target;      // asteroid being mined
    public float3 workSite;    // where that asteroid is (or was, once it breaks up)
    public bool hasWorkSite;
    public float3 destination; // for Moving
    public float fireTimer;
    public int number;         // shown in the worker list
    public float lastHealth;   // to notice being shot
}
