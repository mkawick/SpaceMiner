using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Pirate behaviour, a small state machine per ship:
//   Hunt   pick the best target in sight and fly at it. Lone worker bots are the favourite prey,
//          then buildings, then ships (score = distance x weight).
//   Attack circle the target at preferredRange, nose on it, and fire whenever it's in the arc,
//          alternating between guns. Back to Hunt if it gets away.
//   Flee   below fleeBelow health: run from the target and disappear once nobody is in sight.
// Targets are anything the Layer Matrix lets a Ship target that isn't neutral or a pirate.
// Pirates only shoot through the normal projectile pipeline, so the matrix, density damage and
// friendly fire rules all apply to them too.
[BurstCompile]
[UpdateAfter(typeof(WorkerBotSystem_Dots))]
public partial struct PirateSystem_Dots : ISystem
{
    private struct Candidate
    {
        public Entity entity;
        public float2 position;
        public float weight;
    }

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PirateAI>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        if (dt <= 0f)
            return;

        LayerMatrix matrix = SystemAPI.HasSingleton<LayerMatrix>() ? SystemAPI.GetSingleton<LayerMatrix>() : LayerMatrix.Default();
        bool hasCatalog = SystemAPI.HasSingleton<WeaponCatalog>();
        DynamicBuffer<WeaponDef> catalog = hasCatalog
            ? SystemAPI.GetBuffer<WeaponDef>(SystemAPI.GetSingletonEntity<WeaponCatalog>())
            : default;

        // Everything worth shooting at.
        var candidates = new NativeList<Candidate>(Allocator.Temp);
        foreach (var (targetable, transform, entity) in
                 SystemAPI.Query<RefRO<Targetable>, RefRO<LocalTransform>>()
                          .WithAll<Health>()
                          .WithNone<DestroyedEntityTag_Dots>()
                          .WithEntityAccess())
        {
            Targetable tg = targetable.ValueRO;
            if (tg.faction == CombatFaction.None || tg.faction == CombatFaction.Pirates)
                continue;
            if (!matrix.CanTarget(CollisionLayer.Ship, tg.layer))
                continue;
            float weight = tg.layer == CollisionLayer.Bot ? 0.6f
                         : tg.layer == CollisionLayer.Building ? 0.8f
                         : 1f;
            candidates.Add(new Candidate { entity = entity, position = transform.ValueRO.Position.xz, weight = weight });
        }

