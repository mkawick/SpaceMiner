using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;


public struct DestroyedEntityTag_Dots : IComponentData
{
}


public partial struct DestroyEntitiesSystem_Dots : ISystem
{
    private EntityManager entityManager;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        entityManager = state.EntityManager;
    }

    [WithAll(typeof(DestroyedEntityTag_Dots))]
    private partial struct AllTaggedEntitiesDestroy : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ecb;

        private void Execute(Entity entity, [ChunkIndexInQuery] int index)
        {
            ecb.DestroyEntity(index, entity);
        }
    }


    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
       /* entityManager = state.EntityManager;
        foreach (var (tag, entity) in SystemAPI.Query<RefRO<DestroyedEntityTag_Dots>>().WithEntityAccess())
        {
            Debug.Log("<b> <color=#55FF55>state.EntityManager.DestroyEntity(entity) </color> </b>");
            state.EntityManager.DestroyEntity(entity);
            Debug.Log("<b> <color=#55FF55>DestroyEntity() finished </color> </b>");
        }*/

        EntityCommandBuffer entityCommandBuffer = new EntityCommandBuffer(Allocator.TempJob);
        AllTaggedEntitiesDestroy taggedDestroy = new AllTaggedEntitiesDestroy
        {
            ecb = entityCommandBuffer.AsParallelWriter()
        };
        taggedDestroy.Schedule();
        state.Dependency.Complete();
        entityCommandBuffer.Playback(state.EntityManager);
        entityCommandBuffer.Dispose();
    }
}
