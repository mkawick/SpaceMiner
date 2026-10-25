using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Worker bots: wait (Idle), fly to a point (Moving), or mine an asteroid (Mining): keep a short
// distance, shoot it so ore pops out, and pull the ore in. Loose ore around the work site that the
// collector can't reach from the mining spot gets fetched, including the last pieces after the
// asteroid breaks up. When the hold is full they fly home (Returning), unload into the player's
// cargo, and come back to the same site; once it's clean they move to the nearest asteroid.
[BurstCompile]
[UpdateAfter(typeof(BuildingSystem_Dots))]
public partial struct WorkerBotSystem_Dots : ISystem
{
    private struct AsteroidInfo
    {
        public Entity entity;
        public float2 position;
    }

    private const float SearchRadius = 200f;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<WorkerBot>();
        state.RequireForUpdate<BuildCatalog>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();

        BuildCatalog catalog = SystemAPI.GetSingleton<BuildCatalog>();
        float dt = SystemAPI.Time.DeltaTime;

        Entity player = Entity.Null;
        foreach (var (input, entity) in SystemAPI.Query<RefRO<InputData_Dots>>().WithAll<CargoItem>().WithEntityAccess())
            player = entity;

        // Collect the bots first, then update them one at a time, so reading other entities'
        // transforms never overlaps a write-enabled query over LocalTransform.
        var bots = new NativeList<Entity>(Allocator.Temp);
        foreach (var (worker, entity) in SystemAPI.Query<RefRO<WorkerBot>>().WithEntityAccess())
            bots.Add(entity);

        var asteroids = new NativeList<AsteroidInfo>(Allocator.Temp);
        bool asteroidsLoaded = false;
        var looseOre = new NativeList<float3>(Allocator.Temp);
        bool oreLoaded = false;
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        for (int b = 0; b < bots.Length; b++)
        {
            Entity entity = bots[b];
            WorkerBot bot = SystemAPI.GetComponent<WorkerBot>(entity);
            LocalTransform t = SystemAPI.GetComponent<LocalTransform>(entity);
            DynamicBuffer<CargoItem> held = SystemAPI.GetBuffer<CargoItem>(entity);

            switch (bot.state)
            {
                case BotState.Moving:
                    if (MoveTowards(ref t, bot.destination, catalog, dt, 1.5f))
                        bot.state = BotState.Idle;
                    break;

                case BotState.Mining:
                {
                    if (Total(held) >= catalog.botCargoCapacity)
                    {
                        bot.state = BotState.Returning;
                        break;
                    }

                    bool targetValid = IsAsteroid(ref state, bot.target);
                    if (targetValid)
                    {
                        bot.workSite = SystemAPI.GetComponent<LocalTransform>(bot.target).Position;
                        bot.hasWorkSite = true;
                    }

                    // 1. Loose ore around the work site that the collector can't reach from here: go get it.
                    if (bot.hasWorkSite)
                    {
                        if (!oreLoaded) { LoadLooseOre(ref state, ref looseOre); oreLoaded = true; }
                        float reach = catalog.botCollectorRadius * 0.9f;
                        if (NearestLooseOre(looseOre, bot.workSite.xz, catalog.botGatherRadius, t.Position.xz, reach, out float3 orePosition))
                        {
                            MoveTowards(ref t, orePosition, catalog, dt, catalog.botCollectorRadius * 0.5f);
                            break;
                        }
                    }

                    // 2. The asteroid is gone and nothing is left to pick up: move on to the nearest one.
                    if (!targetValid)
                    {
                        if (!asteroidsLoaded) { LoadAsteroids(ref state, ref asteroids); asteroidsLoaded = true; }
                        float2 from = bot.hasWorkSite ? bot.workSite.xz : t.Position.xz;
                        bot.target = Nearest(asteroids, from, SearchRadius);
                        if (bot.target == Entity.Null)
                        {
                            bot.hasWorkSite = false;
                            bot.state = Total(held) > 0f ? BotState.Returning : BotState.Idle;
                            break;
                        }
                        bot.workSite = SystemAPI.GetComponent<LocalTransform>(bot.target).Position;
                        bot.hasWorkSite = true;
                    }

                    // 3. Mine: hold position a short way off the surface and shoot.
                    float3 rock = bot.workSite;
                    float rockRadius = SystemAPI.GetComponent<AsteroidHex>(bot.target).radius;
                    float3 away = t.Position - rock;
                    away.y = 0f;
                    away = math.normalizesafe(away, new float3(0f, 0f, -1f));
                    float standOff = rockRadius + catalog.botMiningStandOff;
                    float3 spot = rock + away * standOff;

                    bool inPlace = MoveTowards(ref t, spot, catalog, dt, 1f);
                    float3 toRock = -away;
                    if (math.distance(t.Position.xz, rock.xz) <= standOff + 2f)
                    {
                        Face(ref t, toRock, catalog, dt);
                        bot.fireTimer -= dt;
                        if (inPlace && bot.fireTimer <= 0f)
                        {
                            bot.fireTimer = catalog.botFireInterval;
                            Entity request = ecb.CreateEntity();
                            ecb.AddComponent(request, new ProjectileRequest
                            {
                                projectileEnum = 0,
                                damageIfHits = 1,
                                ownerId = entity.Index,
                                position = t.Position + toRock * (catalog.botSelectRadius + 0.5f),
                                dir = quaternion.LookRotationSafe(toRock, math.up()),
                                speed = catalog.botProjectileSpeed
                            });
                        }
                    }
                    break;
                }

                case BotState.Returning:
                {
                    if (!IsBuilding(ref state, bot.home))
                        bot.home = NearestFactory(ref state, t.Position.xz);
                    if (bot.home == Entity.Null)
                    {
                        bot.state = BotState.Idle;
                        break;
                    }

                    float3 home = SystemAPI.GetComponent<LocalTransform>(bot.home).Position;
                    float homeRadius = SystemAPI.GetComponent<Building>(bot.home).radius;
                    float3 dock = home + math.normalizesafe(t.Position - home, new float3(0f, 0f, 1f)) * (homeRadius + 2f);
                    if (!MoveTowards(ref t, dock, catalog, dt, 1.5f))
                        break;

                    // Unload into the player's cargo.
                    if (player != Entity.Null)
                    {
                        DynamicBuffer<CargoItem> playerCargo = SystemAPI.GetBuffer<CargoItem>(player);
                        for (int i = 0; i < held.Length && i < playerCargo.Length; i++)
                        {
                            CargoItem item = playerCargo[i];
                            item.amount += held[i].amount;
                            playerCargo[i] = item;
                        }
                    }
                    for (int i = 0; i < held.Length; i++)
                        held[i] = new CargoItem();

                    bot.state = BotState.Mining; // back to work; Mining picks a new rock if the old one is gone
                    break;
                }
            }

            SystemAPI.SetComponent(entity, bot);
            SystemAPI.SetComponent(entity, t);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
        asteroids.Dispose();
        looseOre.Dispose();
        bots.Dispose();
    }

