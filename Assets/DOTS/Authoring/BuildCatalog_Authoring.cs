using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Put one of these in the subscene (the AsteroidField object is a good place).
// Holds build costs, timings and the building / bot prefabs for each faction.
public class BuildCatalog_Authoring : MonoBehaviour
{
    [Header("Start")]
    [Tooltip("Resources the player starts with (each must fit the 200 of built-in storage)")]
    public ResourceCost startingResources = new ResourceCost { iron = 100, silicate = 40, copper = 30 };

    [Header("General")]
    [Tooltip("Buildings must be placed within this distance of the player's ship")]
    public float placementRange = 60f;
    [Tooltip("Testing: ignore costs and build instantly")]
    public bool freeBuild = true;

    [Header("Worker Factory")]
    public ResourceCost workerFactoryCost = new ResourceCost { iron = 60, silicate = 40, copper = 20 };
    public float workerFactoryBuildTime = 15f;
    [Tooltip("Footprint radius, used for placement and clicking")]
    public float workerFactoryRadius = 10f;
    public float workerFactoryHealth = 500f;
    [Tooltip("Used when a faction has no prefab of its own. Empty = Assets/Prefabs/Worker Factory.prefab")]
    public GameObject workerFactoryPrefab;
    [Tooltip("Empty = Assets/Prefabs/Grix/Tvornica - Worker Factory.prefab")]
    public GameObject grixWorkerFactory;
    [Tooltip("Empty = Assets/Prefabs/Fairway Dominion/Roboterstube - Worker Factory.prefab")]
    public GameObject fairwayWorkerFactory;
    [Tooltip("Empty = Assets/Prefabs/Postibule Sanctuary/Robotkammare - Worker Factory.prefab")]
    public GameObject postibuleWorkerFactory;

    [Header("Storage Depot")]
    [Tooltip("Unlocks 2,000 of block storage (upgradeable from the Storage screen)")]
    public ResourceCost storageCost = new ResourceCost { iron = 80, silicate = 60, carbon = 30 };
    public float storageBuildTime = 12f;
    public float storageRadius = 8f;
    public float storageHealth = 400f;
    [Tooltip("Empty = Assets/Prefabs/Storage Depot.prefab")]
    public GameObject storagePrefab;
    [Tooltip("Empty = Assets/Prefabs/Grix/Skladiste - Storage Depot.prefab")]
    public GameObject grixStorage;

    [Header("Refinery")]
    [Tooltip("Turns ore into steel plates (iron + carbon) and hydrogen (ice). Refining itself comes next")]
    public ResourceCost refineryCost = new ResourceCost { iron = 100, silicate = 50, carbon = 30 };
    public float refineryBuildTime = 15f;
    public float refineryRadius = 10f;
    public float refineryHealth = 450f;
    [Tooltip("Used when a faction has no refinery of its own. Empty = the Grix Rafinerija for now")]
    public GameObject refineryPrefab;
    [Tooltip("Empty = Assets/Prefabs/Grix/Rafinerija - Refinery.prefab")]
    public GameObject grixRefinery;

    [Header("Worker bot")]
    public ResourceCost workerBotCost = new ResourceCost { iron = 8, copper = 2 };
    [Tooltip("Seconds per mining bot (Free Build makes it 0.5)")]
    public float workerBotTrainTime = 7f;
    [Tooltip("Size of mining bots: scales the model and its click and hit radius")]
    [Min(0.1f)] public float botScale = 1f;
    [Tooltip("Used when a faction has no mining bot of its own. Empty = Assets/Prefabs/Worker Bot.prefab")]
    public GameObject workerBotPrefab;
    [Tooltip("Empty = Assets/Prefabs/Grix/Rudar - Mining Bot.prefab")]
    public GameObject grixWorkerBot;
    [Tooltip("Empty = use the default mining bot")]
    public GameObject fairwayWorkerBot;
    [Tooltip("Empty = use the default mining bot")]
    public GameObject postibuleWorkerBot;
    public float botSpeed = 14f;
    public float botTurnSpeedDegrees = 240f;
    public float botCargoCapacity = 10f;
    public float botCollectorRadius = 8f;
    public float botMiningStandOff = 5f;
    [Tooltip("Loose ore within this distance of the asteroid a bot is working on gets collected, even after the asteroid breaks up")]
    public float botGatherRadius = 30f;
    public float botFireInterval = 0.6f;
    public float botProjectileSpeed = 45f;
    public float botSelectRadius = 2.5f;
    public float botHealth = 40f;
}

