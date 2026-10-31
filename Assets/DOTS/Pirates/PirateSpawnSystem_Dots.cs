using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.InputSystem;

// Sends pirate raiding groups at the player: the first wave after firstWaveDelay, then one every
// waveInterval, each of groupSize ships from the Game Library's Pirate Ships, appearing
// spawnDistance away and heading in. F9 (testing) sends a wave right now.
// Later this follows the design's escalation: bigger, better-armed groups as the week goes on,
// aimed at whoever owns the most of the map.
public partial class PirateSpawnSystem_Dots : SystemBase
{
    private double nextWave = -1;
    private Random rng;

    protected override void OnCreate()
    {
        RequireForUpdate<PirateSpawnConfig>();
        RequireForUpdate<InputData_Dots>();
        rng = new Random(0x5EA5u ^ (uint)System.DateTime.Now.Ticks | 1u);
    }

    protected override void OnUpdate()
    {
        Entity libraryEntity = SystemAPI.GetSingletonEntity<PirateSpawnConfig>();
        PirateSpawnConfig cfg = SystemAPI.GetComponent<PirateSpawnConfig>(libraryEntity);
        double now = SystemAPI.Time.ElapsedTime;
        if (nextWave < 0)
            nextWave = now + cfg.firstWaveDelay;

        Keyboard keyboard = Keyboard.current;
        bool forced = keyboard != null && keyboard.f9Key.wasPressedThisFrame;
        if (!forced && (!cfg.enabled || now < nextWave))
            return;
        nextWave = now + cfg.waveInterval;

        EntityQuery alive = SystemAPI.QueryBuilder().WithAll<PirateAI>().Build();
        if (!forced && alive.CalculateEntityCount() >= cfg.maxAlive)
            return;

        DynamicBuffer<PiratePrefab> prefabs = SystemAPI.GetBuffer<PiratePrefab>(libraryEntity);
        if (prefabs.Length == 0)
        {
            UnityEngine.Debug.LogWarning("Pirates: add pirate ship prefabs to GameLibrary_Authoring's Pirate Ships list.");
            return;
        }
        var prefabList = prefabs.ToNativeArray(Allocator.Temp);

        EntityQuery playerQuery = SystemAPI.QueryBuilder().WithAll<InputData_Dots, LocalTransform>().Build();
        float3 playerPos = playerQuery.GetSingleton<LocalTransform>().Position;

        float angle = rng.NextFloat(0f, 2f * math.PI);
        float3 dir = new float3(math.sin(angle), 0f, math.cos(angle));
        float3 side = new float3(dir.z, 0f, -dir.x);
        float3 centre = playerPos + dir * cfg.spawnDistance;
        quaternion facing = quaternion.LookRotationSafe(-dir, math.up());

        int count = rng.NextInt(math.max(1, cfg.groupSize.x), math.max(cfg.groupSize.x, cfg.groupSize.y) + 1);
        for (int i = 0; i < count; i++)
        {
            Entity prefab = prefabList[rng.NextInt(prefabList.Length)].prefab;
            Entity pirate = EntityManager.Instantiate(prefab);
            LocalTransform t = EntityManager.GetComponentData<LocalTransform>(pirate);
            t.Position = centre + side * ((i - (count - 1) * 0.5f) * 20f) + dir * rng.NextFloat(-8f, 8f);
            t.Rotation = facing;
            EntityManager.SetComponentData(pirate, t);
            if (EntityManager.HasComponent<PirateAI>(pirate))
                EntityManager.SetComponentData(pirate, new PirateAI
                {
                    state = PirateState.Hunt,
                    orbitSign = rng.NextBool() ? 1f : -1f,
                    retargetTimer = rng.NextFloat(0f, 0.5f)
                });
        }
        prefabList.Dispose();
    }
}
