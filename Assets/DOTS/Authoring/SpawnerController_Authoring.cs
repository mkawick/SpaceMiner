using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

//--------------------------------------------------------------

[InternalBufferCapacity(16)]
public struct SpawnPrefabData : IBufferElementData
{
    public Entity Prefab;
    public float Foo; // couldn't read the name, but seemed to be related to the Prefab array
}

[ChunkSerializable]
public struct SpawnerComponent : IComponentData
{
    
    //public DynamicBuffer<SpawnPrefabData> prefabs;
    public float3 spawnPos;
    public float nextSpawnTime;
    public float spawnRate;

}

//--------------------------------------------------------------

public class SpawnerController_Authoring : MonoBehaviour
{
    public GameObject[] prefabsToSpawn;
    public float spawnRate;

    public Material[] listOfMaterials;
}

// actual baker converts monobehaviour into an entity and all of the referenced
// prefabs into entities
[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
class SpawnerController_Baker : Baker<SpawnerController_Authoring>
{

    public override void Bake(SpawnerController_Authoring authoring)
    {
        //UnityEngine.Debug.Log("CubeSpawnerBaker bake");
        Debug.Log("<b> <size=13> <color=#9DF155>Info : SpawnerController_Dots bake.</color> </size> </b>");
        Entity entity = GetEntity(TransformUsageFlags.None);
        if (authoring.prefabsToSpawn == null)
        {
            UnityEngine.Debug.LogError("SpawnerController_Dots bake missing prefabToSpawn");
            return;
        }

        var comp = new SpawnerComponent
        {
            spawnPos = authoring.transform.position,
            nextSpawnTime = 0f,
            spawnRate = authoring.spawnRate
        };

        int size = authoring.prefabsToSpawn.Length;
        DynamicBuffer<SpawnPrefabData> entitiesToSpawn = AddBuffer<SpawnPrefabData>(entity);
        foreach(var go in authoring.prefabsToSpawn)
        {
            // this allows us to author all prefabs anywhere but the authoring moves them to origin
            go.transform.position = Vector3.zero;
            if (Application.isPlaying)// to ensure that the projectile is active when spawning
            {
                go.SetActive(true);
            }
            
            Entity entityPrefab = GetEntity(go, TransformUsageFlags.Dynamic);
            if (entityPrefab != null)
            {
                entitiesToSpawn.Add(new SpawnPrefabData { Prefab = entityPrefab, Foo = 0 });
            }

            if (Application.isPlaying)
            {
                // hide all prefabs in scene
                go.SetActive(false);
            }
        }
        AddComponent(entity, comp);
    }
}

//--------------------------------------------------------------