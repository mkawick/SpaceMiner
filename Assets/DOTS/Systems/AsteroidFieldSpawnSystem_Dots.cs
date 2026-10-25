using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Random = Unity.Mathematics.Random;

// Startup sequence: builds the whole asteroid field from the seed once, then moves the player's
// ship to its start hex. Every hex is generated from hash(seed, q, r) alone, so the result does
// not depend on generation order and any machine with the same seed gets the same field.
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct AsteroidFieldSpawnSystem_Dots : ISystem
{
    private struct Spawn
    {
        public float2 position;
        public quaternion rotation;
        public float radius;
        public float3 spin;
        public ResourceType type;
        public float amount;
        public int hitPoints;
        public int2 hex;
        public int index;
        public int prefab;
    }

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AsteroidFieldConfig>();
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity configEntity = SystemAPI.GetSingletonEntity<AsteroidFieldConfig>();
        AsteroidFieldConfig cfg = SystemAPI.GetComponent<AsteroidFieldConfig>(configEntity);
        EntityManager em = state.EntityManager;

        if (!em.HasComponent<AsteroidFieldReady>(configEntity))
            Generate(em, configEntity, cfg);

        if (!em.HasComponent<PlayerStartPlaced>(configEntity))
            TryPlacePlayer(ref state, em, configEntity, cfg);

        if (em.HasComponent<AsteroidFieldReady>(configEntity) && em.HasComponent<PlayerStartPlaced>(configEntity))
            state.Enabled = false;
    }

    private static void Generate(EntityManager em, Entity configEntity, AsteroidFieldConfig cfg)
    {
        NativeArray<AsteroidVisualPrefab> prefabs =
            em.GetBuffer<AsteroidVisualPrefab>(configEntity).ToNativeArray(Allocator.Temp);
        if (prefabs.Length == 0)
        {
            UnityEngine.Debug.LogError("Asteroid field: no asteroid prefabs assigned on AsteroidField_Authoring.");
            em.AddComponentData(configEntity, new AsteroidFieldReady());
            return;
        }

        for (int i = 0; i < prefabs.Length; i++)
            PreparePrefab(em, prefabs[i].prefab);

        // Every asteroid holds the same number of ore pieces (see MiningConfig).
        int orePerAsteroid = em.HasComponent<MiningConfig>(configEntity)
            ? em.GetComponentData<MiningConfig>(configEntity).orePerAsteroid
            : 100;

        var spawns = new NativeList<Spawn>(16384, Allocator.Temp);
        var playerStarts = new NativeList<int2>(512, Allocator.Temp);
        int hexCount = 0, starterHexCount = 0;

        // Every hex whose centre lies inside the map circle.
        int n = (int)math.ceil(cfg.mapRadius / (1.5f * cfg.hexSize)) + 1;
        for (int q = -n; q <= n; q++)
        {
            for (int r = -n; r <= n; r++)
            {
                int2 hex = new int2(q, r);
                if (!AsteroidFieldRules.IsInsideMap(cfg, hex))
                    continue;

                hexCount++;
                HexKind kind = AsteroidFieldRules.Classify(cfg, hex, out CombatFaction faction);
                if (kind == HexKind.Starter)
                {
                    starterHexCount++;
                    if (faction == cfg.playerFaction)
                        playerStarts.Add(hex);
                }
                GenerateHex(cfg, hex, kind, prefabs, ref spawns);
            }
        }

        Instantiate(em, prefabs, spawns, orePerAsteroid);

        int2 playerHex = int2.zero;
        if (playerStarts.Length > 0)
        {
            uint pick = HexGrid.Hash(cfg.seed, new int2(cfg.playerStartSlot, 7919));
            playerHex = playerStarts[(int)(pick % (uint)playerStarts.Length)];
        }

        em.AddComponentData(configEntity, new AsteroidFieldReady
        {
            hexCount = hexCount,
            asteroidCount = spawns.Length,
            starterHexCount = starterHexCount,
            playerHex = playerHex
        });

        UnityEngine.Debug.Log($"Asteroid field: seed {cfg.seed}, {hexCount} hexes ({starterHexCount} starter), " +
                              $"{spawns.Length} asteroids, player starts in hex ({playerHex.x}, {playerHex.y}).");
    }

    private static void GenerateHex(in AsteroidFieldConfig cfg, int2 hex, HexKind kind,
                                    NativeArray<AsteroidVisualPrefab> prefabs, ref NativeList<Spawn> spawns)
    {
        var rng = new Random(HexGrid.Hash(cfg.seed, hex));
        float2 centre = HexGrid.HexToWorld(hex, cfg.hexSize);
        float d = math.saturate(math.length(centre) / cfg.mapRadius); // 0 centre .. 1 edge
        int colour = HexGrid.Colour5(hex);

        int count;
        float radiusScale = 1f;
        float yieldScale = 1f;
        float keepClear = 0f; // free space at the centre of start hexes for the first base
        switch (kind)
        {
            case HexKind.Newbie:
                count = 2;
                radiusScale = 0.6f;
                yieldScale = 0.5f;
                break;
            case HexKind.Starter:
                count = cfg.starterAsteroidsPerHex;
                keepClear = 45f;
                break;
            default:
                count = (int)math.round(math.lerp(cfg.wildAsteroidsPerHex.x, cfg.wildAsteroidsPerHex.y, 1f - d))
                        + rng.NextInt(-1, 2);
                if (d < 0.34f) radiusScale = 1.3f; // the deep field has bigger rocks
                break;
        }
        count = math.max(0, count);

        // Asteroids stay inside the hex's inner circle so they never cross into a neighbour.
        float innerRadius = cfg.hexSize * HexGrid.Sqrt3 * 0.5f * 0.85f;
        int firstIndex = spawns.Length;

        for (int i = 0; i < count; i++)
        {
            float radius = rng.NextFloat(cfg.radiusRange.x, cfg.radiusRange.y) * radiusScale;

            // Try a few random spots; skip the asteroid if none is free.
            bool placed = false;
            float2 offset = float2.zero;
            for (int attempt = 0; attempt < 12 && !placed; attempt++)
            {
                float angle = rng.NextFloat(0f, 2f * math.PI);
                float dist = math.sqrt(rng.NextFloat()) * (innerRadius - radius);
                offset = new float2(math.cos(angle), math.sin(angle)) * dist;
                if (math.length(offset) < keepClear + radius)
                    continue;

                placed = true;
                for (int j = firstIndex; j < spawns.Length; j++)
                {
                    float2 other = spawns[j].position - centre;
                    if (math.distance(other, offset) < spawns[j].radius + radius + 4f)
                    {
                        placed = false;
                        break;
                    }
                }
            }
            if (!placed)
                continue;

            ResourceType type = kind == HexKind.Wild
                ? PickWildType(ref rng, d)
                : AsteroidFieldRules.StarterType(colour, i);

            float2 position = math.round((centre + offset) * 100f) / 100f; // 0.01-unit grid: identical everywhere
            spawns.Add(new Spawn
            {
                position = position,
                rotation = quaternion.EulerXYZ(rng.NextFloat3(0f, 2f * math.PI)),
                radius = radius,
                spin = rng.NextFloat3Direction() * rng.NextFloat(cfg.spinRange.x, cfg.spinRange.y),
                type = type,
                amount = math.round(radius * AsteroidFieldRules.Yield(type) * yieldScale),
                hitPoints = 1 + (int)(radius / 4f),
                hex = hex,
                index = i,
                prefab = rng.NextInt(prefabs.Length)
            });
        }
    }

    // Common types everywhere; gold toward the centre; fermite only in the deep field.
    private static ResourceType PickWildType(ref Random rng, float d)
    {
        float iron = 3f, copper = 2f, silicate = 3f, ice = 1f + 2f * d, carbon = 2f;
        float gold = 2f * (1f - d);
        float fermite = d < 0.34f ? 2f * (1f - d / 0.34f) : 0f;

        float roll = rng.NextFloat(iron + copper + silicate + ice + carbon + gold + fermite);
        if ((roll -= iron) < 0f) return ResourceType.Iron;
        if ((roll -= copper) < 0f) return ResourceType.Copper;
        if ((roll -= silicate) < 0f) return ResourceType.Silicate;
        if ((roll -= ice) < 0f) return ResourceType.Ice;
        if ((roll -= carbon) < 0f) return ResourceType.Carbon;
        if ((roll -= gold) < 0f) return ResourceType.Gold;
        return ResourceType.Fermite;
    }

    private static void Instantiate(EntityManager em, NativeArray<AsteroidVisualPrefab> prefabs, NativeList<Spawn> spawns, int orePerAsteroid)
    {
        var indices = new NativeList<int>(spawns.Length, Allocator.Temp);
        for (int p = 0; p < prefabs.Length; p++)
        {
            indices.Clear();
            for (int i = 0; i < spawns.Length; i++)
                if (spawns[i].prefab == p)
                    indices.Add(i);
            if (indices.Length == 0)
                continue;

            NativeArray<Entity> entities = em.Instantiate(prefabs[p].prefab, indices.Length, Allocator.Temp);
            for (int k = 0; k < entities.Length; k++)
            {
                Spawn s = spawns[indices[k]];
                Entity e = entities[k];
                em.SetComponentData(e, new LocalTransform
                {
                    Position = new float3(s.position.x, 0f, s.position.y),
                    Rotation = s.rotation,
                    Scale = s.radius / prefabs[p].meshRadius
                });
                em.SetComponentData(e, new AsteroidComponent_Dots
                {
                    rotation = s.spin,
                    rotationSpeed = math.length(s.spin),
                    hitPoints = s.hitPoints
                });
                em.SetComponentData(e, new AsteroidResource { type = s.type, amount = orePerAsteroid });
                em.SetComponentData(e, new AsteroidHex { hex = s.hex, index = s.index, radius = s.radius });
            }
        }
    }

    // The asteroid prefabs carry MeshColliders. Thousands of spinning mesh colliders would be
    // expensive, and hits are tested against the hex grid instead, so strip physics from the
    // prefab entities once; every instance then comes without it.
    private static void PreparePrefab(EntityManager em, Entity prefab)
    {
        if (em.HasBuffer<LinkedEntityGroup>(prefab))
        {
            NativeArray<LinkedEntityGroup> group = em.GetBuffer<LinkedEntityGroup>(prefab).ToNativeArray(Allocator.Temp);
            for (int i = 0; i < group.Length; i++)
                StripPhysics(em, group[i].Value);
        }
        else
        {
            StripPhysics(em, prefab);
        }

        em.AddComponent<AsteroidComponent_Dots>(prefab);
        em.AddComponent<AsteroidResource>(prefab);
        em.AddComponent<AsteroidHex>(prefab);
    }

    private static void StripPhysics(EntityManager em, Entity e)
    {
        em.RemoveComponent<PhysicsCollider>(e);
        em.RemoveComponent<PhysicsWorldIndex>(e);
        em.RemoveComponent<PhysicsVelocity>(e);
        em.RemoveComponent<PhysicsMass>(e);
        em.RemoveComponent<PhysicsDamping>(e);
        em.RemoveComponent<PhysicsGravityFactor>(e);
        em.RemoveComponent<PhysicsColliderKeyEntityPair>(e);
    }

    private void TryPlacePlayer(ref SystemState state, EntityManager em, Entity configEntity, AsteroidFieldConfig cfg)
    {
        if (!em.HasComponent<AsteroidFieldReady>(configEntity))
            return;

        EntityQuery playerQuery = SystemAPI.QueryBuilder()
            .WithAll<InputData_Dots, LocalTransform, SpaceShipStatus_Dots>()
            .Build();
        if (playerQuery.IsEmpty)
            return; // the ship hasn't loaded yet; try again next frame

        int2 hex = em.GetComponentData<AsteroidFieldReady>(configEntity).playerHex;
        float2 centre = HexGrid.HexToWorld(hex, cfg.hexSize);
        float3 toMapCentre = new float3(-centre.x, 0f, -centre.y);

        NativeArray<Entity> players = playerQuery.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < players.Length; i++)
        {
            LocalTransform t = em.GetComponentData<LocalTransform>(players[i]);
            t.Position = new float3(centre.x, 0f, centre.y);
            t.Rotation = quaternion.LookRotationSafe(toMapCentre, math.up());
            em.SetComponentData(players[i], t);

            SpaceShipStatus_Dots status = em.GetComponentData<SpaceShipStatus_Dots>(players[i]);
            status.speed = 0f;
            em.SetComponentData(players[i], status);
        }

        em.AddComponent<PlayerStartPlaced>(configEntity);
    }
}
