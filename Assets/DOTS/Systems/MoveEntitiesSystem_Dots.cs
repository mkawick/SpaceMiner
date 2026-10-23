using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public struct MoveEntityTag_Dots : IComponentData
{
    public float speed;
    public quaternion dir;
}

// Moves projectiles (and anything else tagged) straight along their rotation's forward axis.
[BurstCompile]
public partial struct MoveEntitiesSystem_Dots : ISystem
{
    [BurstCompile]
    private partial struct MoveJob : IJobEntity
    {
        public float deltaTime;

        private void Execute(in MoveEntityTag_Dots movement, ref LocalTransform transform)
        {
            float3 direction = math.mul(movement.dir, new float3(0f, 0f, 1f));
            transform.Position += direction * movement.speed * deltaTime;
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new MoveJob { deltaTime = SystemAPI.Time.DeltaTime }.ScheduleParallel();
    }
}