        var pirates = new NativeList<Entity>(Allocator.Temp);
        foreach (var (ai, entity) in SystemAPI.Query<RefRO<PirateAI>>().WithNone<DestroyedEntityTag_Dots>().WithEntityAccess())
            pirates.Add(entity);

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        for (int p = 0; p < pirates.Length; p++)
        {
            Entity self = pirates[p];
            PirateAI ai = SystemAPI.GetComponent<PirateAI>(self);
            PirateSettings s = SystemAPI.GetComponent<PirateSettings>(self);
            LocalTransform t = SystemAPI.GetComponent<LocalTransform>(self);
            float2 me = t.Position.xz;

            // Ships authored with a plain Targetable may not say they're pirates; make sure.
            if (SystemAPI.HasComponent<Targetable>(self))
            {
                Targetable tg = SystemAPI.GetComponent<Targetable>(self);
                if (tg.faction == CombatFaction.None)
                {
                    tg.faction = CombatFaction.Pirates;
                    SystemAPI.SetComponent(self, tg);
                }
            }

            float healthFraction = SystemAPI.HasComponent<Health>(self) ? SystemAPI.GetComponent<Health>(self).Fraction : 1f;
            if (healthFraction < s.fleeBelow && ai.state != PirateState.Flee)
                ai.state = PirateState.Flee;

            // Is the current target still around?
            bool targetValid = false;
            float2 targetPos = float2.zero;
            for (int c = 0; c < candidates.Length; c++)
                if (candidates[c].entity == ai.target)
                {
                    targetValid = true;
                    targetPos = candidates[c].position;
                    break;
                }

            ai.retargetTimer -= dt;
            if (ai.state != PirateState.Flee && (!targetValid || ai.retargetTimer <= 0f))
            {
                ai.retargetTimer = 2f;
                float best = float.MaxValue;
                Entity bestEntity = Entity.Null;
                for (int c = 0; c < candidates.Length; c++)
                {
                    float d = math.distance(candidates[c].position, me);
                    if (d > s.sightRange)
                        continue;
                    float score = d * candidates[c].weight;
                    if (candidates[c].entity == ai.target)
                        score *= 0.8f; // stick with what you're on unless something much better shows up
                    if (score < best)
                    {
                        best = score;
                        bestEntity = candidates[c].entity;
                        targetPos = candidates[c].position;
                    }
                }
                ai.target = bestEntity;
                targetValid = bestEntity != Entity.Null;
                if (!targetValid && ai.state == PirateState.Attack)
                    ai.state = PirateState.Hunt;
            }

            float distance = targetValid ? math.distance(me, targetPos) : float.MaxValue;
            float2 desiredVelocity = float2.zero;
            float2 face = math.lengthsq(ai.velocity.xz) > 0.25f ? math.normalize(ai.velocity.xz) : Forward2(t);

            switch (ai.state)
            {
                case PirateState.Hunt:
                    if (targetValid)
                    {
                        float2 toTarget = math.normalizesafe(targetPos - me);
                        desiredVelocity = toTarget * s.maxSpeed;
                        face = toTarget;
                        if (distance < s.preferredRange * 1.5f)
                            ai.state = PirateState.Attack;
                    }
                    break;

                case PirateState.Attack:
                    if (!targetValid)
                    {
                        ai.state = PirateState.Hunt;
                        break;
                    }
                    {
                        float2 radial = math.normalizesafe(me - targetPos, new float2(1f, 0f));
                        float2 tangent = new float2(-radial.y, radial.x) * ai.orbitSign;
                        float2 goal = targetPos + math.normalizesafe(radial + tangent * 0.8f) * s.preferredRange;
                        float2 toGoal = goal - me;
                        desiredVelocity = math.normalizesafe(toGoal) * math.min(s.maxSpeed, math.length(toGoal) * 2f + s.maxSpeed * 0.4f);
                        face = math.normalizesafe(targetPos - me);
                        if (distance > s.preferredRange * 3f)
                            ai.state = PirateState.Hunt;
                    }
                    break;

                case PirateState.Flee:
                {
                    float2 away = targetValid ? math.normalizesafe(me - targetPos, face) : face;
                    desiredVelocity = away * s.maxSpeed;
                    face = away;
                    // Escaped: nobody in sight any more.
                    bool seen = false;
                    for (int c = 0; c < candidates.Length && !seen; c++)
                        seen = math.distance(candidates[c].position, me) < s.sightRange * 1.2f;
                    if (!seen)
                        ecb.AddComponent<DestroyedEntityTag_Dots>(self);
                    break;
                }
            }

            // Fly: ease the velocity toward the wanted one, then turn toward the wanted facing.
            float2 v = ai.velocity.xz;
            float2 dv = desiredVelocity - v;
            float maxDv = s.acceleration * dt;
            if (math.lengthsq(dv) > maxDv * maxDv)
                dv = math.normalize(dv) * maxDv;
            v += dv;
            ai.velocity = new float3(v.x, 0f, v.y);
            t.Position += ai.velocity * dt;
            t.Position.y = 0f;
            t.Rotation = TurnToward(t.Rotation, face, s.turnSpeed * dt);

            // Shoot.
            ai.fireTimer -= dt;
            if (targetValid && ai.state != PirateState.Flee && ai.fireTimer <= 0f)
            {
                WeaponDef def = WeaponData.Find(s.weapon, hasCatalog, catalog);
                float range = def.speed * def.lifetime * 0.9f;
                float2 forward = Forward2(t);
                float2 toTarget = math.normalizesafe(targetPos - me);
                float angle = math.acos(math.clamp(math.dot(forward, toTarget), -1f, 1f));
                if (distance <= range && angle <= s.fireArc)
                {
                    DynamicBuffer<PirateGun> guns = SystemAPI.GetBuffer<PirateGun>(self);
                    float3 muzzle = t.TransformPoint(guns[ai.nextGun % guns.Length].localPosition);
                    muzzle.y = 0f;
                    float3 aim = new float3(targetPos.x, 0f, targetPos.y) - muzzle;
                    Entity request = ecb.CreateEntity();
                    ecb.AddComponent(request, new ProjectileRequest
                    {
                        weapon = s.weapon,
                        owner = self,
                        faction = CombatFaction.Pirates,
                        position = muzzle,
                        dir = quaternion.LookRotationSafe(math.normalizesafe(aim, new float3(forward.x, 0f, forward.y)), math.up()),
                        speedBonus = math.max(0f, math.dot(ai.velocity.xz, forward))
                    });
                    ai.nextGun++;
                    ai.fireTimer = math.max(0.05f, def.fireInterval) * (guns.Length > 1 ? 0.5f : 1f);
                }
            }

            SystemAPI.SetComponent(self, ai);
            SystemAPI.SetComponent(self, t);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
        pirates.Dispose();
        candidates.Dispose();
    }

    private static float2 Forward2(LocalTransform t)
    {
        float3 f = math.mul(t.Rotation, new float3(0f, 0f, 1f));
        return math.normalizesafe(f.xz, new float2(0f, 1f));
    }

    private static quaternion TurnToward(quaternion current, float2 direction, float maxRadians)
    {
        if (math.lengthsq(direction) < 1e-6f)
            return current;
        float3 f = math.mul(current, new float3(0f, 0f, 1f));
        float yawNow = math.atan2(f.x, f.z);
        float yawWanted = math.atan2(direction.x, direction.y);
        float delta = yawWanted - yawNow;
        delta = math.atan2(math.sin(delta), math.cos(delta)); // wrap to -pi..pi
        delta = math.clamp(delta, -maxRadians, maxRadians);
        return quaternion.RotateY(yawNow + delta);
    }
}
