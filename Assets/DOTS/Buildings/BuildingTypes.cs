using Unity.Entities;
using Unity.Mathematics;

public enum BuildingId : byte
{
    WorkerFactory = 0,
}

public enum UnitId : byte
{
    WorkerBot = 0,
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
public struct TrainRequest : IComponentData
{
    public Entity building;
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

public struct TrainQueue : IComponentData
{
    public int queued;
    public float progress; // seconds into the current bot
    public int trained;
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
}
