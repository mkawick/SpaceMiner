using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Slowly tumbles asteroids. rotation is in degrees per second on each axis.
[BurstCompile]
public partial struct AsteroidDriftingSystem_Dots : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        foreach (var (drift, transform) in
                 SystemAPI.Query<RefRO<AsteroidComponent_Dots>, RefRW<LocalTransform>>())
        {
            float3 step = math.radians(drift.ValueRO.rotation) * dt;
            transform.ValueRW.Rotation = math.mul(transform.ValueRO.Rotation, quaternion.EulerZXY(step));
        }
    }
}
