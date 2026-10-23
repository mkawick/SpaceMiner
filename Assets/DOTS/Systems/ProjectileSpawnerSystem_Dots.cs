using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor.PackageManager.Requests;
using UnityEditor.Search;
using UnityEngine;
using static UnityEngine.Rendering.STP;

public struct ProjectileRequest : IComponentData
{
    public int projectileEnum;
    public int ownerId;
    public float damageIfHits;
    public quaternion dir;
    public float3 position;
    public float speed;
}


[UpdateAfter(typeof(Input_to_SpaceShip_System_Dots))]
public partial struct ProjectileSpawnerSystem_Dots : ISystem
{
    EntityQuery projectileRequestQuery;
    RefRO<SpawnerComponent> spawner;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        if (SystemAPI.TryGetSingletonEntity<SpawnerComponent>(out Entity spawnerEntity))
        {
            spawner = SystemAPI.GetComponentRO<SpawnerComponent>(spawnerEntity);
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        //var config = SystemAPI.GetSingleton<Config>();
        if (SystemAPI.TryGetSingletonEntity<SpawnerComponent>(out Entity spawnerEntity))
        {
            RefRO<SpawnerComponent> spawner = SystemAPI.GetComponentRO<SpawnerComponent>(spawnerEntity);
            DynamicBuffer<SpawnPrefabData> prefabsToSpawn = state.EntityManager.GetBuffer<SpawnPrefabData>(spawnerEntity);

            //   if (spawner.ValueRO.nextSpawnTime < SystemAPI.Time.ElapsedTime)
            {
                Spawn(ref state, spawner, prefabsToSpawn);
            }
        }

    }


    //[BurstCompile]
    void Spawn(ref SystemState state, RefRO<SpawnerComponent> spawner, DynamicBuffer<SpawnPrefabData> prefabsToSpawn)
    {
        var ecb = new EntityCommandBuffer(Allocator.TempJob);
        EntityCommandBuffer.ParallelWriter parallelEcb = ecb.AsParallelWriter();

        foreach (var ( request, requestEntity) in SystemAPI.Query<RefRO<ProjectileRequest>>().WithEntityAccess())
        {
            Debug.Log("<b> <size=13> <color=#9DF155>Request processing </color> </size> </b>");

            int prefabIndex = request.ValueRO.projectileEnum;
            Debug.Log($"<b> <size=13> <color=#999999>Request {request}</color> </size> </b>");// ©
            Debug.Log($"<b> <size=13> <color=#999999>Request {prefabsToSpawn.Length}</color> </size> </b>");
            

            if (prefabIndex < prefabsToSpawn.Length)
            {
                Debug.Log($"<b> <size=13> <color=#9DF155>Request found index {prefabIndex}</color> </size> </b>");
                var projectilePrefab = prefabsToSpawn[prefabIndex];
                var projectile = state.EntityManager.Instantiate(projectilePrefab.Prefab);

                Debug.Log("<b> <size=13> <color=#9DF155>Request Instantiated </color> </size> </b>");
                state.EntityManager.SetComponentData(projectile,
                                         LocalTransform.FromPositionRotation(request.ValueRO.position, request.ValueRO.dir));

                ecb.AddComponent(projectile, new MoveEntityTag_Dots() 
                { 
                    speed = request.ValueRO.speed, 
                    dir = request.ValueRO.dir 
                });

                Debug.Log($"<b> <size=13> <color=#9DF155>projectile pos {request.ValueRO.position} </color> </size> </b>");
                Debug.Log($"<b> <size=13> <color=#9DF155>projectile speed {request.ValueRO.speed} , dir {request.ValueRO.dir} </color> </size> </b>");

                ecb.AddComponent(projectile, new LifetimeEntityTag_Dots() 
                { 
                    deathSystemTime = (float) SystemAPI.Time.ElapsedTime + 8.2f // lifetime should be part of weapon
                });
                ecb.SetEnabled(projectile, true);
            }
            // set the request to be deleted
            Debug.Log("<b> <size=13> <color=#9DF155>Request destroy component added </color> </size> </b>");
            ecb.AddComponent(requestEntity, new DestroyedEntityTag_Dots());
        }


        state.Dependency.Complete();
        ecb.Playback(state.EntityManager);

        ecb.Dispose();


    }
}
