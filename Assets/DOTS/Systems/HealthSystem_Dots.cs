using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Handles anything whose health has run out. Ships, bots, buildings and asteroids are destroyed;
// the player's ship is put back at its respawn point with full health and its cargo kept.
// Also gives the player's ship its faction once the field has picked one.
[BurstCompile]
[UpdateAfter(typeof(ProjectilesCollideSystem_Dots))]
public partial struct HealthSystem_Dots : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        bool startPlaced = false;
        CombatFaction playerFaction = CombatFaction.None;
        if (SystemAPI.HasSingleton<AsteroidFieldConfig>())
        {
            Entity configEntity = SystemAPI.GetSingletonEntity<AsteroidFieldConfig>();
            playerFaction = SystemAPI.GetComponent<AsteroidFieldConfig>(configEntity).playerFaction;
            startPlaced = SystemAPI.HasComponent<PlayerStartPlaced>(configEntity);
        }

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // The player's ship.
        foreach (var (health, targetable, transform, entity) in
                 SystemAPI.Query<RefRW<Health>, RefRW<Targetable>, RefRW<LocalTransform>>()
                          .WithAll<InputData_Dots>()
                          .WithEntityAccess())
        {
            if (targetable.ValueRO.faction == CombatFaction.None && playerFaction != CombatFaction.None)
                targetable.ValueRW.faction = playerFaction;

            bool hasRespawn = SystemAPI.HasComponent<RespawnPoint>(entity);
            if (!hasRespawn && startPlaced)
                ecb.AddComponent(entity, new RespawnPoint { position = transform.ValueRO.Position });

            if (health.ValueRO.current > 0f)
                continue;

            if (hasRespawn)
                transform.ValueRW.Position = SystemAPI.GetComponent<RespawnPoint>(entity).position;
            health.ValueRW.current = health.ValueRO.max;
            if (SystemAPI.HasComponent<SpaceShipStatus_Dots>(entity))
            {
                RefRW<SpaceShipStatus_Dots> status = SystemAPI.GetComponentRW<SpaceShipStatus_Dots>(entity);
                status.ValueRW.speed = 0f;
                status.ValueRW.strafeSpeed = 0f;
            }
        }

        // Everything else.
        foreach (var (health, entity) in
                 SystemAPI.Query<RefRO<Health>>()
                          .WithNone<InputData_Dots, DestroyedEntityTag_Dots>()
                          .WithEntityAccess())
        {
            if (health.ValueRO.current <= 0f)
                ecb.AddComponent<DestroyedEntityTag_Dots>(entity);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
