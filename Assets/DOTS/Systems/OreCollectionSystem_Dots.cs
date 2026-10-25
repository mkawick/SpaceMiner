using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Ore pops out of the asteroid to a resting spot, waits, and once a ship's collector comes within
// range it is pulled in, speeding up as it goes. When it reaches the ship it is added to cargo.
[BurstCompile]
[UpdateAfter(typeof(SpaceShipMovementSystem_Dots))]
public partial struct OreCollectionSystem_Dots : ISystem
{
    private struct CollectorInfo
    {
        public Entity entity;
        public float3 position;
        public OreCollector settings;
    }

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<OreChunk>();
        state.RequireForUpdate<MiningConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();

        float dt = SystemAPI.Time.DeltaTime;
        MiningConfig mining = SystemAPI.GetSingleton<MiningConfig>();

        var collectors = new NativeList<CollectorInfo>(4, Allocator.Temp);
        foreach (var (collector, transform, entity) in
                 SystemAPI.Query<RefRO<OreCollector>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            collectors.Add(new CollectorInfo
            {
                entity = entity,
                position = transform.ValueRO.Position,
                settings = collector.ValueRO
            });
        }

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (chunk, transform, entity) in
                 SystemAPI.Query<RefRW<OreChunk>, RefRW<LocalTransform>>().WithEntityAccess())
        {
            OreChunk ore = chunk.ValueRO;
            float3 position = transform.ValueRO.Position;

            if (ore.state == OreState.PoppingOut)
            {
                ore.progress = math.min(1f, ore.progress + dt / mining.oreScatterTime);
                float eased = 1f - (1f - ore.progress) * (1f - ore.progress); // fast out, gentle stop
                position = math.lerp(ore.from, ore.rest, eased);
                if (ore.progress >= 1f)
                    ore.state = OreState.Resting;
            }

            // Not yet claimed: the nearest collector in range starts pulling it in.
            if (ore.state != OreState.Collecting)
            {
                float bestSqr = float.MaxValue;
                for (int i = 0; i < collectors.Length; i++)
                {
                    float dSqr = math.distancesq(position, collectors[i].position);
                    float r = collectors[i].settings.radius;
                    if (dSqr <= r * r && dSqr < bestSqr)
                    {
                        bestSqr = dSqr;
                        ore.collector = collectors[i].entity;
                        ore.state = OreState.Collecting;
                        ore.speed = 0f;
                    }
                }
            }

            if (ore.state == OreState.Collecting)
            {
                int index = -1;
                for (int i = 0; i < collectors.Length; i++)
                    if (collectors[i].entity == ore.collector)
                        index = i;

                if (index < 0)
                {
                    ore.state = OreState.Resting; // the ship is gone; leave the ore where it is
                }
                else
                {
                    CollectorInfo c = collectors[index];
                    ore.speed = math.min(ore.speed + c.settings.pullAcceleration * dt, c.settings.maxPullSpeed);
                    float3 toShip = c.position - position;
                    float distance = math.length(toShip);
                    float step = ore.speed * dt;

                    if (distance <= c.settings.pickupDistance || step >= distance)
                    {
                        if (SystemAPI.HasBuffer<CargoItem>(c.entity))
                        {
                            DynamicBuffer<CargoItem> cargo = SystemAPI.GetBuffer<CargoItem>(c.entity);
                            int slot = (int)ore.type;
                            if (slot < cargo.Length)
                            {
                                CargoItem item = cargo[slot];
                                item.amount += ore.amount;
                                cargo[slot] = item;
                            }
                        }
                        ecb.DestroyEntity(entity);
                        continue;
                    }

                    position += toShip / distance * step;
                }
            }

            chunk.ValueRW = ore;
            transform.ValueRW.Position = position;
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
        collectors.Dispose();
    }
}
