using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

public class AsteroidComponent_Authoring : MonoBehaviour
{
    public float speed = 2f;
    [Tooltip("Shots needed to break this asteroid")]
    public int hitPoints = 3;
}

public struct AsteroidComponent_Dots : IComponentData
{
    //public Unity.Mathematics.quaternion rotation;// = Unity.Mathematics.quaternion.random;
    public float3 rotation;
    public float rotationSpeed;
    public int hitPoints;
}

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
public class AsteroidBaker_Dots : Baker<AsteroidComponent_Authoring>
{
    public override void Bake(AsteroidComponent_Authoring authoring)
    {
        Entity asteroidEntity = GetEntity(TransformUsageFlags.None);
        Vector3 randomRot = Random.onUnitSphere * authoring.speed;
        
        AddComponent(asteroidEntity, new AsteroidComponent_Dots
        {
            rotation = randomRot,
            rotationSpeed = authoring.speed,
            hitPoints = authoring.hitPoints
        });
    }
}