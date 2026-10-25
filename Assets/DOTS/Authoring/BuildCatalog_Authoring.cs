using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Put one of these in the subscene (the AsteroidField object is a good place).
// Holds build costs, timings and the building / bot prefabs for each faction.
public class BuildCatalog_Authoring : MonoBehaviour
{
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
    [Tooltip("Used when a faction has no prefab of its own. Empty = Assets/Prefabs/Worker Factory.prefab")]
    public GameObject workerFactoryPrefab;
    [Tooltip("Empty = Assets/Prefabs/Grix/Tvornica - Worker Factory.prefab")]
    public GameObject grixWorkerFactory;
    [Tooltip("Empty = Assets/Prefabs/Fairway Dominion/Roboterstube - Worker Factory.prefab")]
    public GameObject fairwayWorkerFactory;
    [Tooltip("Empty = Assets/Prefabs/Postibule Sanctuary/Robotkammare - Worker Factory.prefab")]
    public GameObject postibuleWorkerFactory;

    [Header("Worker bot")]
    public ResourceCost workerBotCost = new ResourceCost { iron = 15, copper = 5, carbon = 5 };
    public float workerBotTrainTime = 8f;
    [Tooltip("Empty = Assets/Prefabs/Worker Bot.prefab")]
    public GameObject workerBotPrefab;
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
            botSelectRadius = a.botSelectRadius
        });

        DynamicBuffer<FactionBuildingPrefab> buildings = AddBuffer<FactionBuildingPrefab>(entity);
        AddBuilding(buildings, BuildingId.WorkerFactory, CombatFaction.None, a.workerFactoryPrefab, "Assets/Prefabs/Worker Factory.prefab");
        AddBuilding(buildings, BuildingId.WorkerFactory, CombatFaction.Grix, a.grixWorkerFactory, "Assets/Prefabs/Grix/Tvornica - Worker Factory.prefab");
        AddBuilding(buildings, BuildingId.WorkerFactory, CombatFaction.FairwayDominion, a.fairwayWorkerFactory, "Assets/Prefabs/Fairway Dominion/Roboterstube - Worker Factory.prefab");
        AddBuilding(buildings, BuildingId.WorkerFactory, CombatFaction.PostibuleSanctuary, a.postibuleWorkerFactory, "Assets/Prefabs/Postibule Sanctuary/Robotkammare - Worker Factory.prefab");

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

    private GameObject Resolve(GameObject assigned, string fallbackPath)
    {
        GameObject prefab = assigned;
#if UNITY_EDITOR
        if (prefab == null)
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(fallbackPath);
#endif
        if (prefab != null)
            DependsOn(prefab);
        return prefab;
    }
}
