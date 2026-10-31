using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Engine and thruster flames.
// 1. Spawn: the first time a ship with Propulsion appears (and its faction is known), a flame
//    from the Engine Effect Library is parented to every attachment point. Engines get a basic
//    flame and, if set, a boost flame. Flames join the ship's LinkedEntityGroup so they die with it.
// 2. Measure: each ship's motion (forward/sideways speed, turn rate) plus the player's keys.
// 3. Drive: each flame works out how much its nozzle helps the current move (its push direction
//    against the wanted direction) and turn (its push times its lever arm), eases its throttle
//    toward that, flickers a little, and stretches along its length. Engines crossfade to the
//    boost flame above boostSpeed.
[BurstCompile]
[UpdateAfter(typeof(WorkerBotSystem_Dots))]
[UpdateAfter(typeof(SpaceShipMovementSystem_Dots))]
[UpdateAfter(typeof(HealthSystem_Dots))]
public partial struct PropulsionSystem_Dots : ISystem
{
    private const float FullTurnRate = 2f; // radians per second that counts as full rotation thrust
    private const float Response = 10f;    // how fast throttle follows, per second
    private const float Hidden = 0.02f;    // below this a flame is hidden
    private bool warnedNoLibrary;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Propulsion>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        float time = (float)SystemAPI.Time.ElapsedTime;