class BuildCatalog_Baker : Baker<BuildCatalog_Authoring>
{
    public override void Bake(BuildCatalog_Authoring a)
    {
        Entity entity = GetEntity(TransformUsageFlags.None);
        AddComponent(entity, new BuildCatalog
        {
            placementRange = a.placementRange,
            freeBuild = a.freeBuild,
            workerFactoryCost = a.workerFactoryCost,
            workerFactoryBuildTime = math.max(0.1f, a.workerFactoryBuildTime),
            workerFactoryRadius = math.max(1f, a.workerFactoryRadius),
            workerFactoryHealth = math.max(1f, a.workerFactoryHealth),
            storageCost = a.storageCost,
            storageBuildTime = math.max(0.1f, a.storageBuildTime),
            storageRadius = math.max(1f, a.storageRadius),
            storageHealth = math.max(1f, a.storageHealth),
            refineryCost = a.refineryCost,
            refineryBuildTime = math.max(0.1f, a.refineryBuildTime),
            refineryRadius = math.max(1f, a.refineryRadius),
            refineryHealth = math.max(1f, a.refineryHealth),
            workerBotCost = a.workerBotCost,
            workerBotTrainTime = math.max(0.1f, a.workerBotTrainTime),
            botSpeed = a.botSpeed,
            botTurnSpeed = math.radians(a.botTurnSpeedDegrees),
            botCargoCapacity = math.max(1f, a.botCargoCapacity),
            botCollectorRadius = a.botCollectorRadius,
            botMiningStandOff = a.botMiningStandOff,
            botGatherRadius = a.botGatherRadius,
            botFireInterval = math.max(0.05f, a.botFireInterval),
            botProjectileSpeed = a.botProjectileSpeed,
            botSelectRadius = a.botSelectRadius,
            botHealth = math.max(1f, a.botHealth),
            botScale = math.max(0.1f, a.botScale),
            startingResources = a.startingResources
        });

        DynamicBuffer<FactionBuildingPrefab> buildings = AddBuffer<FactionBuildingPrefab>(entity);
        AddBuilding(buildings, BuildingId.WorkerFactory, CombatFaction.None, a.workerFactoryPrefab, "Assets/Prefabs/Worker Factory.prefab");
        AddBuilding(buildings, BuildingId.WorkerFactory, CombatFaction.Grix, a.grixWorkerFactory, "Assets/Prefabs/Grix/Tvornica - Worker Factory.prefab");
        AddBuilding(buildings, BuildingId.WorkerFactory, CombatFaction.FairwayDominion, a.fairwayWorkerFactory, "Assets/Prefabs/Fairway Dominion/Roboterstube - Worker Factory.prefab");
        AddBuilding(buildings, BuildingId.WorkerFactory, CombatFaction.PostibuleSanctuary, a.postibuleWorkerFactory, "Assets/Prefabs/Postibule Sanctuary/Robotkammare - Worker Factory.prefab");
        AddBuilding(buildings, BuildingId.Storage, CombatFaction.None, a.storagePrefab, "Assets/Prefabs/Storage Depot.prefab");
        AddBuilding(buildings, BuildingId.Storage, CombatFaction.Grix, a.grixStorage, "Assets/Prefabs/Grix/Skladiste - Storage Depot.prefab");
        AddBuilding(buildings, BuildingId.Refinery, CombatFaction.None, a.refineryPrefab, "Assets/Prefabs/Grix/Rafinerija - Refinery.prefab");
        AddBuilding(buildings, BuildingId.Refinery, CombatFaction.Grix, a.grixRefinery, "Assets/Prefabs/Grix/Rafinerija - Refinery.prefab");

        DynamicBuffer<FactionUnitPrefab> units = AddBuffer<FactionUnitPrefab>(entity);
        AddUnit(units, UnitId.WorkerBot, CombatFaction.Grix, a.grixWorkerBot, "Assets/Prefabs/Grix/Rudar - Mining Bot.prefab");
        AddUnit(units, UnitId.WorkerBot, CombatFaction.FairwayDominion, a.fairwayWorkerBot, null);
        AddUnit(units, UnitId.WorkerBot, CombatFaction.PostibuleSanctuary, a.postibuleWorkerBot, null);

        GameObject bot = Resolve(a.workerBotPrefab, "Assets/Prefabs/Worker Bot.prefab");
        AddComponent(entity, new WorkerBotPrefab
        {
            prefab = bot != null ? GetEntity(bot, TransformUsageFlags.Dynamic) : Entity.Null
        });
    }

    private void AddBuilding(DynamicBuffer<FactionBuildingPrefab> buffer, BuildingId id, CombatFaction faction,
                             GameObject assigned, string fallbackPath)
    {
        GameObject prefab = Resolve(assigned, fallbackPath);
        if (prefab == null)
            return;
        buffer.Add(new FactionBuildingPrefab
        {
            id = id,
            faction = faction,
            prefab = GetEntity(prefab, TransformUsageFlags.Dynamic)
        });
    }

    private void AddUnit(DynamicBuffer<FactionUnitPrefab> buffer, UnitId unit, CombatFaction faction,
                         GameObject assigned, string fallbackPath)
    {
        GameObject prefab = Resolve(assigned, fallbackPath);
        if (prefab == null)
            return;
        buffer.Add(new FactionUnitPrefab
        {
            unit = unit,
            faction = faction,
            prefab = GetEntity(prefab, TransformUsageFlags.Dynamic)
        });
    }

    private GameObject Resolve(GameObject assigned, string fallbackPath)
    {
        GameObject prefab = assigned;
#if UNITY_EDITOR
        if (prefab == null && !string.IsNullOrEmpty(fallbackPath))
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(fallbackPath);
#endif
        if (prefab != null)
            DependsOn(prefab);
        return prefab;
    }
}
