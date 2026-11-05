using Unity.Entities;
using Unity.Mathematics;

// The player's storage. Every resource has a small built-in allowance (BaseCapacity). A Storage
// Depot adds blocks of BlockSize: 10 blocks (2,000) at first, upgradeable to 4k, 7k, 12k, 17k
// and 25k. Each block can be reserved for one resource (designation) or left disorganized, in
// which case it takes whatever comes in. A block only ever holds one resource at a time.
//
// The player's CargoItem buffer stays the one true total per resource: everything that earns or
// spends resources keeps using it. StorageSystem_Dots works out which blocks those totals sit in,
// and publishes how much of each resource fits (StorageCapacity) so pickups stop when full.
public struct StorageBank : IComponentData
{
    public int tier; // 0 = no depot yet, 1 = 2k, 2 = 4k, 3 = 7k, 4 = 12k, 5 = 17k, 6 = 25k
}

public struct StorageBlock : IBufferElementData
{
    public sbyte designation; // resource type this block is reserved for; -1 = disorganized
    public sbyte content;     // resource type in it; -1 = empty
    public float amount;
}

// Per resource type: how much sits in the built-in allowance, and the total last seen.
public struct StorageLedger : IBufferElementData
{
    public float inBase;
    public float lastTotal;
}

// Per resource type: the most the player can hold right now (stored + free space it could use).
public struct StorageCapacity : IBufferElementData
{
    public float value;
}

public static class StorageRules
{
    public const float BlockSize = 200f;
    public const float BaseCapacity = 200f;
    public const int MaxTier = 6;

    public static int Blocks(int tier)
    {
        switch (tier)
        {
            case 0: return 0;
            case 1: return 10;
            case 2: return 20;
            case 3: return 35;
            case 4: return 60;
            case 5: return 85;
            default: return 125;
        }
    }

    public static float TierCapacity(int tier) => Blocks(tier) * BlockSize;

    // Grid width for a block count: an exact rectangle, a bit wider than tall
    // (10 → 5×2, 20 → 5×4, 35 → 7×5, 60 → 10×6, 85 → 17×5, 125 → 25×5).
    public static int Columns(int blocks)
    {
        if (blocks <= 0)
            return 1;
        int best = blocks;
        float bestScore = float.MaxValue;
        for (int cols = 1; cols <= blocks; cols++)
        {
            if (blocks % cols != 0)
                continue;
            int rows = blocks / cols;
            if (cols < rows)
                continue;
            float score = math.abs((float)cols / rows - 2f);
            if (score < bestScore)
            {
                bestScore = score;
                best = cols;
            }
        }
        return best;
    }

    // Cost of upgrading to the given tier (2..6). Paid from storage.
    public static ResourceCost UpgradeCost(int toTier)
    {
        switch (toTier)
        {
            case 2: return new ResourceCost { iron = 300, silicate = 200, copper = 100 };
            case 3: return new ResourceCost { iron = 600, silicate = 400, copper = 200, carbon = 100 };
            case 4: return new ResourceCost { iron = 1200, silicate = 800, copper = 400, carbon = 200, gold = 50 };
            case 5: return new ResourceCost { iron = 2000, silicate = 1400, copper = 700, carbon = 400, gold = 120 };
            default: return new ResourceCost { iron = 3200, silicate = 2200, copper = 1100, carbon = 700, gold = 250, fermite = 50 };
        }
    }
}
