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
        Train(ref state, em, catalogEntity, catalog, player, dt);
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

            if (SystemAPI.HasSingleton<GameRules>() && !SystemAPI.GetSingleton<GameRules>().Allows(request.id))
            {
                UnityEngine.Debug.Log(request.id + " is switched off in Bot Config.");
                continue;
            }

            float radius = catalog.RadiusOf(request.id);
            ResourceCost cost = catalog.CostOf(request.id);
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
            float buildTime = catalog.freeBuild ? 0.5f : catalog.BuildTimeOf(request.id);
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
                catalog.HealthOf(request.id), faction);
            if (request.id == BuildingId.WorkerFactory)
            {
                em.AddComponentData(building, new TrainQueue());
                em.AddBuffer<QueuedUnit>(building);
            }
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

    // The faction's own model for a unit (e.g. the Grix Rudar), or the default if it has none.
    private static Entity FindUnitPrefab(EntityManager em, Entity catalogEntity, UnitId unit, CombatFaction faction, Entity fallback)
    {
        if (!em.HasBuffer<FactionUnitPrefab>(catalogEntity))
            return fallback;
        DynamicBuffer<FactionUnitPrefab> prefabs = em.GetBuffer<FactionUnitPrefab>(catalogEntity);
        for (int i = 0; i < prefabs.Length; i++)
            if (prefabs[i].unit == unit && prefabs[i].faction == faction && prefabs[i].prefab != Entity.Null)
                return prefabs[i].prefab;
        return fallback;
    }

    // Adds requested units to factory queues, and takes out ones the player removed. Nothing is
    // paid here: a unit is paid for when it reaches the front of the queue.
    private void QueueRequestedBots(ref SystemState state, EntityManager em, BuildCatalog catalog, Entity player)
    {
        GameRules rules = SystemAPI.HasSingleton<GameRules>() ? SystemAPI.GetSingleton<GameRules>() : GameRules.Default;

        EntityQuery addQuery = SystemAPI.QueryBuilder().WithAll<TrainRequest>().Build();
        if (!addQuery.IsEmpty)
        {
            NativeArray<Entity> requests = addQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < requests.Length; i++)
            {
                TrainRequest request = em.GetComponentData<TrainRequest>(requests[i]);
                em.DestroyEntity(requests[i]);
                Entity factory = request.building;
                if (!em.Exists(factory) || !em.HasBuffer<QueuedUnit>(factory) || !em.GetComponentData<Building>(factory).IsComplete)
                    continue;
                if (!UnitRules.Implemented(request.unit) || !rules.Allows(request.unit))
                    continue;
                DynamicBuffer<QueuedUnit> queue = em.GetBuffer<QueuedUnit>(factory);
                if (queue.Length < UnitRules.MaxQueue)
                    queue.Add(new QueuedUnit { unit = request.unit });
            }
            requests.Dispose();
        }

        EntityQuery removeQuery = SystemAPI.QueryBuilder().WithAll<QueueRemoveRequest>().Build();
        if (!removeQuery.IsEmpty)
        {
            NativeArray<Entity> requests = removeQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < requests.Length; i++)
            {
                QueueRemoveRequest request = em.GetComponentData<QueueRemoveRequest>(requests[i]);
                em.DestroyEntity(requests[i]);
                if (!em.Exists(request.building) || !em.HasBuffer<QueuedUnit>(request.building))
                    continue;
                DynamicBuffer<QueuedUnit> queue = em.GetBuffer<QueuedUnit>(request.building);
                if (request.index >= 1 && request.index < queue.Length) // the one being built stays
                    queue.RemoveAt(request.index);
            }
            requests.Dispose();
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

    // Each factory works through its queue. The front unit is paid for when it starts; if the player
    // can't afford it (or its type is switched off in Bot Config) the queue stalls until they can.
    private void Train(ref SystemState state, EntityManager em, Entity catalogEntity, BuildCatalog catalog, Entity player, float dt)
    {
        Entity botPrefab = em.GetComponentData<WorkerBotPrefab>(catalogEntity).prefab;
        GameRules rules = SystemAPI.HasSingleton<GameRules>() ? SystemAPI.GetSingleton<GameRules>() : GameRules.Default;
        DynamicBuffer<CargoItem> cargo = em.GetBuffer<CargoItem>(player);

        var spawns = new NativeList<Entity>(4, Allocator.Temp); // factories that finished a unit this frame
        var spawnUnits = new NativeList<UnitId>(4, Allocator.Temp);
        foreach (var (building, progress, queue, entity) in
                 SystemAPI.Query<RefRO<Building>, RefRW<TrainQueue>, DynamicBuffer<QueuedUnit>>().WithEntityAccess())
        {
            if (!building.ValueRO.IsComplete)
                continue;
            if (queue.Length == 0)
            {
                progress.ValueRW.progress = 0f;
                progress.ValueRW.stalled = false;
                continue;
            }

            UnitId unit = queue[0].unit;
            if (!rules.Allows(unit))
            {
                progress.ValueRW.stalled = true;
                continue;
            }
            if (!progress.ValueRO.paid)
            {
                ResourceCost cost = catalog.UnitCost(unit);
                if (!catalog.freeBuild && !cost.CanAfford(cargo))
                {
                    progress.ValueRW.stalled = true;
                    continue;
                }
                if (!catalog.freeBuild)
                    cost.Pay(cargo);
                progress.ValueRW.paid = true;
            }
            progress.ValueRW.stalled = false;
            progress.ValueRW.progress += dt;
            if (progress.ValueRO.progress < catalog.UnitTime(unit))
                continue;

            progress.ValueRW.progress = 0f;
            progress.ValueRW.paid = false;
            queue.RemoveAt(0);
            spawns.Add(entity);
            spawnUnits.Add(unit);
        }

        int botCount = SystemAPI.QueryBuilder().WithAll<WorkerBot>().Build().CalculateEntityCount();
        for (int i = 0; i < spawns.Length; i++)
        {
            if (spawnUnits[i] != UnitId.WorkerBot)
                continue; // other unit types don't exist yet
            Entity factory = spawns[i];
            LocalTransform factoryTransform = em.GetComponentData<LocalTransform>(factory);
            Building building = em.GetComponentData<Building>(factory);
            Entity unitPrefab = FindUnitPrefab(em, catalogEntity, UnitId.WorkerBot, building.faction, botPrefab);
            if (unitPrefab == Entity.Null)
            {
                UnityEngine.Debug.LogWarning("No mining bot prefab; add one to BuildCatalog_Authoring.");
                continue;
            }
            TrainQueue queue = em.GetComponentData<TrainQueue>(factory);
            float scale = catalog.botScale > 0f ? catalog.botScale : 1f;
            float botRadius = catalog.botSelectRadius * scale;

            // Park bots in a ring around the factory, spaced out by how many it has made.
            float angle = queue.trained * 0.7f;
            float ring = building.radius + botRadius * 1.6f + 2f + (queue.trained / 9) * botRadius * 2.2f;
            float3 offset = new float3(math.sin(angle), 0f, math.cos(angle)) * ring;

            Entity bot = em.Instantiate(unitPrefab);
            LocalTransform t = em.GetComponentData<LocalTransform>(bot); // keep the prefab's own scale
            t.Position = factoryTransform.Position + offset;
            t.Rotation = quaternion.LookRotationSafe(offset, math.up());
            t.Scale *= scale;
            em.SetComponentData(bot, t);
            botCount++;
            em.AddComponentData(bot, new WorkerBot { state = BotState.Idle, home = factory, number = botCount });
            em.AddComponentData(bot, new Selectable { radius = botRadius, kind = 0 });
            Combat.ApplyTargetable(em, bot,
                new Targetable { layer = CollisionLayer.Bot, radius = botRadius, density = 0f },
                catalog.botHealth, building.faction);
            em.AddComponentData(bot, new OreCollector
            {
                radius = math.max(catalog.botCollectorRadius, botRadius * 2f),
                pickupDistance = math.max(1.5f, botRadius),
                pullAcceleration = 60f,
                maxPullSpeed = 40f,
                capacity = catalog.botCargoCapacity
            });
            DynamicBuffer<CargoItem> held = em.AddBuffer<CargoItem>(bot);
            for (int c = 0; c < AsteroidFieldRules.ResourceCount; c++)
                held.Add(new CargoItem());

            queue.trained++;
            em.SetComponentData(factory, queue);
        }
        spawns.Dispose();
        spawnUnits.Dispose();
    }
}