    private bool IsAsteroid(ref SystemState state, Entity e)
    {
        return e != Entity.Null
               && SystemAPI.HasComponent<AsteroidHex>(e)
               && !SystemAPI.HasComponent<DestroyedEntityTag_Dots>(e);
    }

    private bool IsBuilding(ref SystemState state, Entity e)
    {
        return e != Entity.Null && SystemAPI.HasComponent<Building>(e);
    }

    private void LoadAsteroids(ref SystemState state, ref NativeList<AsteroidInfo> list)
    {
        foreach (var (transform, entity) in
                 SystemAPI.Query<RefRO<LocalTransform>>()
                          .WithAll<AsteroidHex>()
                          .WithNone<DestroyedEntityTag_Dots>()
                          .WithEntityAccess())
        {
            list.Add(new AsteroidInfo { entity = entity, position = transform.ValueRO.Position.xz });
        }
    }

    // Ore that has popped out and isn't already being pulled in by someone.
    private void LoadLooseOre(ref SystemState state, ref NativeList<float3> list)
    {
        foreach (var (chunk, transform) in SystemAPI.Query<RefRO<OreChunk>, RefRO<LocalTransform>>())
            if (chunk.ValueRO.state != OreState.Collecting)
                list.Add(transform.ValueRO.Position);
    }

    // Nearest loose ore (to the bot) that lies near the work site but outside the collector's reach.
    private static bool NearestLooseOre(NativeList<float3> ore, float2 site, float siteRadius, float2 bot, float reach, out float3 position)
    {
        position = float3.zero;
        float bestSqr = float.MaxValue;
        bool found = false;
        for (int i = 0; i < ore.Length; i++)
        {
            float2 p = ore[i].xz;
            if (math.distancesq(p, site) > siteRadius * siteRadius)
                continue;
            float dSqr = math.distancesq(p, bot);
            if (dSqr <= reach * reach || dSqr >= bestSqr)
                continue;
            bestSqr = dSqr;
            position = ore[i];
            found = true;
        }
        return found;
    }

    private static Entity Nearest(NativeList<AsteroidInfo> list, float2 from, float maxDistance)
    {
        Entity best = Entity.Null;
        float bestSqr = maxDistance * maxDistance;
        for (int i = 0; i < list.Length; i++)
        {
            float dSqr = math.distancesq(list[i].position, from);
            if (dSqr < bestSqr)
            {
                bestSqr = dSqr;
                best = list[i].entity;
            }
        }
        return best;
    }

    private Entity NearestFactory(ref SystemState state, float2 from)
    {
        Entity best = Entity.Null;
        float bestSqr = float.MaxValue;
        foreach (var (building, transform, entity) in
                 SystemAPI.Query<RefRO<Building>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            if (building.ValueRO.id != BuildingId.WorkerFactory || !building.ValueRO.IsComplete)
                continue;
            float dSqr = math.distancesq(transform.ValueRO.Position.xz, from);
            if (dSqr < bestSqr)
            {
                bestSqr = dSqr;
                best = entity;
            }
        }
        return best;
    }

    private static float Total(DynamicBuffer<CargoItem> cargo)
    {
        float total = 0f;
        for (int i = 0; i < cargo.Length; i++)
            total += cargo[i].amount;
        return total;
    }

    // Flies straight at the target on the play plane, turning to face where it's going.
    // Returns true once within arriveDistance.
    private static bool MoveTowards(ref LocalTransform t, float3 target, in BuildCatalog catalog, float dt, float arriveDistance)
    {
        float3 delta = target - t.Position;
        delta.y = 0f;
        float distance = math.length(delta);
        if (distance <= arriveDistance)
            return true;

        float3 direction = delta / distance;
        float step = math.min(catalog.botSpeed * dt, distance);
        t.Position += direction * step;
        t.Position.y = 0f;
        Face(ref t, direction, catalog, dt);
        return distance - step <= arriveDistance;
    }

    private static void Face(ref LocalTransform t, float3 direction, in BuildCatalog catalog, float dt)
    {
        direction.y = 0f;
        if (math.lengthsq(direction) < 1e-6f)
            return;
        quaternion goal = quaternion.LookRotationSafe(math.normalize(direction), math.up());
        float angle = math.angle(t.Rotation, goal);
        float maxStep = catalog.botTurnSpeed * dt;
        t.Rotation = angle <= maxStep ? goal : math.slerp(t.Rotation, goal, maxStep / angle);
    }
}
