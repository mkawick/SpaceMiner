using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public struct ProjectileRequest : IComponentData
{
    public int projectileEnum;
    public int ownerId;
    public float damageIfHits;
    public quaternion dir;
    public float3 position;
    public float speed;
}

// Turns ProjectileRequest entities into projectiles, using the prefabs listed on the Spawner.
[UpdateAfter(typeof(Input_to_SpaceShip_System_Dots))]
public partial struct ProjectileSpawnerSystem_Dots : ISystem
{
    private const float ProjectileLifetime = 6f; // seconds; will move to weapon data

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SpawnerComponent>();
        state.RequireForUpdate<ProjectileRequest>();
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity spawnerEntity = SystemAPI.GetSingletonEntity<SpawnerComponent>();
        DynamicBuffer<SpawnPrefabData> prefabs = SystemAPI.GetBuffer<SpawnPrefabData>(spawnerEntity);
        float now = (float)SystemAPI.Time.ElapsedTime;

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (request, requestEntity) in
                 SystemAPI.Query<RefRO<ProjectileRequest>>().WithEntityAccess())
        {
            ProjectileRequest r = request.ValueRO;
            if (r.projectileEnum >= 0 && r.projectileEnum < prefabs.Length)
            {
                Entity projectile = ecb.Instantiate(prefabs[r.projectileEnum].Prefab);

                // The template may be a child of another object in the subscene; detach the copy
                // so its position is in world space.
                ecb.RemoveComponent<Parent>(projectile);
                ecb.SetComponent(projectile, LocalTransform.FromPositionRotation(r.position, r.dir));
                ecb.AddComponent(projectile, new MoveEntityTag_Dots { speed = r.speed, dir = r.dir });
                ecb.AddComponent(projectile, new Projectile
                {
                    speed = r.speed,
                    damage = (int)math.max(1f, r.damageIfHits),
                    owner = Entity.Null
                });
                ecb.AddComponent(projectile, new LifetimeEntityTag_Dots { deathSystemTime = now + ProjectileLifetime });
                ecb.SetEnabled(projectile, true);
            }

            ecb.DestroyEntity(requestEntity);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
