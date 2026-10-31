using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.InputSystem;

// Sends pirate parties at the player. The first wave comes after firstWaveDelay, then one every
// waveInterval (while fewer than maxAlive pirates are around). Each wave picks a party from the
// Pirate Roster at random, weighted by the parties' Chance values (they don't need to add up to
// 100), rolls how many of each ship it brings, and lines them up in the party's formation
// spawnDistance away, heading in. F9 (testing) sends a wave right now.
public partial class PirateSpawnSystem_Dots : SystemBase
{
    private double nextWave = -1;
    private int waveCount;
    private Random rng;

    protected override void OnCreate()
    {
        RequireForUpdate<PirateSpawnConfig>();
        RequireForUpdate<InputData_Dots>();
        rng = new Random(0x5EA5u ^ (uint)System.DateTime.Now.Ticks | 1u);
    }

    protected override void OnUpdate()
    {
        Entity library = SystemAPI.GetSingletonEntity<PirateSpawnConfig>();
        PirateSpawnConfig cfg = SystemAPI.GetComponent<PirateSpawnConfig>(library);
        double now = SystemAPI.Time.ElapsedTime;
        if (nextWave < 0)
            nextWave = now + cfg.firstWaveDelay;

        Keyboard keyboard = Keyboard.current;
        bool forced = keyboard != null && keyboard.f9Key.wasPressedThisFrame;
        if (!forced && (!cfg.enabled || now < nextWave))
            return;
        nextWave = now + cfg.waveInterval;

        if (!forced && SystemAPI.QueryBuilder().WithAll<PirateAI>().Build().CalculateEntityCount() >= cfg.maxAlive)
            return;

        NativeArray<PiratePartyDef> parties = SystemAPI.GetBuffer<PiratePartyDef>(library).ToNativeArray(Allocator.Temp);
        NativeArray<PiratePartyShip> partyShips = SystemAPI.GetBuffer<PiratePartyShip>(library).ToNativeArray(Allocator.Temp);
        if (parties.Length == 0)
        {
            UnityEngine.Debug.LogWarning("Pirates: the Pirate Roster has no parties with ships (Tools > Spacey > Pirate Roster).");
            parties.Dispose();
            partyShips.Dispose();
            return;
        }

        PiratePartyDef party = PickParty(parties);

        // Roll the ships.
        var ships = new NativeList<Entity>(Allocator.Temp);
        for (int i = 0; i < party.shipCount; i++)
        {
            PiratePartyShip entry = partyShips[party.shipStart + i];
            int n = rng.NextInt(entry.min, entry.max + 1);
            for (int k = 0; k < n; k++)
                ships.Add(entry.prefab);
        }
        if (ships.Length == 0)
            ships.Add(partyShips[party.shipStart].prefab);

        // Spacing: the party's, or enough that the biggest ships don't overlap.
        float biggest = 1f;
        for (int i = 0; i < ships.Length; i++)
            if (EntityManager.HasComponent<Targetable>(ships[i]))
                biggest = math.max(biggest, EntityManager.GetComponentData<Targetable>(ships[i]).radius);
        float spacing = party.spacing > 0f ? party.spacing : biggest * 2.6f;

        float3 playerPos = SystemAPI.QueryBuilder().WithAll<InputData_Dots, LocalTransform>().Build()
                                    .GetSingleton<LocalTransform>().Position;
        float angle = rng.NextFloat(0f, 2f * math.PI);
        float3 dir = new float3(math.sin(angle), 0f, math.cos(angle));
        float3 forward = -dir; // the party heads toward the player
        float3 right = math.cross(math.up(), forward);
        float3 centre = playerPos + dir * cfg.spawnDistance;
        quaternion facing = quaternion.LookRotationSafe(forward, math.up());

        waveCount++;
        var placed = new NativeList<float2>(Allocator.Temp);
        for (int i = 0; i < ships.Length; i++)
        {
            float2 slot = Slot(party.formation, i, ships.Length, spacing, placed);
            placed.Add(slot);
            Entity pirate = EntityManager.Instantiate(ships[i]);
            LocalTransform t = EntityManager.GetComponentData<LocalTransform>(pirate);
            t.Position = centre + right * slot.x + forward * slot.y;
            t.Rotation = facing;
            EntityManager.SetComponentData(pirate, t);

            if (EntityManager.HasComponent<PirateAI>(pirate))
                EntityManager.SetComponentData(pirate, new PirateAI
                {
                    state = PirateState.Hunt,
                    orbitSign = rng.NextBool() ? 1f : -1f,
                    retargetTimer = rng.NextFloat(0f, 0.5f)
                });
            EntityManager.AddComponentData(pirate, new PirateBehaviour
            {
                goal = party.goal,
                aggression = party.aggression,
                lootGoal = party.lootGoal,
                party = waveCount
            });
        }
        UnityEngine.Debug.Log($"Pirates: {party.name} ({ships.Length} ship{(ships.Length == 1 ? "" : "s")}, {party.goal})");

        placed.Dispose();
        ships.Dispose();
        parties.Dispose();
        partyShips.Dispose();
    }

    // Weighted random: add up all the chances and pick a point along that total.
    private PiratePartyDef PickParty(NativeArray<PiratePartyDef> parties)
    {
        float total = 0f;
        for (int i = 0; i < parties.Length; i++)
            total += parties[i].weight;
        if (total <= 0f)
            return parties[rng.NextInt(parties.Length)];
        float roll = rng.NextFloat(total);
        for (int i = 0; i < parties.Length; i++)
        {
            roll -= parties[i].weight;
            if (roll < 0f)
                return parties[i];
        }
        return parties[parties.Length - 1];
    }

    // Position of ship i in the formation: x = to the right, y = forward (toward the player).
    private float2 Slot(PartyFormation formation, int i, int count, float spacing, NativeList<float2> placed)
    {
        switch (formation)
        {
            case PartyFormation.Line:
                return new float2((i - (count - 1) * 0.5f) * spacing, 0f);
            case PartyFormation.Column:
                return new float2(0f, -i * spacing);
            case PartyFormation.Cluster:
            {
                float area = spacing * math.sqrt(count);
                float2 best = float2.zero;
                float bestGap = -1f;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    float2 p = rng.NextFloat2(-area, area);
                    float gap = float.MaxValue;
                    for (int k = 0; k < placed.Length; k++)
                        gap = math.min(gap, math.distance(p, placed[k]));
                    if (gap > bestGap) { bestGap = gap; best = p; }
                    if (gap >= spacing) break;
                }
                return best;
            }
            default: // Vee: leader in front, the rest alternating behind to the left and right
            {
                if (i == 0) return float2.zero;
                int row = (i + 1) / 2;
                float side = (i % 2 == 1) ? -1f : 1f;
                return new float2(side * row * spacing, -row * spacing);
            }
        }
    }
}
