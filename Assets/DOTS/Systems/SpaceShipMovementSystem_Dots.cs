using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Arcade flight on the flat play plane (y = 0).
// W/S = thrust and brake, A/D = turn. Speed decays when there is no thrust.
[BurstCompile]
[UpdateAfter(typeof(Input_to_SpaceShip_System_Dots))]
public partial struct SpaceShipMovementSystem_Dots : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        foreach (var (config, status, input, transform) in
                 SystemAPI.Query<RefRO<SpaceShipControllerData_Dots>,
                                 RefRW<SpaceShipStatus_Dots>,
                                 RefRO<InputData_Dots>,
                                 RefRW<LocalTransform>>())
        {
            SpaceShipControllerData_Dots c = config.ValueRO;
            float thrust = input.ValueRO.moveDir.y; // W = +1, S = -1
            float turn = input.ValueRO.moveDir.x;   // D = +1, A = -1

            float speed = status.ValueRO.speed;
            if (math.abs(thrust) > 0.01f)
                speed += thrust * c.acceleration * dt;
            else
                speed -= speed * math.min(1f, c.drag * dt);
            speed = math.clamp(speed, -c.maxSpeed * c.reverseSpeedFactor, c.maxSpeed);
            status.ValueRW.speed = speed;

            LocalTransform t = transform.ValueRO;
            t.Rotation = math.mul(t.Rotation, quaternion.RotateY(turn * c.turnSpeed * dt));

            float3 forward = t.Forward();
            forward.y = 0f;
            forward = math.normalizesafe(forward, new float3(0f, 0f, 1f));

            t.Position += forward * speed * dt;
            t.Position.y = 0f; // all gameplay happens on one plane
            transform.ValueRW = t;
        }
    }
}
