using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Pirate behaviour, a small state machine per ship, shaped by its party's goal and aggression:
//   Hunt    pick the best target in sight (or loot) and fly to it.
//   Attack  circle the target at range, nose on it, and fire when it's in the arc.
//   Flee    badly damaged: run, and vanish once nobody is in sight.
//   Depart  has enough loot: leave the same way.
// Goals:
//   Raid      targets stations first, then bots, then ships.
//   Scavenge  steals loose ore (flies over it); shadows worker bots when there's none; only
//             shoots back after being hit.
//   Slavers   wears worker bots down and carries them off when they're nearly dead.
//   Hunt      goes for ships first.
// Aggression (0..1) widens sight, tightens the orbit and makes it run later.
// Pirates keep apart from each other (separation steering), and shoot through the normal
// projectile pipeline, so the layer matrix, density damage and friendly fire rules all apply.
[BurstCompile]
[UpdateAfter(typeof(WorkerBotSystem_Dots))]
public partial struct PirateSystem_Dots : ISystem
{
    private const float LootPerOre = 1f;
    private const float LootPerCapture = 5f;

    private struct Candidate
    {
        public Entity entity;
        public float2 position;
        public CollisionLayer layer;
        public bool isWorkerBot;
    }

    private struct Ore
    {
        public Entity entity;
        public float2 position;
    }

    private struct Mate
    {
        public Entity entity;
        public float2 position;
        public float radius;
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
            candidates.Add(new Candidate
            {
                entity = entity,
                position = transform.ValueRO.Position.xz,
                layer = tg.layer,
                isWorkerBot = SystemAPI.HasComponent<WorkerBot>(entity)
            });
        }

        // Loose ore nobody is pulling in yet.
        var ore = new NativeList<Ore>(Allocator.Temp);
        foreach (var (chunk, transform, entity) in
                 SystemAPI.Query<RefRO<OreChunk>, RefRO<LocalTransform>>()
                          .WithNone<DestroyedEntityTag_Dots>()
                          .WithEntityAccess())
            if (chunk.ValueRO.state != OreState.Collecting)
                ore.Add(new Ore { entity = entity, position = transform.ValueRO.Position.xz });

        // The pirates themselves, for separation.
        var mates = new NativeList<Mate>(Allocator.Temp);
        foreach (var (ai, transform, entity) in
                 SystemAPI.Query<RefRO<PirateAI>, RefRO<LocalTransform>>()
                          .WithNone<DestroyedEntityTag_Dots>()
                          .WithEntityAccess())
        {
            float radius = SystemAPI.HasComponent<Targetable>(entity) ? SystemAPI.GetComponent<Targetable>(entity).radius : 3f;
            mates.Add(new Mate { entity = entity, position = transform.ValueRO.Position.xz, radius = radius });
        }

