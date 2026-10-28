using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Turns BuildRequests into buildings, grows them while under construction, and runs each
// factory's training queue. Bots appear beside their factory and wait for orders.
public partial struct BuildingSystem_Dots : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BuildCatalog>();
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityManager em = state.EntityManager;
        Entity catalogEntity = SystemAPI.GetSingletonEntity<BuildCatalog>();
        BuildCatalog catalog = SystemAPI.GetComponent<BuildCatalog>(catalogEntity);
        float dt = SystemAPI.Time.DeltaTime;

        if (!TryGetPlayer(ref state, out Entity player, out float3 playerPosition))
            return;

        CombatFaction faction = SystemAPI.HasSingleton<AsteroidFieldConfig>()
            ? SystemAPI.GetSingleton<AsteroidFieldConfig>().playerFaction
            : CombatFaction.None;

        PlaceRequestedBuildings(ref state, em, catalogEntity, catalog, player, playerPosition, faction);
        QueueRequestedBots(ref state, em, catalog, player);
        Construct(ref state, catalog, dt);
        Train(ref state, em, catalogEntity, catalog, dt);
    }

    private bool TryGetPlayer(ref SystemState state, out Entity player, out float3 position)
    {
        player = Entity.Null;
        position = float3.zero;
        foreach (var (transform, entity) in
                 SystemAPI.Query<RefRO<LocalTransform>>().WithAll<InputData_Dots, CargoItem>().WithEntityAccess())
        {
            player = entity;
            position = transform.ValueRO.Position;
            return true;
        }
        return false;
    }

    private void PlaceRequestedBuildings(ref SystemState state, EntityManager em, Entity catalogEntity, BuildCatalog catalog,
                                         Entity player, float3 playerPosition, CombatFaction faction)
    {
        EntityQuery requestQuery = SystemAPI.QueryBuilder().WithAll<BuildRequest>().Build();
        if (requestQuery.IsEmpty)
            return;

        NativeArray<Entity> requests = requestQuery.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < requests.Length; i++)
        {
            BuildRequest request = em.GetComponentData<BuildRequest>(requests[i]);
            em.DestroyEntity(requests[i]);

            float radius = catalog.workerFactoryRadius;
            ResourceCost cost = catalog.workerFactoryCost;
            float3 position = new float3(request.position.x, 0f, request.position.z);

            if (!IsPlacementValid(ref state, position, radius, playerPosition, catalog.placementRange))
            {
                UnityEngine.Debug.Log("Can't build there: too far from the ship or overlapping something.");
                continue;
            }

            DynamicBuffer<CargoItem> cargo = em.GetBuffer<CargoItem>(player);
            if (!catalog.freeBuild)
            {
                if (!cost.CanAfford(cargo))
                {
                    UnityEngine.Debug.Log("Not enough resources for " + request.id + ".");
                    continue;
                }
                cost.Pay(cargo);
            }

            Entity prefab = FindPrefab(em, catalogEntity, request.id, faction);
            if (prefab == Entity.Null)
            {
                UnityEngine.Debug.LogWarning("No prefab for " + request.id + "; add one to BuildCatalog_Authoring.");
                continue;
            }

            Entity building = em.Instantiate(prefab);
            LocalTransform t = em.GetComponentData<LocalTransform>(building);
            float baseScale = t.Scale;
            float buildTime = catalog.freeBuild ? 0.5f : catalog.workerFactoryBuildTime;
            t.Position = position;
            t.Rotation = request.rotation;
            t.Scale = baseScale * 0.2f;
            em.SetComponentData(building, t);

            em.AddComponentData(building, new Building
            {
                id = request.id,
                faction = faction,
                radius = radius,
                buildTime = buildTime,
                progress = 0f,
                baseScale = baseScale
            });
            em.AddComponentData(building, new Selectable { radius = radius, kind = 1 });
            Combat.ApplyTargetable(em, building,
                new Targetable { layer = CollisionLayer.Building, radius = radius, density = 0.2f },
                catalog.workerFactoryHealth, faction);
            em.AddComponentData(building, new TrainQueue());
        }
    }

    private bool IsPlacementValid(ref SystemState state, float3 position, float radius, float3 playerPosition, float range)
    {
        if (math.distance(position.xz, playerPosition.xz) > range)
            return false;

        foreach (var (building, transform) in SystemAPI.Query<RefRO<Building>, RefRO<LocalTransform>>())
            if (math.distance(position.xz, transform.ValueRO.Position.xz) < radius + building.ValueRO.radius)
                return false;

        foreach (var (asteroid, transform) in SystemAPI.Query<RefRO<AsteroidHex>, RefRO<LocalTransform>>())
            if (math.distance(position.xz, transform.ValueRO.Position.xz) < radius + asteroid.ValueRO.radius)
                return false;

        return true;
    }

    private static Entity FindPrefab(EntityManager em, Entity catalogEntity, BuildingId id, CombatFaction faction)
    {
        DynamicBuffer<FactionBuildingPrefab> prefabs = em.GetBuffer<FactionBuildingPrefab>(catalogEntity);
        Entity fallback = Entity.Null;
        for (int i = 0; i < prefabs.Length; i++)
        {
            if (prefabs[i].id != id)
                continue;
            if (prefabs[i].faction == faction)
                return prefabs[i].prefab;
            if (prefabs[i].faction == CombatFaction.None)
                fallback = prefabs[i].prefab;
        }
        return fallback;
    }

    private void QueueRequestedBots(ref SystemState state, EntityManager em, BuildCatalog catalog, Entity player)
    {
        EntityQuery requestQuery = SystemAPI.QueryBuilder().WithAll<TrainRequest>().Build();
        if (requestQuery.IsEmpty)
            return;

        NativeArray<Entity> requests = requestQuery.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < requests.Length; i++)
        {
            Entity factory = em.GetComponentData<TrainRequest>(requests[i]).building;
            em.DestroyEntity(requests[i]);

            if (!em.Exists(factory) || !em.HasComponent<Building>(factory) || !em.GetComponentData<Building>(factory).IsComplete)
                continue;

            if (!catalog.freeBuild)
            {
                DynamicBuffer<CargoItem> cargo = em.GetBuffer<CargoItem>(player);
                if (!catalog.workerBotCost.CanAfford(cargo))
                {
                    UnityEngine.Debug.Log("Not enough resources for a worker.");
                    continue;
                }
                catalog.workerBotCost.Pay(cargo);
            }

            TrainQueue queue = em.GetComponentData<TrainQueue>(factory);
            queue.queued++;
            em.SetComponentData(factory, queue);
        }
    }

    private void Construct(ref SystemState state, BuildCatalog catalog, float dt)
    {
        foreach (var (building, transform) in SystemAPI.Query<RefRW<Building>, RefRW<LocalTransform>>())
        {
            if (building.ValueRO.IsComplete)
                continue;
            building.ValueRW.progress = math.min(building.ValueRO.buildTime, building.ValueRO.progress + dt);
            float t = building.ValueRO.progress / building.ValueRO.buildTime;
            transform.ValueRW.Scale = building.ValueRO.baseScale * math.lerp(0.2f, 1f, t);
        }
    }

    private void Train(ref SystemState state, EntityManager em, Entity catalogEntity, BuildCatalog catalog, float dt)
    {
        Entity botPrefab = em.GetComponentData<WorkerBotPrefab>(catalogEntity).prefab;
        float trainTime = catalog.freeBuild ? 0.5f : catalog.workerBotTrainTime;

        var spawns = new NativeList<Entity>(4, Allocator.Temp); // factories that finished a bot this frame
        foreach (var (building, queue, entity) in
                 SystemAPI.Query<RefRO<Building>, RefRW<TrainQueue>>().WithEntityAccess())
        {
            if (!building.ValueRO.IsComplete || queue.ValueRO.queued <= 0)
                continue;
            queue.ValueRW.progress += dt;
            if (queue.ValueRO.progress < trainTime)
                continue;
            queue.ValueRW.progress = 0f;
            queue.ValueRW.queued--;
            spawns.Add(entity);
        }

        if (botPrefab == Entity.Null)
        {
            if (spawns.Length > 0)
                UnityEngine.Debug.LogWarning("No worker bot prefab; add one to BuildCatalog_Authoring.");
            return;
        }

        int botCount = SystemAPI.QueryBuilder().WithAll<WorkerBot>().Build().CalculateEntityCount();
        for (int i = 0; i < spawns.Length; i++)
        {
            Entity factory = spawns[i];
            LocalTransform factoryTransform = em.GetComponentData<LocalTransform>(factory);
            Building building = em.GetComponentData<Building>(factory);
            TrainQueue queue = em.GetComponentData<TrainQueue>(factory);

            // Park bots in a ring around the factory, spaced out by how many it has made.
            float angle = queue.trained * 0.7f;
            float ring = building.radius + 4f + (queue.trained / 9) * 4f;
            float3 offset = new float3(math.sin(angle), 0f, math.cos(angle)) * ring;

            Entity bot = em.Instantiate(botPrefab);
            em.SetComponentData(bot, LocalTransform.FromPositionRotation(
                factoryTransform.Position + offset,
                quaternion.LookRotationSafe(offset, math.up())));
            botCount++;
            em.AddComponentData(bot, new WorkerBot { state = BotState.Idle, home = factory, number = botCount });
            em.AddComponentData(bot, new Selectable { radius = catalog.botSelectRadius, kind = 0 });
            Combat.ApplyTargetable(em, bot,
                new Targetable { layer = CollisionLayer.Bot, radius = catalog.botSelectRadius, density = 0f },
                catalog.botHealth, building.faction);
            em.AddComponentData(bot, new OreCollector
            {
                radius = catalog.botCollectorRadius,
                pickupDistance = 1.5f,
                pullAcceleration = 60f,
                maxPullSpeed = 40f,
                capacity = catalog.botCargoCapacity
            });
            DynamicBuffer<CargoItem> cargo = em.AddBuffer<CargoItem>(bot);
            for (int c = 0; c < AsteroidFieldRules.ResourceCount; c++)
                cargo.Add(new CargoItem());

            queue.trained++;
            em.SetComponentData(factory, queue);
        }
    }
}
