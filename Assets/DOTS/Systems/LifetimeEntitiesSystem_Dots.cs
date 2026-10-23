using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

public struct LifetimeEntityTag_Dots : IComponentData
{
    public float deathSystemTime; // in SystemAPI.Time.ElapsedTime seconds
}

// Marks entities for destruction once their lifetime runs out.
[BurstCompile]
public partial struct LifetimeEntitiesSystem_Dots : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float now = (float)SystemAPI.Time.ElapsedTime;
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (lifetime, entity) in
                 SystemAPI.Query<RefRO<LifetimeEntityTag_Dots>>().WithEntityAccess())
        {
            if (lifetime.ValueRO.deathSystemTime < now)
            {
                ecb.AddComponent<DestroyedEntityTag_Dots>(entity);
                ecb.RemoveComponent<LifetimeEntityTag_Dots>(entity);
            }
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
