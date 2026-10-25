using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Arcade flight on the flat play plane (y = 0).
// W/S = thrust and brake, A/D = strafe, Q/E = rotate. Speed decays when there is no input.
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
            float strafe = input.ValueRO.moveDir.x; // D = +1, A = -1
            float rotate = input.ValueRO.lookDir.x; // E = +1, Q = -1

            float speed = Accelerate(status.ValueRO.speed, thrust, c.acceleration, c.drag, dt);
            speed = math.clamp(speed, -c.maxSpeed * c.reverseSpeedFactor, c.maxSpeed);

            float strafeMax = c.maxSpeed * c.strafeSpeedFactor;
            float strafeSpeed = Accelerate(status.ValueRO.strafeSpeed, strafe, c.acceleration, c.drag, dt);
            strafeSpeed = math.clamp(strafeSpeed, -strafeMax, strafeMax);

            status.ValueRW.speed = speed;
            status.ValueRW.strafeSpeed = strafeSpeed;

            LocalTransform t = transform.ValueRO;
            t.Rotation = math.mul(t.Rotation, quaternion.RotateY(rotate * c.turnSpeed * dt));

            float3 forward = t.Forward();
            forward.y = 0f;
            forward = math.normalizesafe(forward, new float3(0f, 0f, 1f));
            float3 right = new float3(forward.z, 0f, -forward.x);

            t.Position += (forward * speed + right * strafeSpeed) * dt;
            t.Position.y = 0f; // all gameplay happens on one plane
            transform.ValueRW = t;
        }
    }

    private static float Accelerate(float current, float input, float acceleration, float drag, float dt)
    {
        if (math.abs(input) > 0.01f)
            return current + input * acceleration * dt;
        return current - current * math.min(1f, drag * dt);
    }
}
