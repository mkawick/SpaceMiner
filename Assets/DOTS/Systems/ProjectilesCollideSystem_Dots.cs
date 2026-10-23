using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

// Ray-casts each projectile along the distance it moved this frame. A hit destroys the projectile;
// hitting an asteroid takes away hit points, and an asteroid at 0 is destroyed.
[BurstCompile]
[UpdateAfter(typeof(MoveEntitiesSystem_Dots))]
public partial struct ProjectilesCollideSystem_Dots : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();

        CollisionWorld collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        float dt = SystemAPI.Time.DeltaTime;
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        var hits = new NativeList<RaycastHit>(Allocator.Temp);

        foreach (var (transform, move, projectile, entity) in
                 SystemAPI.Query<RefRO<LocalTransform>, RefRO<MoveEntityTag_Dots>, RefRO<Projectile>>()
                          .WithNone<DestroyedEntityTag_Dots>()
                          .WithEntityAccess())
        {
            float3 end = transform.ValueRO.Position;
            float3 direction = math.mul(move.ValueRO.dir, new float3(0f, 0f, 1f));
            float3 start = end - direction * move.ValueRO.speed * dt;

            var ray = new RaycastInput { Start = start, End = end, Filter = CollisionFilter.Default };
            hits.Clear();
            if (!collisionWorld.CastRay(ray, ref hits))
                continue;

            // Closest hit that isn't this shot, another shot, or the player's ship.
            Entity target = Entity.Null;
            float closest = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                Entity e = hits[i].Entity;
                if (e == entity ||
                    SystemAPI.HasComponent<MoveEntityTag_Dots>(e) ||
                    SystemAPI.HasComponent<InputData_Dots>(e))
                    continue;
                if (hits[i].Fraction < closest)
                {
                    closest = hits[i].Fraction;
                    target = e;
                }
            }
            if (target == Entity.Null)
                continue;

            ecb.AddComponent<DestroyedEntityTag_Dots>(entity);

            if (SystemAPI.HasComponent<AsteroidComponent_Dots>(target))
            {
                RefRW<AsteroidComponent_Dots> asteroid = SystemAPI.GetComponentRW<AsteroidComponent_Dots>(target);
                asteroid.ValueRW.hitPoints -= projectile.ValueRO.damage;
                if (asteroid.ValueRO.hitPoints <= 0)
                    ecb.AddComponent<DestroyedEntityTag_Dots>(target);
            }
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
        hits.Dispose();
    }
}
