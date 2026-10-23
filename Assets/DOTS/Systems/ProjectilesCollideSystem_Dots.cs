using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Transforms;

partial struct ProjectilesCollideSystem_Dots : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var collisionWorld = physicsWorld.CollisionWorld;
        float currentTime = (float)SystemAPI.Time.ElapsedTime;
        
        CollisionFilter filter = new CollisionFilter
        {
            BelongsTo = (uint)GameConstants.allLayersMask,
            CollidesWith = (uint)GameConstants.asteroidsLayer,
            GroupIndex = 0
        };
        foreach (var (transform, projectile)
                 in SystemAPI.Query<RefRO<LocalTransform>, RefRW<Projectile>>())
        {
        }

    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        
    }
}
