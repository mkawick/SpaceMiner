using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// Reads the Input System and writes it into the player ship's InputData_Dots.
// Left mouse fires along the ship's facing. The right mouse button now belongs to the chase camera.
public partial class Input_to_SpaceShip_System_Dots : SystemBase
{
    private InputControls input;

    protected override void OnCreate()
    {
        input = new InputControls();
        input.Enable();
        RequireForUpdate<InputData_Dots>();
    }

    protected override void OnDestroy()
    {
        if (input != null)
        {
            input.Disable();
            input.Dispose();
        }
    }

    protected override void OnUpdate()
    {
        float2 move = input.Player.Move.ReadValue<Vector2>();
        bool firing = input.Player.Attack.ReadValue<float>() > 0f;
        float now = (float)SystemAPI.Time.ElapsedTime;

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (config, status, inputData, transform) in
                 SystemAPI.Query<RefRO<SpaceShipControllerData_Dots>,
                                 RefRW<SpaceShipStatus_Dots>,
                                 RefRW<InputData_Dots>,
                                 RefRO<LocalTransform>>())
        {
            inputData.ValueRW.moveDir = move;
            inputData.ValueRW.lookDir = float2.zero;

            if (!firing || now < status.ValueRO.nextTimeToFire)
                continue;

            LocalTransform t = transform.ValueRO;
            Entity request = ecb.CreateEntity();
            ecb.AddComponent(request, new ProjectileRequest
            {
                projectileEnum = 0,
                damageIfHits = 1,
                ownerId = 42,
                position = t.TransformPoint(config.ValueRO.firingPoint),
                dir = t.Rotation,
                // add the ship's own speed so shots never fall behind a fast ship
                speed = config.ValueRO.projectileSpeed + math.max(0f, status.ValueRO.speed)
            });
            status.ValueRW.nextTimeToFire = now + status.ValueRO.fireRate;
        }

        ecb.Playback(EntityManager);
        ecb.Dispose();
    }
}
