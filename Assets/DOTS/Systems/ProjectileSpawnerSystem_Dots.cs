using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Ask for a shot: create an entity with this component. The weapon's data (WeaponDef) decides the
// projectile prefab, size, speed, range, damage and spread.
public struct ProjectileRequest : IComponentData
{
    public WeaponId weapon;
    public Entity owner;
    public CombatFaction faction;
    public float3 position;
    public quaternion dir;
    public float speedBonus; // the shooter's forward speed, so shots never fall behind a fast ship
}

// Turns ProjectileRequest entities into projectiles, using the prefabs listed on the Spawner.
[UpdateAfter(typeof(Input_to_SpaceShip_System_Dots))]
public partial struct ProjectileSpawnerSystem_Dots : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SpawnerComponent>();
        state.RequireForUpdate<ProjectileRequest>();
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity spawnerEntity = SystemAPI.GetSingletonEntity<SpawnerComponent>();
        DynamicBuffer<SpawnPrefabData> prefabs = SystemAPI.GetBuffer<SpawnPrefabData>(spawnerEntity);
        bool hasCatalog = SystemAPI.HasSingleton<WeaponCatalog>();
        DynamicBuffer<WeaponDef> catalog = hasCatalog
            ? SystemAPI.GetBuffer<WeaponDef>(SystemAPI.GetSingletonEntity<WeaponCatalog>())
            : default;
        float now = (float)SystemAPI.Time.ElapsedTime;

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (request, requestEntity) in
                 SystemAPI.Query<RefRO<ProjectileRequest>>().WithEntityAccess())
        {
            ProjectileRequest r = request.ValueRO;
            WeaponDef def = WeaponData.Find(r.weapon, hasCatalog, catalog);
            int prefabIndex = def.prefabIndex >= 0 && def.prefabIndex < prefabs.Length ? def.prefabIndex : 0;
            if (prefabs.Length > 0)
            {
                int count = math.max(1, def.projectilesPerShot);
                for (int i = 0; i < count; i++)
                {
                    float offset = count == 1 ? 0f : math.lerp(-0.5f, 0.5f, i / (float)(count - 1)) * def.spreadDegrees;
                    quaternion dir = math.mul(r.dir, quaternion.RotateY(math.radians(offset)));
                    float speed = def.speed + math.max(0f, r.speedBonus);

                    Entity projectile = ecb.Instantiate(prefabs[prefabIndex].Prefab);
                    // The template may be a child of another object in the subscene; detach the copy
                    // so its position is in world space.
                    ecb.RemoveComponent<Parent>(projectile);
                    ecb.SetComponent(projectile, new LocalTransform
                    {
                        Position = r.position,
                        Rotation = dir,
                        Scale = def.scale > 0f ? def.scale : 1f
                    });
                    ecb.AddComponent(projectile, new MoveEntityTag_Dots { speed = speed, dir = dir });
                    ecb.AddComponent(projectile, new Projectile
                    {
                        speed = speed,
                        hullDamage = def.hullDamage,
                        rockDamage = def.rockDamage,
                        owner = r.owner,
                        faction = r.faction,
                        layer = def.projectileLayer
                    });
                    ecb.AddComponent(projectile, new LifetimeEntityTag_Dots { deathSystemTime = now + math.max(0.1f, def.lifetime) });
                    ecb.SetEnabled(projectile, true);
                }
            }

            ecb.DestroyEntity(requestEntity);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
