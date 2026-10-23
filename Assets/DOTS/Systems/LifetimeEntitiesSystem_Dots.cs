using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;


public struct LifetimeEntityTag_Dots : IComponentData
{
    public float deathSystemTime;
}


public partial struct LifetimeEntitiesSystem_Dots : ISystem
{
    private EntityManager entityManager;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        entityManager = state.EntityManager;
    }

    [WithAll(typeof(LifetimeEntityTag_Dots))]
    private partial struct AllTaggedEntitiesAge : IJobEntity
    {
        public EntityCommandBuffer ecb;

        public float currentTime;
        private void Execute(Entity entity, LifetimeEntityTag_Dots lifetime, [ChunkIndexInQuery] int index)
        {
            if(lifetime.deathSystemTime < currentTime)
            {
                ecb.AddComponent<DestroyedEntityTag_Dots>(entity);
                ecb.RemoveComponent<LifetimeEntityTag_Dots>(entity);
            }
        }
    }


    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer entityCommandBuffer = new EntityCommandBuffer(Allocator.TempJob);
        AllTaggedEntitiesAge taggedAge = new AllTaggedEntitiesAge
        {
            ecb = entityCommandBuffer,
            currentTime = Time.time
        };
        taggedAge.Schedule();
        state.Dependency.Complete();
        entityCommandBuffer.Playback(state.EntityManager);
        entityCommandBuffer.Dispose();
    }
}
