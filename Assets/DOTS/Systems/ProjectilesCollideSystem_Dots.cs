using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Random = Unity.Mathematics.Random;

// Hit tests on the play plane using the hex grid instead of physics colliders.
// Every Targetable (ships, bots, buildings, asteroids) is bucketed by hex; each projectile checks
// its hex and the six around it along the segment it travelled this frame. A projectile hits a
// target only if the Layer Matrix says their layers collide, and it passes through its own
// shooter and anything of the shooter's faction, so friendly fire can't happen.
// Damage = lerp(hullDamage, rockDamage, target density): mining weapons crack rock, combat
// weapons hurt ships. On an asteroid, each hit may knock out a piece of ore (more likely the
// harder it hits), and the asteroid breaks up when its ore or its health runs out.
// HealthSystem_Dots handles anything whose health reaches 0.
[BurstCompile]
[UpdateAfter(typeof(MoveEntitiesSystem_Dots))]
public partial struct ProjectilesCollideSystem_Dots : ISystem
{
    private struct TargetSlot
    {
        public Entity entity;
        public float2 position;
        public float radius;
        public float density;
        public CollisionLayer layer;
        public CombatFaction faction;
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
        LayerMatrix matrix = SystemAPI.HasSingleton<LayerMatrix>() ? SystemAPI.GetSingleton<LayerMatrix>() : LayerMatrix.Default();

        // Bucket every target by hex.
        EntityQuery targetQuery = SystemAPI.QueryBuilder()
            .WithAll<Targetable, LocalTransform>()
            .WithNone<DestroyedEntityTag_Dots>()
            .Build();
        int targetCount = targetQuery.CalculateEntityCount();
        var grid = new NativeParallelMultiHashMap<int2, TargetSlot>(math.max(1, targetCount), Allocator.Temp);
        foreach (var (targetable, transform, targetEntity) in
                 SystemAPI.Query<RefRO<Targetable>, RefRO<LocalTransform>>()
                          .WithNone<DestroyedEntityTag_Dots>()
                          .WithEntityAccess())
        {
            float2 p = transform.ValueRO.Position.xz;
            grid.Add(HexGrid.WorldToHex(p, hexSize), new TargetSlot
            {
                entity = targetEntity,
                position = p,
                radius = targetable.ValueRO.radius,
                density = targetable.ValueRO.density,
                layer = targetable.ValueRO.layer,
                faction = targetable.ValueRO.faction
            });
        }

        var ecb = new EntityCommandBuffer(Allocator.Temp);
        var deadThisFrame = new NativeHashSet<Entity>(16, Allocator.Temp);

        foreach (var (transform, move, projectile, entity) in
                 SystemAPI.Query<RefRO<LocalTransform>, RefRO<MoveEntityTag_Dots>, RefRO<Projectile>>()
                          .WithNone<DestroyedEntityTag_Dots>()
                          .WithEntityAccess())
        {
            Projectile shot = projectile.ValueRO;
            float2 end = transform.ValueRO.Position.xz;
            float2 direction = math.mul(move.ValueRO.dir, new float3(0f, 0f, 1f)).xz;
            float2 start = end - direction * move.ValueRO.speed * dt;
            int2 hex = HexGrid.WorldToHex(end, hexSize);

            TargetSlot target = default;
            float best = float.MaxValue;
            for (int n = 0; n < 7; n++)
            {
                int2 h = n == 6 ? hex : hex + HexGrid.EdgeNeighbour(n);
                if (!grid.TryGetFirstValue(h, out TargetSlot slot, out NativeParallelMultiHashMapIterator<int2> it))
                    continue;
                do
                {
                    if (slot.entity == shot.owner
                        || !matrix.Collides(shot.layer, slot.layer)
                        || Combat.IsFriendly(slot.faction, shot.faction)
                        || deadThisFrame.Contains(slot.entity))
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

            float damage = Combat.Damage(shot.hullDamage, shot.rockDamage, target.density);
            bool dead = false;
            if (SystemAPI.HasComponent<Health>(target.entity))
            {
                RefRW<Health> health = SystemAPI.GetComponentRW<Health>(target.entity);
                health.ValueRW.current -= damage;
                dead = health.ValueRO.current <= 0f;
            }

            if (SystemAPI.HasComponent<AsteroidResource>(target.entity))
                dead |= KnockOutOre(ref state, ref ecb, mining, orePrefabs, target, damage,
                                    math.lerp(start, end, best), direction);

            if (dead)
            {
                deadThisFrame.Add(target.entity);
                if (SystemAPI.HasComponent<Health>(target.entity))
                    SystemAPI.GetComponentRW<Health>(target.entity).ValueRW.current = 0f;
                else
                    ecb.AddComponent<DestroyedEntityTag_Dots>(target.entity);
            }
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
        grid.Dispose();
        deadThisFrame.Dispose();
    }

    // Maybe pop a piece of ore out of the side that was hit. Returns true when the asteroid is
    // mined out. Weak hits (combat weapons on rock) rarely knock anything loose.
    private bool KnockOutOre(ref SystemState state, ref EntityCommandBuffer ecb, in MiningConfig mining,
                             in DynamicBuffer<OrePrefab> orePrefabs, in TargetSlot target, float damage,
                             float2 hitPoint, float2 direction)
    {
        RefRW<AsteroidResource> ore = SystemAPI.GetComponentRW<AsteroidResource>(target.entity);
        if (ore.ValueRO.amount < 1f)
            return true;
        if (rng.NextFloat() >= mining.oreDropChance * math.saturate(damage))
            return false;

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
        return ore.ValueRO.amount < 1f; // mined out: the asteroid breaks up
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
