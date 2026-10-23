using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor.PackageManager.Requests;
using UnityEngine;


public struct MoveEntityTag_Dots : IComponentData
{
    public float speed;
    public quaternion dir;
}


public partial struct MoveEntitiesSystem_Dots : ISystem
{
    private EntityManager entityManager;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        entityManager = state.EntityManager;
    }

    [WithAll(typeof(MoveEntityTag_Dots))]
    private partial struct AllTaggedEntitiesMove : IJobEntity
    {
        public EntityCommandBuffer ecb;

        public float elapsedTime;

        private void Execute(Entity entity, MoveEntityTag_Dots movement, ref LocalTransform transform)
        {
            // transform.LocalPosition localPosition
            float3 newPosition = transform.Position;
            newPosition = math.EulerZXY(movement.dir) *  movement.speed * elapsedTime + newPosition;
            transform.Position = newPosition;

            //Debug.Log($"<b> <size=13> <color=#9DF155>move speed{movement.speed}, dir {movement.dir}, math.euler {math.EulerZXY(movement.dir)} </color> </size> </b>");
        }
    }


    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer entityCommandBuffer = new EntityCommandBuffer(Allocator.TempJob);
        AllTaggedEntitiesMove taggedMove = new AllTaggedEntitiesMove
        {
            ecb = entityCommandBuffer,
            elapsedTime = Time.deltaTime
        };
        taggedMove.Schedule();
        state.Dependency.Complete();
        entityCommandBuffer.Playback(state.EntityManager);
        entityCommandBuffer.Dispose();
    }
}
