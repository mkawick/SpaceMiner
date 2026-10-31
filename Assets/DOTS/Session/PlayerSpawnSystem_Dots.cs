using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Puts the player in the game once the asteroid field exists.
//   JoinRequest: spawn the requested ship type from the Game Library at the player's start hex
//   and give it player control (input, target, respawn point). In multiplayer this runs on the
//   server and the new ship gets the requesting connection as its owner.
//   A ship placed directly in a subscene with player control (the old way) is simply moved to
//   the start hex instead, so scenes made before the spawner keep working.
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(AsteroidFieldSpawnSystem_Dots))]
public partial struct PlayerSpawnSystem_Dots : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AsteroidFieldReady>();
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityManager em = state.EntityManager;
        Entity configEntity = SystemAPI.GetSingletonEntity<AsteroidFieldReady>();
        AsteroidFieldConfig cfg = em.GetComponentData<AsteroidFieldConfig>(configEntity);
        int2 hex = em.GetComponentData<AsteroidFieldReady>(configEntity).playerHex;
        float2 centre = HexGrid.HexToWorld(hex, cfg.hexSize);
        float3 start = new float3(centre.x, 0f, centre.y);
        quaternion facing = quaternion.LookRotationSafe(new float3(-centre.x, 0f, -centre.y), math.up()); // toward the map centre

        EntityQuery players = SystemAPI.QueryBuilder().WithAll<InputData_Dots, LocalTransform>().Build();

        // Old way: a player ship placed in the subscene. Move it to the start once.
        if (!em.HasComponent<PlayerStartPlaced>(configEntity) && !players.IsEmpty)
        {
            NativeArray<Entity> existing = players.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < existing.Length; i++)
                PlaceAt(em, existing[i], start, facing);
            existing.Dispose();
            em.AddComponent<PlayerStartPlaced>(configEntity);
        }

        EntityQuery requests = SystemAPI.QueryBuilder().WithAll<JoinRequest>().Build();
        if (requests.IsEmpty)
            return;

        NativeArray<Entity> requestEntities = requests.ToEntityArray(Allocator.Temp);
        NativeArray<JoinRequest> requestData = requests.ToComponentDataArray<JoinRequest>(Allocator.Temp);
        for (int r = 0; r < requestEntities.Length; r++)
        {
            em.DestroyEntity(requestEntities[r]);

            if (!players.IsEmpty)
                continue; // already flying a ship (single player: one at a time)

            Entity prefab = FindShip(ref state, requestData[r].shipType);
            if (prefab == Entity.Null)
            {
                UnityEngine.Debug.LogWarning("Join: no ship prefab in the Game Library's Ship Types list " +
                                             $"for type {requestData[r].shipType}; add one to GameLibrary_Authoring.");
                continue;
            }

            Entity ship = em.Instantiate(prefab);
            PlaceAt(em, ship, start, facing);
            GivePlayerControl(em, ship, start);
            em.AddComponent<PlayerStartPlaced>(configEntity);
        }
        requestEntities.Dispose();
        requestData.Dispose();
    }

    private Entity FindShip(ref SystemState state, int type)
    {
        if (!SystemAPI.HasSingleton<GameLibrary>())
            return Entity.Null;
        DynamicBuffer<ShipPrefab> ships = SystemAPI.GetBuffer<ShipPrefab>(SystemAPI.GetSingletonEntity<GameLibrary>());
        if (ships.Length == 0)
            return Entity.Null;
        return ships[math.clamp(type, 0, ships.Length - 1)].prefab;
    }

    private static void PlaceAt(EntityManager em, Entity ship, float3 position, quaternion rotation)
    {
        LocalTransform t = em.GetComponentData<LocalTransform>(ship);
        t.Position = position;
        t.Rotation = rotation;
        em.SetComponentData(ship, t);
        if (em.HasComponent<SpaceShipStatus_Dots>(ship))
        {
            SpaceShipStatus_Dots status = em.GetComponentData<SpaceShipStatus_Dots>(ship);
            status.speed = 0f;
            status.strafeSpeed = 0f;
            em.SetComponentData(ship, status);
        }
    }

    // What makes a ship "the player's": input, a target slot and a respawn point. Its faction is
    // filled in by HealthSystem_Dots from the field config.
    private static void GivePlayerControl(EntityManager em, Entity ship, float3 start)
    {
        if (!em.HasComponent<InputData_Dots>(ship)) em.AddComponentData(ship, new InputData_Dots());
        if (!em.HasComponent<PlayerTarget>(ship)) em.AddComponentData(ship, new PlayerTarget());
        em.AddComponentData(ship, new RespawnPoint { position = start });
        em.AddComponentData(ship, new PlayerShip { playerId = 0 });
    }
}
