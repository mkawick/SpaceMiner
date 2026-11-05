using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Keeps the player's storage blocks in step with their resource totals, once per frame, after
// everything else has earned or spent resources:
//   more of a resource → it goes into the built-in allowance, then blocks reserved for it, then
//                        disorganized blocks already holding it, then empty disorganized blocks.
//                        Anything that still doesn't fit is lost (pickups normally stop first).
//   less of a resource → it comes out of disorganized blocks first, then reserved ones, then the
//                        allowance, newest blocks first.
// It also unlocks the first 10 blocks when the player's first Storage Depot is finished, and
// publishes StorageCapacity per resource for pickups, bots and the HUD.
[UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
public partial struct StorageSystem_Dots : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<InputData_Dots>();
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityManager em = state.EntityManager;
        Entity player = Entity.Null;
        foreach (var (cargo, entity) in SystemAPI.Query<DynamicBuffer<CargoItem>>().WithAll<InputData_Dots>().WithEntityAccess())
        {
            player = entity;
            break;
        }
        if (player == Entity.Null)
            return;

        int types = AsteroidFieldRules.ResourceCount;
        if (!em.HasComponent<StorageBank>(player))
        {
            em.AddComponentData(player, new StorageBank { tier = 0 });
            DynamicBuffer<StorageLedger> newLedger = em.AddBuffer<StorageLedger>(player);
            for (int t = 0; t < types; t++) newLedger.Add(new StorageLedger());
            DynamicBuffer<StorageCapacity> newCapacity = em.AddBuffer<StorageCapacity>(player);
            for (int t = 0; t < types; t++) newCapacity.Add(new StorageCapacity { value = StorageRules.BaseCapacity });
            em.AddBuffer<StorageBlock>(player);
        }

        // The first finished depot unlocks the first tier.
        StorageBank bank = em.GetComponentData<StorageBank>(player);
        if (bank.tier == 0 && HasFinishedDepot(ref state, em, player))
        {
            bank.tier = 1;
            em.SetComponentData(player, bank);
        }
        Resize(em.GetBuffer<StorageBlock>(player), StorageRules.Blocks(bank.tier));

        DynamicBuffer<CargoItem> totals = em.GetBuffer<CargoItem>(player);
        DynamicBuffer<StorageBlock> blocks = em.GetBuffer<StorageBlock>(player);
        DynamicBuffer<StorageLedger> ledger = em.GetBuffer<StorageLedger>(player);
        DynamicBuffer<StorageCapacity> capacity = em.GetBuffer<StorageCapacity>(player);

        for (int t = 0; t < types && t < totals.Length; t++)
        {
            StorageLedger l = ledger[t];
            float total = totals[t].amount;
            float delta = total - l.lastTotal;
            if (delta > 0.0001f)
            {
                float lost = Fill(blocks, (sbyte)t, delta, ref l);
                if (lost > 0.0001f)
                {
                    total -= lost;
                    totals[t] = new CargoItem { amount = total };
                }
            }
            else if (delta < -0.0001f)
            {
                Drain(blocks, (sbyte)t, -delta, ref l);
            }
            l.lastTotal = total;
            ledger[t] = l;
        }

        int emptyLoose = 0;
        for (int b = 0; b < blocks.Length; b++)
            if (blocks[b].designation < 0 && blocks[b].content < 0)
                emptyLoose++;
        for (int t = 0; t < types && t < totals.Length; t++)
        {
            float free = StorageRules.BaseCapacity - ledger[t].inBase + emptyLoose * StorageRules.BlockSize;
            for (int b = 0; b < blocks.Length; b++)
            {
                StorageBlock k = blocks[b];
                bool reservedForIt = k.designation == t && (k.content < 0 || k.content == t);
                bool looseWithIt = k.designation < 0 && k.content == t;
                if (reservedForIt || looseWithIt)
                    free += StorageRules.BlockSize - k.amount;
            }
            capacity[t] = new StorageCapacity { value = totals[t].amount + math.max(0f, free) };
        }
    }

    private bool HasFinishedDepot(ref SystemState state, EntityManager em, Entity player)
    {
        CombatFaction mine = em.HasComponent<Targetable>(player) ? em.GetComponentData<Targetable>(player).faction : CombatFaction.None;
        foreach (RefRO<Building> building in SystemAPI.Query<RefRO<Building>>())
            if (building.ValueRO.id == BuildingId.Storage && building.ValueRO.IsComplete
                && (mine == CombatFaction.None || building.ValueRO.faction == mine))
                return true;
        return false;
    }

    private static void Resize(DynamicBuffer<StorageBlock> blocks, int count)
    {
        while (blocks.Length < count)
            blocks.Add(new StorageBlock { designation = -1, content = -1, amount = 0f });
    }

    // Returns what didn't fit.
    private static float Fill(DynamicBuffer<StorageBlock> blocks, sbyte type, float amount, ref StorageLedger l)
    {
        float take = math.min(amount, StorageRules.BaseCapacity - l.inBase);
        if (take > 0f) { l.inBase += take; amount -= take; }

        for (int pass = 0; pass < 3 && amount > 0.0001f; pass++)
        {
            for (int b = 0; b < blocks.Length && amount > 0.0001f; b++)
            {
                StorageBlock k = blocks[b];
                bool fits = pass == 0 ? k.designation == type && (k.content < 0 || k.content == type)
                          : pass == 1 ? k.designation < 0 && k.content == type
                          : k.designation < 0 && k.content < 0;
                if (!fits)
                    continue;
                float room = StorageRules.BlockSize - k.amount;
                if (room <= 0f)
                    continue;
                float put = math.min(room, amount);
                k.amount += put;
                k.content = type;
                amount -= put;
                blocks[b] = k;
            }
        }
        return math.max(0f, amount);
    }

    private static void Drain(DynamicBuffer<StorageBlock> blocks, sbyte type, float amount, ref StorageLedger l)
    {
        for (int pass = 0; pass < 2 && amount > 0.0001f; pass++)
        {
            for (int b = blocks.Length - 1; b >= 0 && amount > 0.0001f; b--)
            {
                StorageBlock k = blocks[b];
                bool match = k.content == type && (pass == 0 ? k.designation < 0 : k.designation >= 0);
                if (!match)
                    continue;
                float take = math.min(k.amount, amount);
                k.amount -= take;
                amount -= take;
                if (k.amount <= 0.0001f)
                {
                    k.amount = 0f;
                    k.content = -1;
                }
                blocks[b] = k;
            }
        }
        if (amount > 0.0001f)
            l.inBase = math.max(0f, l.inBase - amount);
    }
}