        var taken = new NativeHashSet<Entity>(8, Allocator.Temp); // ore and bots claimed this frame
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        for (int p = 0; p < mates.Length; p++)
        {
            Entity self = mates[p].entity;
            PirateAI ai = SystemAPI.GetComponent<PirateAI>(self);
            PirateSettings s = SystemAPI.GetComponent<PirateSettings>(self);
            PirateBehaviour b = SystemAPI.HasComponent<PirateBehaviour>(self)
                ? SystemAPI.GetComponent<PirateBehaviour>(self)
                : new PirateBehaviour { goal = PirateGoal.Raid, aggression = 0.5f };
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

            // Personality.
            float a = math.saturate(b.aggression);
            float sight = s.sightRange * math.lerp(0.6f, 1.4f, a);
            float range = s.preferredRange * math.lerp(1.25f, 0.85f, a);
            float fleeBelow = s.fleeBelow * math.lerp(1.6f, 0.4f, a);

            // Hit? Remember it for a while (scavengers only fight when provoked).
            Health health = SystemAPI.HasComponent<Health>(self) ? SystemAPI.GetComponent<Health>(self) : new Health { current = 1f, max = 1f };
            if (ai.lastHealth > 0f && health.current < ai.lastHealth - 0.01f)
                ai.provokedTimer = 8f;
            ai.lastHealth = health.current;
            ai.provokedTimer = math.max(0f, ai.provokedTimer - dt);
            bool provoked = ai.provokedTimer > 0f;
            bool peaceful = b.goal == PirateGoal.Scavenge && !provoked;

            if (health.Fraction < fleeBelow && ai.state != PirateState.Flee)
                ai.state = PirateState.Flee;
            if (b.lootGoal > 0f && ai.loot >= b.lootGoal && ai.state != PirateState.Flee)
                ai.state = PirateState.Depart;

            // Where is the current target now?
            bool targetValid = false;
            float2 targetPos = float2.zero;
            int targetIndex = -1;
            if (ai.targetIsOre)
            {
                for (int o = 0; o < ore.Length; o++)
                    if (ore[o].entity == ai.target && !taken.Contains(ore[o].entity))
                    {
                        targetValid = true;
                        targetPos = ore[o].position;
                        break;
                    }
            }
            else
            {
                for (int c = 0; c < candidates.Length; c++)
                    if (candidates[c].entity == ai.target)
                    {
                        targetValid = true;
                        targetPos = candidates[c].position;
                        targetIndex = c;
                        break;
                    }
            }

            // Pick a target.
            ai.retargetTimer -= dt;
            bool hunting = ai.state == PirateState.Hunt || ai.state == PirateState.Attack;
            if (hunting && (!targetValid || ai.retargetTimer <= 0f))
            {
                ai.retargetTimer = 2f;
                Entity best = Entity.Null;
                bool bestIsOre = false;
                float bestScore = float.MaxValue;

                if (b.goal == PirateGoal.Scavenge && !provoked)
                {
                    for (int o = 0; o < ore.Length; o++)
                    {
                        if (taken.Contains(ore[o].entity)) continue;
                        float d = math.distance(ore[o].position, me);
                        if (d < sight && d < bestScore) { bestScore = d; best = ore[o].entity; targetPos = ore[o].position; bestIsOre = true; }
                    }
                }
                if (best == Entity.Null)
                {
                    for (int c = 0; c < candidates.Length; c++)
                    {
                        float d = math.distance(candidates[c].position, me);
                        if (d > sight) continue;
                        float score = d * Weight(b.goal, candidates[c], provoked);
                        if (candidates[c].entity == ai.target) score *= 0.8f; // stick with it unless something much better shows up
                        if (score < bestScore) { bestScore = score; best = candidates[c].entity; targetPos = candidates[c].position; targetIndex = c; bestIsOre = false; }
                    }
                }
                ai.target = best;
                ai.targetIsOre = bestIsOre;
                targetValid = best != Entity.Null;
                if (!targetValid && ai.state == PirateState.Attack)
                    ai.state = PirateState.Hunt;
            }

            float distance = targetValid ? math.distance(me, targetPos) : float.MaxValue;
            float2 desiredVelocity = float2.zero;
            float2 face = math.lengthsq(ai.velocity.xz) > 0.25f ? math.normalize(ai.velocity.xz) : Forward2(t);

            switch (ai.state)
            {
                case PirateState.Hunt:
                    if (!targetValid)
                        break;
                    if (ai.targetIsOre)
                    {
                        // Fly over the ore and scoop it up.
                        desiredVelocity = math.normalizesafe(targetPos - me) * s.maxSpeed;
                        face = math.normalizesafe(targetPos - me, face);
                        if (distance < mates[p].radius + 2f && taken.Add(ai.target))
                        {
                            ecb.AddComponent<DestroyedEntityTag_Dots>(ai.target);
                            ai.loot += LootPerOre;
                            ai.target = Entity.Null;
                            ai.retargetTimer = 0f;
                        }
                    }
                    else if (peaceful)
                    {
                        // Shadow the miners from a distance, waiting for ore to appear.
                        float2 away = math.normalizesafe(me - targetPos, face);
                        float2 goal = targetPos + away * range * 2f;
                        desiredVelocity = math.normalizesafe(goal - me) * math.min(s.maxSpeed, math.distance(goal, me));
                        face = math.normalizesafe(targetPos - me, face);
                    }
                    else
                    {
                        float2 toTarget = math.normalizesafe(targetPos - me);
                        desiredVelocity = toTarget * s.maxSpeed;
                        face = toTarget;
                        if (distance < range * 1.5f)
                            ai.state = PirateState.Attack;
                    }
                    break;

                case PirateState.Attack:
                    if (!targetValid || ai.targetIsOre || peaceful)
                    {
                        ai.state = PirateState.Hunt;
                        break;
                    }
                    {
                        float2 radial = math.normalizesafe(me - targetPos, new float2(1f, 0f));
                        float2 tangent = new float2(-radial.y, radial.x) * ai.orbitSign;
                        float2 goal = targetPos + math.normalizesafe(radial + tangent * 0.8f) * range;
                        float2 toGoal = goal - me;
                        desiredVelocity = math.normalizesafe(toGoal) * math.min(s.maxSpeed, math.length(toGoal) * 2f + s.maxSpeed * 0.4f);
                        face = math.normalizesafe(targetPos - me);
                        if (distance > range * 3f)
                            ai.state = PirateState.Hunt;

                        // Slavers: carry off a worker bot once it's nearly dead.
                        if (b.goal == PirateGoal.Slavers && targetIndex >= 0 && candidates[targetIndex].isWorkerBot
                            && distance < range * 0.9f && SystemAPI.HasComponent<Health>(ai.target)
                            && SystemAPI.GetComponent<Health>(ai.target).Fraction < 0.35f && taken.Add(ai.target))
                        {
                            ecb.AddComponent<DestroyedEntityTag_Dots>(ai.target);
                            ai.loot += LootPerCapture;
                            ai.target = Entity.Null;
                            ai.retargetTimer = 0f;
                            ai.state = PirateState.Hunt;
                            targetValid = false;
                        }
                    }
                    break;

                case PirateState.Flee:
                case PirateState.Depart:
                {
                    float2 away = targetValid && !ai.targetIsOre ? math.normalizesafe(me - targetPos, face) : face;
                    desiredVelocity = away * s.maxSpeed;
                    face = away;
                    bool seen = false;
                    for (int c = 0; c < candidates.Length && !seen; c++)
                        seen = math.distance(candidates[c].position, me) < sight * 1.2f;
                    if (!seen)
                        ecb.AddComponent<DestroyedEntityTag_Dots>(self); // got away
                    break;
                }
            }

            // Keep apart from the other pirates.
            float2 push = float2.zero;
            for (int m = 0; m < mates.Length; m++)
            {
                if (m == p) continue;
                float2 offset = me - mates[m].position;
                float minGap = (mates[p].radius + mates[m].radius) * 1.6f;
                float d = math.length(offset);
                if (d < minGap)
                    push += (d > 0.01f ? offset / d : new float2(ai.orbitSign, 0f)) * (1f - d / minGap);
            }
            desiredVelocity += push * s.maxSpeed;

            // Fly: ease the velocity toward the wanted one, then turn toward the wanted facing.
            float2 v = ai.velocity.xz;
            float2 dv = desiredVelocity - v;
            float maxDv = s.acceleration * dt;
            if (math.lengthsq(dv) > maxDv * maxDv)
                dv = math.normalize(dv) * maxDv;
            v += dv;
            float speedCap = s.maxSpeed * 1.2f;
            if (math.lengthsq(v) > speedCap * speedCap)
                v = math.normalize(v) * speedCap;
            ai.velocity = new float3(v.x, 0f, v.y);
            t.Position += ai.velocity * dt;
            t.Position.y = 0f;
            t.Rotation = TurnToward(t.Rotation, face, s.turnSpeed * dt);

            // Shoot.
            ai.fireTimer -= dt;
            bool mayShoot = targetValid && !ai.targetIsOre && !peaceful
                            && (ai.state == PirateState.Hunt || ai.state == PirateState.Attack);
            if (mayShoot && ai.fireTimer <= 0f)
            {
                WeaponDef def = WeaponData.Find(s.weapon, hasCatalog, catalog);
                float weaponRange = def.speed * def.lifetime * 0.9f;
                float2 forward = Forward2(t);
                float2 toTarget = math.normalizesafe(targetPos - me);
                float angle = math.acos(math.clamp(math.dot(forward, toTarget), -1f, 1f));
                if (distance <= weaponRange && angle <= s.fireArc)
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
        taken.Dispose();
        mates.Dispose();
        ore.Dispose();
        candidates.Dispose();
    }

    // Lower = more attractive. Multiplies the distance when choosing a target.
    private static float Weight(PirateGoal goal, in Candidate c, bool provoked)
    {
        bool bot = c.layer == CollisionLayer.Bot, building = c.layer == CollisionLayer.Building;
        switch (goal)
        {
            case PirateGoal.Raid:     return building ? 0.5f : bot ? 0.7f : 1f;
            case PirateGoal.Slavers:  return c.isWorkerBot ? 0.4f : building ? 3f : 1.5f;
            case PirateGoal.Hunt:     return building ? 1.5f : bot ? 1f : 0.5f;
            default:                  return provoked ? (building ? 2f : 1f) : (c.isWorkerBot ? 0.5f : 2f); // Scavenge
        }
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
