using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

public partial struct SpaceShipMovementSystem_Dots : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach(var (moveStats, inputs, transform) in 
            SystemAPI.Query<
                RefRO<SpaceShipControllerData_Dots>, 
                RefRO<InputData_Dots>, 
                RefRW<LocalTransform>>()) 
        {
            float3 position = transform.ValueRO.Position;
            float3 forward = transform.ValueRO.Forward();

            var timeMult = SystemAPI.Time.DeltaTime * moveStats.ValueRO.maxSpeed;
            position.x += inputs.ValueRO.moveDir.x * timeMult;
            position.z += inputs.ValueRO.moveDir.y * timeMult;
            transform.ValueRW.Position = position;


            transform.ValueRW.RotateZ( inputs.ValueRO.lookDir.x);
        }
    }
}
