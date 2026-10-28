using Unity.Collections;
using Unity.Entities;

// Spacey's own collision layers (not Unity's physics layers). Set on each object with
// Targetable_Authoring, and on each weapon's projectiles in WeaponDef. Which layers hit each other,
// and which can target which, is set in the Layer Matrix asset (Tools > Spacey > Layer Matrix).
// Rename the spare layers here when you need more.
public enum CollisionLayer : byte
{
    Default = 0,
    Ship = 1,
    Bot = 2,
    Building = 3,
    Asteroid = 4,
    Projectile = 5,  // combat shots: lasers, cannons
    MiningShot = 6,  // mining lasers
    Missile = 7,     // rockets and missiles (shootable by point defense later)
    Ore = 8,
    Debris = 9,
    Spare10 = 10,
    Spare11 = 11,
    Spare12 = 12,
    Spare13 = 13,
    Spare14 = 14,
    Spare15 = 15
}

// The two matrices, one 16-bit row per layer.
//   collides[a] bit b: layer a and layer b hit each other (kept symmetric, like Unity's matrix).
//   targets[a]  bit b: something on layer a may target something on layer b.
// Friendly fire is a separate check (Combat.IsFriendly): layers say what CAN interact,
// faction says who is on your side.
public struct LayerMatrix : IComponentData
{
    public const int LayerCount = 16;

    public FixedList64Bytes<ushort> collides;
    public FixedList64Bytes<ushort> targets;

    public bool Collides(CollisionLayer a, CollisionLayer b)
    {
        int i = (int)a;
        return i < collides.Length && (collides[i] & (1 << (int)b)) != 0;
    }

    public bool CanTarget(CollisionLayer who, CollisionLayer what)
    {
        int i = (int)who;
        return i < targets.Length && (targets[i] & (1 << (int)what)) != 0;
    }

    public static LayerMatrix Empty()
    {
        var m = new LayerMatrix();
        for (int i = 0; i < LayerCount; i++)
        {
            m.collides.Add(0);
            m.targets.Add(0);
        }
        return m;
    }

    public void SetCollides(CollisionLayer a, CollisionLayer b, bool on)
    {
        Set(ref collides, (int)a, (int)b, on);
        Set(ref collides, (int)b, (int)a, on);
    }

    public void SetCanTarget(CollisionLayer who, CollisionLayer what, bool on)
    {
        Set(ref targets, (int)who, (int)what, on);
    }

    private static void Set(ref FixedList64Bytes<ushort> rows, int row, int bit, bool on)
    {
        rows[row] = (ushort)(on ? rows[row] | (1 << bit) : rows[row] & ~(1 << bit));
    }

    private void HitsEverything(CollisionLayer s)
    {
        SetCollides(s, CollisionLayer.Ship, true);
        SetCollides(s, CollisionLayer.Bot, true);
        SetCollides(s, CollisionLayer.Building, true);
        SetCollides(s, CollisionLayer.Asteroid, true);
    }

    // Used when the project has no Layer Matrix asset yet, and by its Reset.
    public static LayerMatrix Default()
    {
        LayerMatrix m = Empty();
        CollisionLayer ship = CollisionLayer.Ship, bot = CollisionLayer.Bot, building = CollisionLayer.Building,
                       asteroid = CollisionLayer.Asteroid, shot = CollisionLayer.Projectile,
                       mining = CollisionLayer.MiningShot, missile = CollisionLayer.Missile;

        // Every kind of shot hits ships, bots, buildings and asteroids.
        m.HitsEverything(shot);
        m.HitsEverything(mining);
        m.HitsEverything(missile);
        m.SetCollides(shot, missile, true); // lasers can shoot down missiles

        // Ships target anything; worker bots only rocks; buildings (towers, later) target ships, bots and missiles.
        m.SetCanTarget(ship, ship, true);
        m.SetCanTarget(ship, bot, true);
        m.SetCanTarget(ship, building, true);
        m.SetCanTarget(ship, asteroid, true);
        m.SetCanTarget(ship, missile, true);
        m.SetCanTarget(bot, asteroid, true);
        m.SetCanTarget(building, ship, true);
        m.SetCanTarget(building, bot, true);
        m.SetCanTarget(building, missile, true);
        return m;
    }
}
