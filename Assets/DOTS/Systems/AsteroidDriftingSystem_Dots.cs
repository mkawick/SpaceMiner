using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public partial struct AsteroidDriftingSystem_Dots : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach(var (driftData, transform) in 
                SystemAPI.Query<
                    RefRO<AsteroidComponent_Dots>, 
                    RefRW<LocalTransform>>()) 
        {
            Vector3 rotationAmount = driftData.ValueRO.rotation;
            //Unity.Mathematics.float3;
            Quaternion frameRotation = Quaternion.Euler(rotationAmount * Time.deltaTime);// * driftData.ValueRO.rotationSpeed);
            //Debug.Log("drifting");
            quaternion q = transform.ValueRO.Rotation;
            q = math.mul(q, frameRotation);
            transform.ValueRW.Rotation = q;
        }
    }
}