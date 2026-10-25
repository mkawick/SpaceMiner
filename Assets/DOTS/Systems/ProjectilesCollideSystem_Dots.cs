using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Random = Unity.Mathematics.Random;

// Hit tests on the play plane using the hex grid instead of physics colliders.
// Each projectile checks the asteroids in its hex and the six around it, along the segment it
// travelled this frame. Each hit has a chance (MiningConfig.oreDropChance) to knock out a piece of
// ore, which pops out a short way from the asteroid toward the shooter. An asteroid breaks up once
// its ore runs out.
[BurstCompile]
[UpdateAfter(typeof(MoveEntitiesSystem_Dots))]
public partial struct ProjectilesCollideSystem_Dots : ISystem
{
    private struct AsteroidSlot
    {
        public Entity entity;
        public float2 position;
        public float radius;
    }

    private Random rng;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AsteroidFieldConfig>();
        state.RequireForUpdate<MiningConfig>();
        state.RequireForUpdate<Projectile>();
        rng = new Random(0x6E1Du ^ (uint)System.DateTime.Now.Ticks | 1u);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();

        Entity configEntity = SystemAPI.GetSingletonEntity<AsteroidFieldConfig>();
        float hexSize = SystemAPI.GetComponent<AsteroidFieldConfig>(configEntity).hexSize;
        MiningConfig mining = SystemAPI.GetComponent<MiningConfig>(configEntity);
        DynamicBuffer<OrePrefab> orePrefabs = SystemAPI.GetBuffer<OrePrefab>(configEntity);
        float dt = SystemAPI.Time.DeltaTime;

        // Bucket the asteroids by hex.
        EntityQuery asteroidQuery = SystemAPI.QueryBuilder()
            .WithAll<AsteroidHex, LocalTransform>()
            .WithNone<DestroyedEntityTag_Dots>()
            .Build();
        int asteroidCount = asteroidQuery.CalculateEntityCount();
        var grid = new NativeParallelMultiHashMap<int2, AsteroidSlot>(math.max(1, asteroidCount), Allocator.Temp);
        foreach (var (hexInfo, transform, asteroidEntity) in
                 SystemAPI.Query<RefRO<AsteroidHex>, RefRO<LocalTransform>>()
                          .WithNone<DestroyedEntityTag_Dots>()
                          .WithEntityAccess())
        {
            grid.Add(hexInfo.ValueRO.hex, new AsteroidSlot
            {
                entity = asteroidEntity,
                position = transform.ValueRO.Position.xz,
                radius = hexInfo.ValueRO.radius
            });
        }

        var ecb = new EntityCommandBuffer(Allocator.Temp);
        var depletedThisFrame = new NativeHashSet<Entity>(16, Allocator.Temp);

        foreach (var (transform, move, entity) in
                 SystemAPI.Query<RefRO<LocalTransform>, RefRO<MoveEntityTag_Dots>>()
                          .WithAll<Projectile>()
                          .WithNone<DestroyedEntityTag_Dots>()
                          .WithEntityAccess())
        {
            float2 end = transform.ValueRO.Position.xz;
            float2 direction = math.mul(move.ValueRO.dir, new float3(0f, 0f, 1f)).xz;
            float2 start = end - direction * move.ValueRO.speed * dt;
            int2 hex = HexGrid.WorldToHex(end, hexSize);

            AsteroidSlot target = default;
            float best = float.MaxValue;
            for (int n = 0; n < 7; n++)
            {
                int2 h = n == 6 ? hex : hex + HexGrid.EdgeNeighbour(n);
                if (!grid.TryGetFirstValue(h, out AsteroidSlot slot, out NativeParallelMultiHashMapIterator<int2> it))
                    continue;
                do
                {
                    if (depletedThisFrame.Contains(slot.entity))
                        continue;
                    float t = SegmentCircle(start, end, slot.position, slot.radius);
                    if (t >= 0f && t < best)
                    {
                        best = t;
                        target = slot;
                    }
                } while (grid.TryGetNextValue(out slot, ref it));
            }

            if (target.entity == Entity.Null)
                continue;

            ecb.AddComponent<DestroyedEntityTag_Dots>(entity);

            if (rng.NextFloat() >= mining.oreDropChance)
                continue;

            RefRW<AsteroidResource> ore = SystemAPI.GetComponentRW<AsteroidResource>(target.entity);
            if (ore.ValueRO.amount < 1f)
                continue;

            // Pop the ore out of the side that was hit, spread a little around the shot's direction.
            float2 hitPoint = math.lerp(start, end, best);
            float2 outward = math.normalizesafe(hitPoint - target.position, -direction);
            float spread = math.radians(rng.NextFloat(-mining.oreSpreadDegrees, mining.oreSpreadDegrees));
            float s = math.sin(spread), c = math.cos(spread);
            outward = new float2(outward.x * c - outward.y * s, outward.x * s + outward.y * c);

            float2 from = target.position + outward * target.radius;
            float2 rest = from + outward * rng.NextFloat(mining.oreScatterDistance.x, mining.oreScatterDistance.y);

            int typeIndex = (int)ore.ValueRO.type;
            Entity prefab = typeIndex < orePrefabs.Length ? orePrefabs[typeIndex].prefab : Entity.Null;
            if (prefab != Entity.Null)
            {
                Entity piece = ecb.Instantiate(prefab);
                ecb.SetComponent(piece, LocalTransform.FromPositionRotation(
                    new float3(from.x, 0f, from.y),
                    quaternion.EulerXYZ(rng.NextFloat3(0f, 2f * math.PI))));
                ecb.AddComponent(piece, new OreChunk
                {
                    type = ore.ValueRO.type,
                    amount = 1f,
                    state = OreState.PoppingOut,
                    from = new float3(from.x, 0f, from.y),
                    rest = new float3(rest.x, 0f, rest.y),
                    progress = 0f
                });
            }

            ore.ValueRW.amount -= 1f;
            if (ore.ValueRO.amount < 1f && depletedThisFrame.Add(target.entity))
                ecb.AddComponent<DestroyedEntityTag_Dots>(target.entity); // mined out: the asteroid breaks up
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
        grid.Dispose();
        depletedThisFrame.Dispose();
    }

    // Fraction along a->b where the segment first touches the circle; 0 if a starts inside; -1 for a miss.
    private static float SegmentCircle(float2 a, float2 b, float2 centre, float radius)
    {
        float2 d = b - a;
        float2 f = a - centre;
        float c = math.dot(f, f) - radius * radius;
        if (c <= 0f)
            return 0f;
        float aa = math.dot(d, d);
        if (aa < 1e-8f)
            return -1f;
        float bb = 2f * math.dot(f, d);
        float disc = bb * bb - 4f * aa * c;
        if (disc < 0f)
            return -1f;
        float t = (-bb - math.sqrt(disc)) / (2f * aa);
        return t >= 0f && t <= 1f ? t : -1f;
    }
}