        SpawnFlames(ref state);
        if (dt <= 0f)
            return;
        MeasureShips(ref state, dt);
        DriveFlames(ref state, dt, time);
    }

    private void SpawnFlames(ref SystemState state)
    {
        var pending = new NativeList<Entity>(Allocator.Temp);
        foreach (var (propulsion, entity) in SystemAPI.Query<RefRO<Propulsion>>().WithEntityAccess())
            if (!propulsion.ValueRO.spawned)
                pending.Add(entity);
        if (pending.Length == 0)
        {
            pending.Dispose();
            return;
        }

        if (!SystemAPI.HasSingleton<GameLibrary>())
        {
            if (!warnedNoLibrary)
            {
                UnityEngine.Debug.LogWarning("Propulsion: add GameLibrary_Authoring to the subscene to get engine flames.");
                warnedNoLibrary = true;
            }
            pending.Dispose();
            return;
        }

        EntityManager em = state.EntityManager;
        Entity libraryEntity = SystemAPI.GetSingletonEntity<GameLibrary>();
        var placed = new NativeList<Entity>(Allocator.Temp);

        foreach (Entity ship in pending)
        {
            Propulsion p = em.GetComponentData<Propulsion>(ship);

            // Wait until the ship's faction is known when it picks its colour by faction.
            CombatFaction faction = em.HasComponent<Targetable>(ship) ? em.GetComponentData<Targetable>(ship).faction : CombatFaction.None;
            if (p.colour == FlameColourChoice.Faction && faction == CombatFaction.None && em.HasComponent<InputData_Dots>(ship))
                continue;
            FlameColour colour = EngineEffects.Resolve(p.colour, faction);

            float4x4 toShip = math.inverse(em.GetComponentData<LocalToWorld>(ship).Value);
            NativeArray<PropulsionSocket> sockets = em.GetBuffer<PropulsionSocket>(ship).ToNativeArray(Allocator.Temp);
            placed.Clear();

            for (int i = 0; i < sockets.Length; i++)
            {
                Entity socket = sockets[i].socket;
                if (!em.Exists(socket) || !em.HasComponent<LocalToWorld>(socket))
                    continue;
                float4x4 rel = math.mul(toShip, em.GetComponentData<LocalToWorld>(socket).Value);
                float3 exhaust = math.normalizesafe(rel.c1.xyz); // the point's green arrow (+Y)
                float3 position = rel.c3.xyz;
                // The exhaust leaves along +Y, so the nozzle pushes the ship the opposite way.
                float2 push = math.normalizesafe(-exhaust.xz, new float2(0f, 1f));

                bool engine = sockets[i].kind == PropulsionKind.Engine;
                int basicStyle = engine ? p.engineStyle : p.thrusterStyle;
                float size = engine ? p.engineSize : p.thrusterSize;
                DynamicBuffer<EngineEffectPrefab> library = em.GetBuffer<EngineEffectPrefab>(libraryEntity);
                Entity basicPrefab = EngineEffects.Find(library, basicStyle, colour);
                Entity boostPrefab = engine && p.boostStyle > 0 ? EngineEffects.Find(library, p.boostStyle, colour) : Entity.Null;

                var flame = new Flame
                {
                    ship = ship,
                    kind = sockets[i].kind,
                    push = push,
                    arm = position.xz,
                    size = size,
                    fullThrustSpeed = p.fullThrustSpeed,
                    boostSpeed = boostPrefab != Entity.Null ? p.boostSpeed : 0f,
                    seed = math.hash(position + (float)i) % 1024 * 0.37f
                };
                Place(em, basicPrefab, socket, flame, ref placed);
                flame.isBoost = true;
                Place(em, boostPrefab, socket, flame, ref placed);
            }
            sockets.Dispose();

            // Flames die with the ship.
            if (placed.Length > 0)
            {
                if (!em.HasBuffer<LinkedEntityGroup>(ship))
                    em.AddBuffer<LinkedEntityGroup>(ship).Add(ship);
                DynamicBuffer<LinkedEntityGroup> group = em.GetBuffer<LinkedEntityGroup>(ship);
                for (int k = 0; k < placed.Length; k++)
                    group.Add(placed[k]);
            }

            if (!em.HasComponent<ThrustState>(ship))
            {
                LocalTransform t = em.GetComponentData<LocalTransform>(ship);
                em.AddComponentData(ship, new ThrustState { lastPosition = t.Position, lastRotation = t.Rotation });
            }
            p.spawned = true;
            em.SetComponentData(ship, p);
        }

        placed.Dispose();
        pending.Dispose();
    }

    private static void Place(EntityManager em, Entity prefab, Entity socket, Flame flame, ref NativeList<Entity> placed)
    {
        if (prefab == Entity.Null)
            return;
        Entity e = em.Instantiate(prefab);
        // Flame meshes stretch along their own -Z; turn them so that runs along the point's +Y.
        em.SetComponentData(e, LocalTransform.FromRotation(quaternion.RotateX(math.radians(90f))));
        em.AddComponentData(e, new Parent { Value = socket });
        em.AddComponentData(e, new PostTransformMatrix { Value = float4x4.Scale(0f) });
        em.AddComponentData(e, flame);

        // Collect the flame and its child parts (nozzle glow) so they can join the ship's group.
        if (em.HasBuffer<LinkedEntityGroup>(e))
        {
            NativeArray<LinkedEntityGroup> parts = em.GetBuffer<LinkedEntityGroup>(e).ToNativeArray(Allocator.Temp);
            for (int i = 0; i < parts.Length; i++)
                placed.Add(parts[i].Value);
            parts.Dispose();
        }
        else
        {
            placed.Add(e);
        }
    }

    private void MeasureShips(ref SystemState state, float dt)
    {
        foreach (var (thrust, transform, entity) in
                 SystemAPI.Query<RefRW<ThrustState>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            LocalTransform t = transform.ValueRO;
            float3 velocity = (t.Position - thrust.ValueRO.lastPosition) / dt;
            if (math.lengthsq(velocity) > 1000f * 1000f)
                velocity = float3.zero; // teleported (respawn, start placement)
            float3 local = math.mul(math.inverse(t.Rotation), velocity);

            float3 fwdNow = math.mul(t.Rotation, new float3(0f, 0f, 1f));
            float3 fwdThen = math.mul(thrust.ValueRO.lastRotation, new float3(0f, 0f, 1f));
            float deltaYaw = math.atan2(fwdNow.x, fwdNow.z) - math.atan2(fwdThen.x, fwdThen.z);
            if (deltaYaw > math.PI) deltaYaw -= 2f * math.PI;
            if (deltaYaw < -math.PI) deltaYaw += 2f * math.PI;

            thrust.ValueRW.localVelocity = new float2(local.x, local.z);
            thrust.ValueRW.yawRate = deltaYaw / dt;
            thrust.ValueRW.speed = math.length(velocity.xz);
            thrust.ValueRW.lastPosition = t.Position;
            thrust.ValueRW.lastRotation = t.Rotation;

            if (SystemAPI.HasComponent<InputData_Dots>(entity))
            {
                InputData_Dots input = SystemAPI.GetComponent<InputData_Dots>(entity);
                thrust.ValueRW.inputMove = input.moveDir;
                thrust.ValueRW.inputTurn = input.lookDir.x;
                thrust.ValueRW.hasInput = true;
            }
        }
    }

    private void DriveFlames(ref SystemState state, float dt, float time)
    {
        float ease = 1f - math.exp(-Response * dt);
        foreach (var (flameRef, post) in SystemAPI.Query<RefRW<Flame>, RefRW<PostTransformMatrix>>())
        {
            Flame f = flameRef.ValueRO;
            if (!SystemAPI.HasComponent<ThrustState>(f.ship))
                continue;
            ThrustState s = SystemAPI.GetComponent<ThrustState>(f.ship);

            // The move the ship wants: keys for the player, actual motion for everyone.
            float2 measured = math.clamp(new float2(s.localVelocity.x / (f.fullThrustSpeed * 0.5f),
                                                    s.localVelocity.y / f.fullThrustSpeed), -1f, 1f);
            float2 wanted = measured;
            if (s.hasInput)
            {
                if (math.abs(s.inputMove.x) > 0.01f) wanted.x = s.inputMove.x;
                if (math.abs(s.inputMove.y) > 0.01f) wanted.y = s.inputMove.y;
            }
            float turn = s.hasInput && math.abs(s.inputTurn) > 0.01f
                ? s.inputTurn
                : math.clamp(s.yawRate / FullTurnRate, -1f, 1f);

            float linear = math.saturate(math.dot(f.push, wanted));
            float target = linear;
            if (f.kind == PropulsionKind.Thruster)
            {
                // Turning effect of this nozzle: + = turns the ship right.
                float armLength = math.length(f.arm);
                float torque = armLength > 0.01f ? (f.arm.y * f.push.x - f.arm.x * f.push.y) / armLength : 0f;
                target = math.max(linear, math.saturate(torque * turn));
            }

            bool boosting = f.kind == PropulsionKind.Engine && f.boostSpeed > 0f && s.speed > f.boostSpeed;
            f.throttle = math.lerp(f.throttle, target, ease);
            f.boostBlend = math.lerp(f.boostBlend, boosting ? 1f : 0f, ease);
            flameRef.ValueRW = f;

            float share = f.isBoost ? f.boostBlend : (f.boostSpeed > 0f ? 1f - f.boostBlend : 1f);
            float amount = f.throttle * share;
            if (amount < Hidden)
            {
                post.ValueRW.Value = float4x4.Scale(0f);
                continue;
            }
            float flicker = 1f + 0.08f * math.sin(time * 37f + f.seed) + 0.05f * math.sin(time * 23f + f.seed * 2.3f);
            float width = f.size * (0.55f + 0.45f * amount);
            float length = f.size * amount * flicker;
            post.ValueRW.Value = float4x4.Scale(width, width, length);
        }
    }
}
